import { store } from "../Scripts/store.js";
import * as api from "../Scripts/management-api.js";
import { computeAccessKey, updateAccessKeyCookie } from "../Scripts/auth.js";
import Modal from "../Scripts/Modal.js";

const emptyCredential = () => ({ Name: "", AccessKey: "", Kind: "Machine", Enable: true, Scopes: [] });

// Named permission scopes (see OrbitMesh.Server's OrbitMeshScope) grouped for the matrix below - each
// entry is one scope a credential can be granted, explicit and individually-grantable (PAT-style,
// like GitHub's token scopes).
const scopeCatalog = [
    { category: "Edges", scopes: [
        { value: "edges:read", label: "Read", description: "View edges and their live connection state." },
        { value: "edges:write", label: "Write", description: "Add, remove or restart edges." }
    ] },
    { category: "Packages", scopes: [
        { value: "packages:read", label: "Read", description: "View package instances, live status, logs and settings." },
        { value: "packages:control", label: "Control", description: "Start, stop, restart or reload a running package." },
        { value: "packages:manage", label: "Manage", description: "Add, remove or reconfigure a package instance (settings, groups, recovery)." },
        { value: "packages:deploy", label: "Deploy", description: "Upload, install or remove packages in the Package Repository." }
    ] },
    { category: "Telemetry", scopes: [
        { value: "telemetry:read", label: "Read", description: "View telemetry item values." },
        { value: "telemetry:purge", label: "Purge", description: "Delete stored telemetry item history." }
    ] },
    { category: "Credentials", scopes: [
        { value: "credentials:read", label: "Read", description: "View the list of credentials (never their keys)." },
        { value: "credentials:manage", label: "Manage", description: "Create, edit, remove or reset credentials - the most sensitive scope." }
    ] },
    { category: "Configuration", scopes: [
        { value: "configuration:read", label: "Read", description: "View the raw server configuration." },
        { value: "configuration:write", label: "Write", description: "Edit the raw server configuration or global recovery options." }
    ] },
    { category: "Updates", scopes: [
        { value: "updates:manage", label: "Manage", description: "Check for and apply Server/Console/package updates." }
    ] },
    { category: "Other", scopes: [
        { value: "developer", label: "Developer tools", description: "Direct developer workflows (messages, telemetry) without deploying a package to a real edge." }
    ] }
];
const allScopeValues = scopeCatalog.flatMap((g) => g.scopes.map((s) => s.value));

// crypto.randomUUID() x2 (minus dashes) gives 60 hex chars from a real CSPRNG - plenty of entropy for
// a Machine bearer token, and matches the look of the hex AccessKeys this project already uses.
function generateAccessKey() {
    return (crypto.randomUUID() + crypto.randomUUID()).replace(/-/g, "");
}

