import { store } from "../Scripts/store.js";
import * as api from "../Scripts/management-api.js";

export default {
    data() {
        return {
            store, expanded: null, editCredential: "", saving: false, removing: {}, restarting: {}, checkingUpdate: {},
            newEdge: { name: "", credential: "" }, creating: false,
            pendingEdges: [], pendingNames: {}, approving: {}, dismissing: {}, pendingPollHandle: null,
            revealDialog: null, revealAck: false
        };
    },
    mounted() {
        // A hard reload straight to this route can mount before managementAvailable turns true.
        store.onManagementAvailable(() => {
            store.loadEdges();
            store.loadCredentials();
            this.loadPendingEdges();
            // Pending attempts aren't pushed live - poll for new ones.
            this.pendingPollHandle = setInterval(() => this.loadPendingEdges(), 5000);
        });
    },
    beforeUnmount() {
        if (this.pendingPollHandle) {
            clearInterval(this.pendingPollHandle);
        }
    },
    computed: {
        edgeList() {
            return Object.values(store.edges).sort((a, b) => a.Description.EdgeName.localeCompare(b.Description.EdgeName));
        }
    },
    methods: {
        packageCount(edgeName) {
            return Object.values(store.packages).filter((p) => p.EdgeName === edgeName).length;
        },
        // GetEdges reads IOptionsMonitor's CurrentValue, which only catches up with a config
        // write once its file-watcher reload fires - an immediate reload can race and read stale
        // data (same class of bug fixed for credentials and package deploys).
        async afterConfigWrite() {
            await new Promise((resolve) => setTimeout(resolve, 500));
            await store.loadEdges();
        },
        async createEdge() {
            if (!this.newEdge.name || !this.newEdge.credential) {
                store.notify("Name and credential are both required.", "error");
                return;
            }
            this.creating = true;
            try {
                await api.upsertEdge(this.newEdge.name, this.newEdge.credential);
                await this.afterConfigWrite();
                this.newEdge = { name: "", credential: "" };
            } finally {
                this.creating = false;
            }
        },
        toggleManage(s) {
            const name = s.Description.EdgeName;
            if (this.expanded === name) {
                this.expanded = null;
                return;
            }
            this.expanded = name;
            this.editCredential = s.Credential || "";
        },
        async saveCredential(s) {
            this.saving = true;
            try {
                await api.upsertEdge(s.Description.EdgeName, this.editCredential);
                await this.afterConfigWrite();
                this.expanded = null;
            } finally {
                this.saving = false;
            }
        },
        async remove(s) {
            const name = s.Description.EdgeName;
            if (!(await store.confirm(`Remove edge '${name}'? Its packages will no longer be able to connect.`))) {
                return;
            }
            this.removing[name] = true;
            try {
                await api.removeEdge(name);
                await this.afterConfigWrite();
            } finally {
                this.removing[name] = false;
            }
        },
        async restart(s) {
            const name = s.Description.EdgeName;
            if (!(await store.confirm(`Restart edge '${name}'? It will reconnect after the process relaunches.`))) {
                return;
            }
            this.restarting[name] = true;
            try {
                await store.controller.server.restartEdge(name);
            } finally {
                this.restarting[name] = false;
            }
        },
        async checkForUpdate(s) {
            const name = s.Description.EdgeName;
            this.checkingUpdate[name] = true;
            try {
                await store.controller.server.checkForEdgeUpdate(name);
                store.notify(`Update check requested for '${name}'.`, "success");
            } finally {
                this.checkingUpdate[name] = false;
            }
        },
        async loadPendingEdges() {
            try {
                const pending = await api.getPendingEdges();
                this.pendingEdges = pending;
                // Pre-fill with the declared name, but don't clobber an in-progress edit.
                for (const p of pending) {
                    if (!(p.InstanceId in this.pendingNames)) {
                        this.pendingNames[p.InstanceId] = p.EdgeName;
                    }
                }
            } catch {
                // Next poll tick just tries again.
            }
        },
        async approvePending(p) {
            const name = (this.pendingNames[p.InstanceId] || "").trim();
            if (!name) {
                store.notify("Name is required.", "error");
                return;
            }
            this.approving[p.InstanceId] = true;
            try {
                const result = await api.approvePendingEdge(p.InstanceId, name);
                await this.loadPendingEdges();
                await store.loadEdges();
                await store.loadCredentials();
                this.revealAck = false;
                this.revealDialog = { name: result.name, label: "Access Key", pushed: result.pushed, value: result.accessKey };
            } finally {
                this.approving[p.InstanceId] = false;
            }
        },
        async dismissPending(p) {
            if (!(await store.confirm(`Dismiss '${p.EdgeName}'? It can reappear here if it keeps trying to connect.`))) {
                return;
            }
            this.dismissing[p.InstanceId] = true;
            try {
                await api.dismissPendingEdge(p.InstanceId);
                await this.loadPendingEdges();
            } finally {
                this.dismissing[p.InstanceId] = false;
            }
        },
        closeReveal() {
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
        }
    },
    template: `
        <div>
            <h2>Edges</h2>
            <div class="deploy-form">
                <h3>Add edge</h3>
                <input v-model="newEdge.name" placeholder="Edge name" />
                <select v-model="newEdge.credential">
                    <option value="" disabled>Credential...</option>
                    <option v-for="c in (store.credentials || [])" :key="c.Name" :value="c.Name">{{ c.Name }}</option>
                </select>
                <button :disabled="creating" @click="createEdge">{{ creating ? 'Adding...' : 'Add' }}</button>
            </div>
            <div v-if="store.managementAvailable && pendingEdges.length > 0" class="deploy-form">
                <h3>Pending edges</h3>
                <p style="margin-top:-4px;color:var(--text-muted,#888)">Connected but not yet authorized - a matching credential and edge entry are created for you on approval.</p>
                <table class="data-table">
                    <thead>
                        <tr><th>Declared name</th><th style="width:140px">From</th><th style="width:150px">First seen</th><th style="width:150px">Last seen</th><th style="width:260px">Approve as</th><th style="width:160px">Actions</th></tr>
                    </thead>
                    <tbody>
                        <tr v-for="p in pendingEdges" :key="p.InstanceId">
                            <td>{{ p.EdgeName }}</td>
                            <td>{{ p.RemoteIp }}</td>
                            <td>{{ new Date(p.FirstSeenUtc).toLocaleString() }}</td>
                            <td>{{ new Date(p.LastSeenUtc).toLocaleString() }}</td>
                            <td><input v-model="pendingNames[p.InstanceId]" placeholder="Name" style="width:100%" /></td>
                            <td class="actions">
                                <button :disabled="approving[p.InstanceId]" @click="approvePending(p)">{{ approving[p.InstanceId] ? 'Approving...' : 'Approve' }}</button>
                                <button class="btn-danger" :disabled="dismissing[p.InstanceId]" @click="dismissPending(p)">Dismiss</button>
                            </td>
                        </tr>
                    </tbody>
                </table>
            </div>
            <table class="data-table">
                <thead>
                    <tr><th style="width:150px">Name</th><th style="width:130px">Status</th><th style="width:150px">Machine</th><th>OS</th><th style="width:90px">Runtime</th><th style="width:90px">Version</th><th style="width:130px">Credential</th><th style="width:90px">Packages</th><th style="width:420px">Actions</th></tr>
                </thead>
                <tbody>
                    <template v-for="s in edgeList" :key="s.Description.EdgeName">
                        <tr>
                            <td>{{ s.Description.EdgeName }}</td>
                            <td><span class="badge" :class="s.IsConnected ? 'connected' : 'disconnected'">{{ s.IsConnected ? 'Connected' : 'Disconnected' }}</span></td>
                            <td>{{ s.Description.MachineName }}</td>
                            <td>{{ s.Description.OSCaption }}</td>
                            <td>{{ s.Description.FxVersion }}</td>
                            <td>{{ s.Description.Version || '-' }}</td>
                            <td>{{ s.Credential || '-' }}</td>
                            <td>{{ packageCount(s.Description.EdgeName) }}</td>
                            <td class="actions" v-if="store.managementAvailable">
                                <button @click="toggleManage(s)">{{ expanded === s.Description.EdgeName ? 'Close' : 'Manage' }}</button>
                                <button :disabled="restarting[s.Description.EdgeName]" @click="restart(s)">{{ restarting[s.Description.EdgeName] ? 'Restarting...' : 'Restart' }}</button>
                                <button :disabled="checkingUpdate[s.Description.EdgeName]" @click="checkForUpdate(s)">{{ checkingUpdate[s.Description.EdgeName] ? 'Checking...' : 'Check for update' }}</button>
                                <button class="btn-danger" :disabled="removing[s.Description.EdgeName]" @click="remove(s)">Remove</button>
                            </td>
                        </tr>
                        <tr v-if="expanded === s.Description.EdgeName">
                            <td colspan="9" class="detail-panel">
                                <div class="rule-row">
                                    <div class="rule-field">
                                        <label>Credential</label>
                                        <select v-model="editCredential">
                                            <option v-for="c in (store.credentials || [])" :key="c.Name" :value="c.Name">{{ c.Name }}</option>
                                        </select>
                                    </div>
                                    <button :disabled="saving" @click="saveCredential(s)">{{ saving ? 'Saving...' : 'Save' }}</button>
                                </div>
                            </td>
                        </tr>
                    </template>
                    <tr v-if="edgeList.length === 0"><td colspan="9" class="empty">No edge connected yet.</td></tr>
                </tbody>
            </table>
            <modal :show="!!revealDialog" :title="revealDialog ? revealDialog.name + ' - ' + revealDialog.label : ''" @close="closeReveal">
                <template v-if="revealDialog">
                    <p v-if="revealDialog.pushed">Sent to the edge automatically - it should reconnect shortly. This is shown too as a fallback in case it wasn't still connected to receive it; copy it now, it won't be shown again.</p>
                    <p v-else>Copy this now - it won't be shown again. No open connection to push it to automatically; paste it into this edge's <code>appsettings.json</code> (<code>Edge.OrbitMeshAccessKey</code>) and restart it.</p>
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