export default {
    components: { Modal },
    data() {
        return {
            store, filter: "", kindFilter: "", expanded: null, editCredential: null, editResetKey: false,
            editNewPassword: "", authExpanded: null, editAuth: null,
            newCredential: emptyCredential(), newCredentialPassword: "",
            creating: false, saving: false, savingAuth: false, scopeCatalog,
            lockedIps: [], unlocking: {}, lockoutPollHandle: null,
            // A credential's real secret is only ever known right after it's created or reset (see
            // upsert()'s callers below) - nothing re-displays it afterwards, matching how GitHub/most
            // platforms handle PATs. revealDialog holds it just long enough for the admin to copy it.
            revealDialog: null, revealAck: false
        };
    },
    mounted() {
        // A hard reload straight to this route can mount before managementAvailable turns true.
        store.onManagementAvailable(() => {
            store.loadCredentials();
            this.loadLockedIps();
            // Lockouts expire/clear on their own too - poll to reflect that without a manual refresh.
            this.lockoutPollHandle = setInterval(() => this.loadLockedIps(), 5000);
        });
    },
    beforeUnmount() {
        if (this.lockoutPollHandle) {
            clearInterval(this.lockoutPollHandle);
        }
    },
    computed: {
        filtered() {
            const term = this.filter.toLowerCase();
            return (store.credentials || [])
                .filter((c) => (!term || c.Name.toLowerCase().includes(term)) && (!this.kindFilter || c.Kind === this.kindFilter))
                .sort((a, b) => a.Name.localeCompare(b.Name));
        }
    },
    methods: {
        // afterSave runs right after the POST succeeds but before the refresh below - the one place
        // saveCredential() can safely swap the session over to a just-reset password (see there): the
        // POST itself still authenticates with the OLD key (correct - the server hasn't changed yet),
        // and the refresh needs the NEW one (correct - the server just did). Doing the swap any earlier
        // risks a client/server mismatch if the POST itself fails for an unrelated reason (rate limit,
        // network) - the local session would claim a password the server never actually accepted.
        async upsert(credential, afterSave) {
            await api.upsertCredential(credential.Name, credential.AccessKey, credential.Kind, credential.Enable, credential.Scopes);
            if (afterSave) {
                afterSave();
            }
            // GetCredentials reads IOptionsMonitor's CurrentValue, which only catches up with the write
            // above once its file-watcher reload fires - an immediate reload can race and read stale
            // data (same class of bug fixed for the package deploy flow in Packages.js).
            await new Promise((resolve) => setTimeout(resolve, 500));
            try {
                await store.loadCredentials();
            } catch (err) {
                // The upsert itself already succeeded (the line above didn't throw) - this refresh is
                // just convenience, so a hiccup here (rate limit, network) shouldn't take down the whole
                // save flow and hide the "here's your new secret" reveal dialog with it, which is the
                // one thing that must not silently disappear here.
                store.notify("Saved, but couldn't refresh the credentials list - reload the page to see it.", "error");
                console.error(err);
            }
        },
        async toggleEnable(credential) {
            await this.upsert({ ...credential, AccessKey: "", Enable: !credential.Enable });
        },
        reveal(name, label, value) {
            this.revealAck = false;
            this.revealDialog = { name, label, value };
        },
        closeReveal() {
            // Ignored until acknowledged - see the checkbox in the template. The secret was already
            // sent to the server; this is just making sure the admin actually copied it down first.
            if (!this.revealAck) {
                return;
            }
            this.revealDialog = null;
        },
        async copyRevealed() {
            try {
                await navigator.clipboard.writeText(this.revealDialog.value);
                store.notify("Copied to clipboard.", "success");
            } catch {
                store.notify("Could not copy automatically - select and copy the value manually.", "error");
            }
        },
        lastUsedLabel(credential) {
            return credential.LastUsedUtc ? new Date(credential.LastUsedUtc).toLocaleString() : "Never";
        },
        scopesSummary(credential) {
            const scopes = credential.Scopes || [];
            if (scopes.length === 0) {
                return "None";
            }
            return scopes.length === allScopeValues.length ? "All" : `${scopes.length} scope${scopes.length === 1 ? "" : "s"}`;
        },
        scopesTitle(credential) {
            return (credential.Scopes || []).join(", ") || "No scopes granted.";
        },
        async remove(credential) {
            if (!(await store.confirm(`Remove credential '${credential.Name}'? Any package or edge using it will be denied access.`))) {
                return;
            }
            await api.removeCredential(credential.Name);
            await new Promise((resolve) => setTimeout(resolve, 500));
            await store.loadCredentials();
        },
        toggleManage(credential) {
            if (this.expanded === credential.Name) {
                this.expanded = null;
                return;
            }
            this.expanded = credential.Name;
            this.authExpanded = null;
            this.editResetKey = false;
            this.editNewPassword = "";
            // AccessKey starts blank regardless of Kind - leaving it blank means "unchanged" unless
            // Generate new/Reset is used - saveCredential should never need it just to flip Enable/scopes.
            this.editCredential = { ...credential, AccessKey: "", Scopes: [...(credential.Scopes || [])] };
        },
        async saveCredential() {
            this.saving = true;
            try {
                // Whichever raw secret is about to be sent (Machine key or Human's new password) has to
                // be captured here, before upsert() clears/hashes it - it's the only moment it exists.
                let revealed = null;
                let afterSave = null;
                if (this.editResetKey && this.editCredential.Kind === "Human") {
                    if (!this.editNewPassword) {
                        store.notify("Enter a new password first.", "error");
                        return;
                    }
                    revealed = { label: "New password", value: this.editNewPassword };
                    this.editCredential.AccessKey = await computeAccessKey(this.editCredential.Name, this.editNewPassword);
                    if (this.editCredential.Name === store.username) {
                        // Resetting the signed-in admin's OWN password: the server now only accepts the
                        // new hash, so the session cookie/REST client have to be swapped over to match -
                        // but only once the save itself has actually succeeded (see upsert()'s afterSave
                        // param), or every Management API request after a failed save keeps 403ing with
                        // a local session claiming a password the server never actually accepted.
                        const newAccessKey = this.editCredential.AccessKey;
                        afterSave = () => {
                            updateAccessKeyCookie(newAccessKey);
                            api.updateAccessKey(newAccessKey);
                            store.accessKey = newAccessKey;
                        };
                    }
                } else if (this.editCredential.Kind === "Machine" && this.editCredential.AccessKey) {
                    revealed = { label: "Access Key", value: this.editCredential.AccessKey };
                }
                await this.upsert(this.editCredential, afterSave);
                this.expanded = null;
                this.editResetKey = false;
                this.editNewPassword = "";
                if (revealed) {
                    this.reveal(this.editCredential.Name, revealed.label, revealed.value);
                }
            } finally {
                this.saving = false;
            }
        },
        generateNewAccessKey(target) {
            target.AccessKey = generateAccessKey();
        },
        async createCredential() {
            if (!this.newCredential.Name) {
                store.notify("Name is required.", "error");
                return;
            }
            this.creating = true;
            try {
                // Human doesn't need a reveal dialog afterwards: the secret is the password the admin
                // just typed, which they already know - nothing new to copy down. Machine still needs
                // one, same as ever - it's a generated/typed bearer token, not something memorized.
                let revealed = null;
                if (this.newCredential.Kind === "Human") {
                    if (!this.newCredentialPassword) {
                        store.notify("Password is required.", "error");
                        return;
                    }
                    // Same derivation login() uses to authenticate - the credential's Name doubles as
                    // its login here.
                    this.newCredential.AccessKey = await computeAccessKey(this.newCredential.Name, this.newCredentialPassword);
                } else {
                    if (!this.newCredential.AccessKey) {
                        store.notify("Access Key is required (or use Generate).", "error");
                        return;
                    }
                    revealed = { label: "Access Key", value: this.newCredential.AccessKey };
                }
                const name = this.newCredential.Name;
                await this.upsert(this.newCredential);
                this.newCredential = emptyCredential();
                this.newCredentialPassword = "";
                if (revealed) {
                    this.reveal(name, revealed.label, revealed.value);
                }
            } finally {
                this.creating = false;
            }
        },
        async toggleAuthorizations(credential) {
            if (this.authExpanded === credential.Name) {
                this.authExpanded = null;
                return;
            }
            const authorizations = await api.getCredentialAuthorizations(credential.Name);
            authorizations.Messages ||= { DefaultAuthorization: "Allow", Rules: [] };
            authorizations.Groups ||= { DefaultAuthorization: "Allow", Rules: [] };
            authorizations.TelemetryItems ||= { DefaultAuthorization: "Allow", Rules: [] };
            this.editAuth = authorizations;
            this.authExpanded = credential.Name;
            this.expanded = null;
        },
        addMessageRule() {
            this.editAuth.Messages.Rules.push({ Authorization: "Allow", Scope: "All", Args: "", MessageKey: "" });
        },
        addGroupRule() {
            this.editAuth.Groups.Rules.push({ Authorization: "Allow", GroupName: "" });
        },
        addTelemetryItemRule() {
            this.editAuth.TelemetryItems.Rules.push({ Authorization: "Allow", EdgeName: "*", PackageName: "*", Name: "*" });
        },
        async saveAuthorizations(credential) {
            this.savingAuth = true;
            try {
                await api.setCredentialAuthorizations(credential.Name, this.editAuth);
                this.authExpanded = null;
            } finally {
                this.savingAuth = false;
            }
        },
        async loadLockedIps() {
            try {
                this.lockedIps = await api.getLockedOutIps();
            } catch {
                // Next poll tick just tries again.
            }
        },
        lockedUntilLabel(lockout) {
            return new Date(lockout.LockedUntilUtc).toLocaleString();
        },
        async unlockIp(lockout) {
            this.unlocking[lockout.Ip] = true;
            try {
                await api.unlockIp(lockout.Ip);
                await this.loadLockedIps();
            } finally {
                this.unlocking[lockout.Ip] = false;
            }
        }
    },
    template: `
        <div>
            <h2>Credentials</h2>
            <div class="deploy-form">
                <h3>Add credential</h3>
                <label><input type="radio" value="Machine" v-model="newCredential.Kind" /> Machine (Edge/package token)</label>
                <label><input type="radio" value="Human" v-model="newCredential.Kind" /> Human (console login)</label>
                <input v-model="newCredential.Name" placeholder="Name" />
                <template v-if="newCredential.Kind === 'Machine'">
                    <input v-model="newCredential.AccessKey" placeholder="Access Key" style="width:260px" />
                    <button type="button" @click="generateNewAccessKey(newCredential)">Generate</button>
                </template>
                <input v-else v-model="newCredentialPassword" type="password" placeholder="Password" style="width:180px" />
                <label><input type="checkbox" v-model="newCredential.Enable" /> Enable</label>
                <div class="scope-matrix">
                    <div v-for="group in scopeCatalog" :key="group.category" class="scope-group">
                        <strong>{{ group.category }}</strong>
                        <label v-for="s in group.scopes" :key="s.value" :title="s.description">
                            <input type="checkbox" :value="s.value" v-model="newCredential.Scopes" /> {{ s.label }}
                        </label>
                    </div>
                </div>
                <button :disabled="creating" @click="createCredential">{{ creating ? 'Adding...' : 'Add' }}</button>
            </div>
            <div class="deploy-form">
                <strong>What these rights are for</strong>
                <ul style="margin:6px 0 0 18px;">
                    <li><strong>Machine</strong> — an Edge or package's token; stored encrypted (the Server can still decrypt it to hand back out when launching packages), shown once at creation/reset and never again.</li>
                    <li><strong>Human</strong> — a console login; stored as a one-way hash, never shown or resubmitted once set.</li>
                    <li><strong>Scopes</strong> — named permissions (hover a checkbox for details). Grant only what a credential actually needs - e.g. a read-only demo account gets just the *:read scopes.</li>
                </ul>
            </div>
            <div v-if="lockedIps.length > 0" class="deploy-form">
                <h3>Locked-out IPs</h3>
                <p style="margin-top:-4px;color:var(--text-muted,#888)">Too many failed AccessKey attempts (brute-force protection) - blocked regardless of what credential they try next, until the lockout expires or is cleared here.</p>
                <table class="data-table">
                    <thead>
                        <tr><th>IP</th><th style="width:140px">Failed attempts</th><th style="width:200px">Locked until</th><th style="width:120px">Actions</th></tr>
                    </thead>
                    <tbody>
                        <tr v-for="lockout in lockedIps" :key="lockout.Ip">
                            <td>{{ lockout.Ip }}</td>
                            <td>{{ lockout.FailureCount }}</td>
                            <td>{{ lockedUntilLabel(lockout) }}</td>
                            <td class="actions">
                                <button :disabled="unlocking[lockout.Ip]" @click="unlockIp(lockout)">{{ unlocking[lockout.Ip] ? 'Unlocking...' : 'Unlock' }}</button>
                            </td>
                        </tr>
                    </tbody>
                </table>
            </div>
            <p>
                <input v-model="filter" placeholder="Filter by name" style="width: 260px;" />
                <select v-model="kindFilter" style="margin-left:8px">
                    <option value="">All kinds</option>
                    <option value="Machine">Machine</option>
                    <option value="Human">Human</option>
                </select>
            </p>
            <table class="data-table">
                <thead>
                    <tr>
                        <th style="width:140px">Name</th><th style="width:100px">Kind</th><th style="width:100px">Enable</th>
                        <th>Scopes</th>
                        <th style="width:150px">Last used</th>
                        <th style="width:410px">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    <template v-for="credential in filtered" :key="credential.Name">
                        <tr>
                            <td>{{ credential.Name }}</td>
                            <td><span class="badge" :class="credential.Kind === 'Human' ? 'allow' : ''">{{ credential.Kind }}</span></td>
                            <td><span class="badge" :class="credential.Enable ? 'connected' : 'disconnected'">{{ credential.Enable ? 'Enabled' : 'Disabled' }}</span></td>
                            <td :title="scopesTitle(credential)">{{ scopesSummary(credential) }}</td>
                            <td>{{ lastUsedLabel(credential) }}</td>
                            <td class="actions">
                                <button @click="toggleEnable(credential)">{{ credential.Enable ? 'Disable' : 'Enable' }}</button>
                                <button @click="toggleManage(credential)">{{ expanded === credential.Name ? 'Close' : 'Manage' }}</button>
                                <button @click="toggleAuthorizations(credential)">{{ authExpanded === credential.Name ? 'Close' : 'Authorizations' }}</button>
                                <button class="btn-danger" @click="remove(credential)">Remove</button>
                            </td>
                        </tr>
                        <tr v-if="expanded === credential.Name">
                            <td colspan="6" class="detail-panel">
                                <div class="detail-columns" style="grid-template-columns: 1fr;">
                                    <div>
                                        <h4>Edit '{{ editCredential.Name }}' ({{ editCredential.Kind }})</h4>
                                        <div class="setting-row" v-if="editCredential.Kind === 'Machine'">
                                            <label>Access Key</label>
                                            <input :placeholder="'Leave blank to keep the current key'" v-model="editCredential.AccessKey" style="width:320px" />
                                            <button type="button" @click="generateNewAccessKey(editCredential)">Generate new</button>
                                        </div>
                                        <div class="setting-row" v-else-if="!editResetKey">
                                            <label>Access Key</label>
                                            <span class="muted">hashed - cannot be viewed</span>
                                            <button type="button" @click="editResetKey = true">Reset password</button>
                                        </div>
                                        <div class="setting-row" v-else>
                                            <label>New password</label>
                                            <input v-model="editNewPassword" type="password" style="width:200px" />
                                            <button type="button" @click="editResetKey = false; editNewPassword = ''">Cancel reset</button>
                                        </div>
                                        <label><input type="checkbox" v-model="editCredential.Enable" /> Enable</label>
                                        <div class="scope-matrix">
                                            <div v-for="group in scopeCatalog" :key="group.category" class="scope-group">
                                                <strong>{{ group.category }}</strong>
                                                <label v-for="s in group.scopes" :key="s.value" :title="s.description">
                                                    <input type="checkbox" :value="s.value" v-model="editCredential.Scopes" /> {{ s.label }}
                                                </label>
                                            </div>
                                        </div>
                                        <div style="margin-top:8px;"><button :disabled="saving" @click="saveCredential">{{ saving ? 'Saving...' : 'Save' }}</button></div>
                                    </div>
                                </div>
                            </td>
                        </tr>
                        <tr v-if="authExpanded === credential.Name">
                            <td colspan="6" class="detail-panel">
                                <div class="detail-columns" style="grid-template-columns: repeat(3, 1fr);">
                                    <div>
                                        <h4>Message authorizations</h4>
                                        <label>Default:
                                            <select v-model="editAuth.Messages.DefaultAuthorization">
                                                <option value="Allow">Allow</option>
                                                <option value="Deny">Deny</option>
                                            </select>
                                        </label>
                                        <div v-for="(rule, i) in editAuth.Messages.Rules" :key="i" class="rule-row">
                                            <div class="rule-field"><label>Authorization</label><select v-model="rule.Authorization"><option value="Allow">Allow</option><option value="Deny">Deny</option></select></div>
                                            <div class="rule-field">
                                                <label>Scope</label>
                                                <select v-model="rule.Scope">
                                                    <option value="Group">Group</option><option value="Package">Package</option>
                                                    <option value="Edge">Edge</option><option value="Others">Others</option><option value="All">All</option>
                                                </select>
                                            </div>
                                            <div class="rule-field"><label>Args</label><input v-model="rule.Args" style="width:90px" /></div>
                                            <div class="rule-field"><label>Message key</label><input v-model="rule.MessageKey" style="width:110px" /></div>
                                            <button class="btn-danger" @click="editAuth.Messages.Rules.splice(i, 1)">Remove</button>
                                        </div>
                                        <button @click="addMessageRule">Add rule</button>
                                    </div>
                                    <div>
                                        <h4>Telemetry item authorizations</h4>
                                        <label>Default:
                                            <select v-model="editAuth.TelemetryItems.DefaultAuthorization">
                                                <option value="Allow">Allow</option>
                                                <option value="Deny">Deny</option>
                                            </select>
                                        </label>
                                        <div v-for="(rule, i) in editAuth.TelemetryItems.Rules" :key="i" class="rule-row">
                                            <div class="rule-field"><label>Authorization</label><select v-model="rule.Authorization"><option value="Allow">Allow</option><option value="Deny">Deny</option></select></div>
                                            <div class="rule-field"><label>Edge</label><input v-model="rule.EdgeName" style="width:80px" /></div>
                                            <div class="rule-field"><label>Package</label><input v-model="rule.PackageName" style="width:80px" /></div>
                                            <div class="rule-field"><label>Name</label><input v-model="rule.Name" style="width:80px" /></div>
                                            <button class="btn-danger" @click="editAuth.TelemetryItems.Rules.splice(i, 1)">Remove</button>
                                        </div>
                                        <button @click="addTelemetryItemRule">Add rule</button>
                                    </div>
                                    <div>
                                        <h4>Group authorizations</h4>
                                        <label>Default:
                                            <select v-model="editAuth.Groups.DefaultAuthorization">
                                                <option value="Allow">Allow</option>
                                                <option value="Deny">Deny</option>
                                            </select>
                                        </label>
                                        <div v-for="(rule, i) in editAuth.Groups.Rules" :key="i" class="rule-row">
                                            <div class="rule-field"><label>Authorization</label><select v-model="rule.Authorization"><option value="Allow">Allow</option><option value="Deny">Deny</option></select></div>
                                            <div class="rule-field"><label>Group name</label><input v-model="rule.GroupName" style="width:130px" /></div>
                                            <button class="btn-danger" @click="editAuth.Groups.Rules.splice(i, 1)">Remove</button>
                                        </div>
                                        <button @click="addGroupRule">Add rule</button>
                                    </div>
                                </div>
                                <button :disabled="savingAuth" @click="saveAuthorizations(credential)" style="margin-top:8px;">{{ savingAuth ? 'Saving...' : 'Save authorizations' }}</button>
                            </td>
                        </tr>
                    </template>
                    <tr v-if="filtered.length === 0"><td colspan="6" class="empty">No credentials yet.</td></tr>
                </tbody>
            </table>
            <modal :show="!!revealDialog" :title="revealDialog ? revealDialog.name + ' - ' + revealDialog.label : ''" @close="closeReveal">
                <template v-if="revealDialog">
                    <p>This is shown <strong>once</strong>. Copy it now - it cannot be displayed again afterwards.</p>
                    <p><input :value="revealDialog.value" readonly style="width:100%;font-family:monospace" @focus="$event.target.select()" /></p>
                    <button type="button" @click="copyRevealed">Copy</button>
                    <label style="display:block;margin-top:12px;"><input type="checkbox" v-model="revealAck" /> I've saved this value</label>
                </template>
                <template #footer>
                    <button :disabled="!revealAck" @click="closeReveal">Close</button>
                </template>
            </modal>
        </div>
    `
};
