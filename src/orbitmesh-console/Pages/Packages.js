import { store } from "../Scripts/store.js";
import * as api from "../Scripts/management-api.js";
import CodeEditor from "../Scripts/CodeEditor.js";
import Modal from "../Scripts/Modal.js";

// "0" is valid JSON whether a token sits bare or already inside quotes.
const withPlaceholderVariables = (text) => text.replace(/\{(\w+)\}/g, "0");

// Error message, or null if every JsonObject/XmlDocument setting is well-formed.
function validateSettingsJson(settings, meta) {
    for (const [name, value] of Object.entries(settings)) {
        const type = meta[name]?.Type;
        if (!value) {
            continue;
        }
        if (type === "JsonObject") {
            try {
                JSON.parse(withPlaceholderVariables(value));
            } catch (err) {
                return `Invalid JSON in "${name}": ${err.message}`;
            }
        } else if (type === "XmlDocument") {
            const doc = new DOMParser().parseFromString(withPlaceholderVariables(value), "application/xml");
            if (doc.getElementsByTagName("parsererror").length > 0) {
                return `Invalid XML in "${name}".`;
            }
        }
    }
    return null;
}

export default {
    components: { CodeEditor, Modal },
    data() {
        return {
            store, busy: {}, expanded: null, editSettings: {}, editSettingsMeta: {}, editGroups: "", editRecovery: null, loadingDetail: false,
            variablesList: [], removeDialog: null,
            wizard: this.freshWizard(), deploying: false
        };
    },
    mounted() {
        // A hard reload straight to this route can mount before managementAvailable turns true.
        store.onManagementAvailable(() => {
            store.loadPackageRepository();
            api.getVariables().then((variables) => { this.variablesList = variables; });
        });
    },
    computed: {
        byEdge() {
            const groups = {};
            for (const pkg of Object.values(store.packages)) {
                (groups[pkg.EdgeName] ||= []).push(pkg);
            }
            for (const list of Object.values(groups)) {
                list.sort((a, b) => a.Package.Name.localeCompare(b.Package.Name));
            }
            return groups;
        },
        // Both default to "DotNet" when unset (an older Edge/package manifest predating the Runtime
        // field, or a package with no manifest at all) - matches OrbitMesh.Deployment.PackageManifest's
        // and EdgeDescription's own C# defaults, so this only ever flags a *declared* mismatch.
        wizardEdgeRuntime() {
            return store.edges[this.wizard.edgeName]?.Description?.Runtime || "DotNet";
        },
        wizardPackageRuntime() {
            const repoEntry = (store.packagesRepository || []).find((p) => p.Filename === this.wizard.packageFile);
            return repoEntry?.PackageInfo?.Runtime || "DotNet";
        },
        wizardRuntimeMismatch() {
            if (!this.wizard.edgeName || !this.wizard.packageFile) {
                return false;
            }
            return this.wizardEdgeRuntime !== this.wizardPackageRuntime;
        }
    },
    methods: {
        stateClass(pkg) {
            if (!pkg.IsConnected) return "disconnected";
            if (pkg.State === "Started") return "connected";
            return "connecting";
        },
        key(pkg) {
            return pkg.EdgeName + "/" + pkg.Package.Name;
        },
        async run(pkg, action) {
            const key = this.key(pkg);
            this.busy[key] = true;
            try {
                await store.controller.server[action](pkg.EdgeName, pkg.Package.Name);
            } finally {
                setTimeout(() => { delete this.busy[key]; }, 1000);
            }
        },
        // store.packages reflects live state, not config - fetch the raw PackageOptions for its Credential.
        async remove(pkg) {
            const instance = await api.getPackageInstance(pkg.EdgeName, pkg.Package.Name);
            this.removeDialog = { pkg, credential: instance.Credential || null, deleteCredential: true };
        },
        cancelRemove() {
            this.removeDialog = null;
        },
        async confirmRemove() {
            const { pkg, credential, deleteCredential } = this.removeDialog;
            this.removeDialog = null;
            const key = this.key(pkg);
            this.busy[key] = true;
            try {
                await api.removePackage(pkg.EdgeName, pkg.Package.Name);
                if (deleteCredential && credential) {
                    await api.removeCredential(credential);
                }
                // Same IOptionsMonitor file-watcher race as wizardDeploy below - the write needs a
                // moment to land before the Edge is told to reload against it.
                await new Promise((resolve) => setTimeout(resolve, 500));
                await store.controller.server.reloadServerConfiguration(true);
                // The Edge only reports its final "Stopped" state for the removed instance (see
                // EdgeManager.OnPackagesListReceived) - it never re-announces its whole package list on
                // its own, so without this the row would linger at "Stopped" in the console until
                // something else happens to trigger a resync. This forces that resync now.
                await store.controller.server.requestPackagesList(pkg.EdgeName);
                if (credential) {
                    await store.loadCredentials();
                }
            } finally {
                delete this.busy[key];
            }
        },
        // The deployed instance only stores raw key/value settings - the *type* (so the console can
        // render a checkbox vs. a textarea vs. a password field instead of one plain text box for
        // everything) lives in the package's own manifest, bundled inside its repository zip. Instances
        // deployed before console v2 (or configured by hand) have no PackageFile recorded, so fall back
        // to the "{Name}.zip" convention every package in this repo already follows.
        settingsMetaForFilename(filename) {
            const key = (filename || "").toLowerCase();
            const repoEntry = (store.packagesRepository || []).find((p) => p.Filename.toLowerCase() === key);
            const meta = {};
            for (const s of repoEntry?.PackageInfo?.Settings || []) {
                meta[s.Name] = s;
            }
            return meta;
        },
        settingsMetaFor(pkg) {
            return this.settingsMetaForFilename(pkg.Package.PackageFile || pkg.Package.Name + ".zip");
        },
        // metaMap is explicit (not always this.editSettingsMeta) so the same field-type logic can
        // render both the "Manage" panel's settings and the deploy wizard's, which uses its own
        // wizard.settingsMeta instead.
        settingControl(name, metaMap = this.editSettingsMeta) {
            const type = metaMap[name]?.Type;
            if (type === "Boolean") return "checkbox";
            if (type === "Int32" || type === "Int64" || type === "Double") return "number";
            if (type === "DateTime") return "date";
            if (type === "Password") return "password";
            if (type === "JsonObject") return "json";
            if (type === "XmlDocument" || type === "ConfigurationSection") return "xml";
            // String, TimeSpan, or no manifest match at all - defaults to a textarea (settings values
            // are often long connection strings/JSON, not short words).
            return "textarea";
        },
        async toggleManage(pkg) {
            const key = this.key(pkg);
            if (this.expanded === key) {
                this.expanded = null;
                return;
            }
            this.expanded = key;
            this.loadingDetail = true;
            try {
                const [settings, groups, recovery] = await Promise.all([
                    api.getPackageSettings(pkg.EdgeName, pkg.Package.Name),
                    api.getPackageGroups(pkg.EdgeName, pkg.Package.Name),
                    api.getPackageRecoveryOptions(pkg.EdgeName, pkg.Package.Name)
                ]);
                this.editSettingsMeta = this.settingsMetaFor(pkg);
                // getPackageSettings only returns keys that already have a saved value - a freshly
                // deployed package (or an older instance predating a setting the manifest later added)
                // has none yet, so without this a required setting like TPLinkSmartHome's Password
                // never appears in the editor at all and there's no way to fill it in. Backfill every
                // manifest-declared setting that isn't in the real config yet with its default value,
                // so the editor always shows the full declared shape, not just whatever happens to be
                // saved already.
                this.editSettings = { ...settings };
                for (const meta of Object.values(this.editSettingsMeta)) {
                    if (!(meta.Name in this.editSettings)) {
                        this.editSettings[meta.Name] = meta.DefaultValue ?? "";
                    }
                }
                this.editGroups = groups.join(", ");
                this.editRecovery = { ...recovery };
            } finally {
                this.loadingDetail = false;
            }
        },
        async copyVariableToken(name) {
            try {
                await navigator.clipboard.writeText(`{${name}}`);
                store.notify(`Copied {${name}} - paste it into a setting value.`, "success");
            } catch {
                store.notify(`Could not copy automatically - type {${name}} into a setting value.`, "error");
            }
        },
        async saveSettings(pkg) {
            const error = validateSettingsJson(this.editSettings, this.editSettingsMeta);
            if (error) {
                store.notify(error, "error");
                return;
            }
            try {
                await api.setPackageSettings(pkg.EdgeName, pkg.Package.Name, this.editSettings, true);
                store.notify("Settings saved and pushed to the running package.", "success");
            } catch (err) {
                store.notify(err.body || err.message, "error");
            }
        },
        async saveGroups(pkg) {
            const groups = this.editGroups.split(",").map((g) => g.trim()).filter((g) => g.length > 0);
            await api.setPackageGroups(pkg.EdgeName, pkg.Package.Name, groups);
            store.notify("Groups saved.", "success");
        },
        async saveRecovery(pkg) {
            await api.setPackageRecoveryOptions(pkg.EdgeName, pkg.Package.Name, this.editRecovery);
            store.notify("Recovery options saved.", "success");
        },
        freshWizard() {
            return { open: false, step: 1, edgeName: "", packageFile: "", name: "", nameTouched: false, enable: true, autoStart: true, settings: {}, settingsMeta: {} };
        },
        openWizard() {
            this.wizard = this.freshWizard();
            this.wizard.open = true;
        },
        closeWizard() {
            this.wizard.open = false;
        },
        // Prefills the instance name from the package's own manifest Name (falling back to the
        // filename minus ".zip" if it has no manifest) - only while the user hasn't typed their own
        // name yet, so picking a package doesn't clobber something they already customized.
        onWizardPackageChange() {
            if (this.wizard.nameTouched) {
                return;
            }
            const repoEntry = (store.packagesRepository || []).find((p) => p.Filename === this.wizard.packageFile);
            this.wizard.name = repoEntry?.PackageInfo?.Name || this.wizard.packageFile.replace(/\.zip$/i, "");
        },
        // Moves to the settings step, pre-filled from the package's own manifest (bundled in its
        // repository zip) - same backfill-with-defaults logic toggleManage uses for an already-deployed
        // instance, just against the manifest alone since nothing is deployed yet at this point.
        wizardNext() {
            if (!this.wizard.edgeName || !this.wizard.packageFile || !this.wizard.name) {
                store.notify("Edge, package file and instance name are all required.", "error");
                return;
            }
            if (this.wizardRuntimeMismatch) {
                store.notify(
                    `${this.wizard.packageFile} needs a ${this.wizardPackageRuntime} Edge, but '${this.wizard.edgeName}' is a ${this.wizardEdgeRuntime} Edge - it would refuse to start this package.`,
                    "error"
                );
                return;
            }
            this.wizard.settingsMeta = this.settingsMetaForFilename(this.wizard.packageFile);
            this.wizard.settings = {};
            for (const meta of Object.values(this.wizard.settingsMeta)) {
                this.wizard.settings[meta.Name] = meta.DefaultValue ?? "";
            }
            this.wizard.step = 2;
        },
        wizardBack() {
            this.wizard.step = 1;
        },
        async wizardDeploy() {
            const error = validateSettingsJson(this.wizard.settings, this.wizard.settingsMeta);
            if (error) {
                store.notify(error, "error");
                return;
            }
            this.deploying = true;
            try {
                // Deploys with the settings collected in step 2 already attached, instead of leaving the
                // package unconfigured until someone remembers to open "Manage" afterwards.
                await api.upsertPackage(this.wizard.edgeName, this.wizard.name, null, this.wizard.packageFile, this.wizard.enable, this.wizard.autoStart);
                if (Object.keys(this.wizard.settings).length > 0) {
                    // refreshPackage:false - nothing is running yet to push a live update to; the
                    // reload below is what actually starts it, already carrying these values.
                    await api.setPackageSettings(this.wizard.edgeName, this.wizard.name, this.wizard.settings, false);
                }
                // Same IOptionsMonitor file-watcher race noted throughout this file - give the write(s)
                // a moment to land before reloading, so the Edge gets the fully-configured package in
                // one shot instead of an unconfigured one followed by a second push.
                await new Promise((resolve) => setTimeout(resolve, 500));
                await store.controller.server.reloadServerConfiguration(true);
                store.notify(`${this.wizard.name} deployed to ${this.wizard.edgeName}, configured and reloading.`, "success");
                this.closeWizard();
            } catch (err) {
                store.notify(err.body || err.message, "error");
            } finally {
                this.deploying = false;
            }
        }
    },
    template: `
        <div>
            <h2>Packages</h2>
            <p><button @click="openWizard">Deploy a package</button></p>

            <modal :show="wizard.open" :title="wizard.step === 1 ? 'Deploy a package' : 'Configure ' + wizard.name" @close="closeWizard">
                <template #default>
                    <div v-if="wizard.step === 1">
                        <p>
                            <label>Edge<br />
                                <select v-model="wizard.edgeName" style="width:100%">
                                    <option value="" disabled>Edge...</option>
                                    <option v-for="s in Object.keys(store.edges)" :key="s" :value="s">{{ s }}</option>
                                </select>
                            </label>
                        </p>
                        <p>
                            <label>Package<br />
                                <select v-model="wizard.packageFile" @change="onWizardPackageChange" style="width:100%">
                                    <option value="" disabled>Package file...</option>
                                    <option v-for="p in (store.packagesRepository || [])" :key="p.Filename" :value="p.Filename">{{ p.Filename }}</option>
                                </select>
                            </label>
                        </p>
                        <p>
                            <label>Instance name<br />
                                <input v-model="wizard.name" @input="wizard.nameTouched = true" placeholder="Instance name" style="width:100%" />
                            </label>
                        </p>
                        <p v-if="wizardRuntimeMismatch" class="error">
                            {{ wizard.packageFile }} needs a {{ wizardPackageRuntime }} Edge, but '{{ wizard.edgeName }}' is a {{ wizardEdgeRuntime }} Edge - it would refuse to start this package.
                        </p>
                        <p>
                            <label><input type="checkbox" v-model="wizard.enable" /> Enable</label>
                            <label style="margin-left:12px"><input type="checkbox" v-model="wizard.autoStart" /> Auto-start</label>
                        </p>
                    </div>
                    <div v-else>
                        <p v-if="variablesList.length > 0" class="muted" style="margin-bottom:10px">
                            Available variables (click to copy the token, paste into a value below):
                            <button v-for="v in variablesList" :key="v.Name" @click="copyVariableToken(v.Name)">{{'{' + v.Name + '}'}}</button>
                        </p>
                        <div v-for="(value, name) in wizard.settings" :key="name" class="setting-row" :title="wizard.settingsMeta[name]?.Description || ''">
                            <label>{{ name }}</label>
                            <input v-if="settingControl(name, wizard.settingsMeta) === 'checkbox'" type="checkbox"
                                   :checked="wizard.settings[name] === 'true'"
                                   @change="wizard.settings[name] = $event.target.checked ? 'true' : 'false'" />
                            <input v-else-if="settingControl(name, wizard.settingsMeta) === 'password'" type="password" v-model="wizard.settings[name]" />
                            <!-- text, not number: a "{Name}" variable token isn't a valid number, and
                                 type="number" rejects that character outright, typed or pasted. -->
                            <input v-else-if="settingControl(name, wizard.settingsMeta) === 'number'" type="text" inputmode="decimal" v-model="wizard.settings[name]" />
                            <input v-else-if="settingControl(name, wizard.settingsMeta) === 'date'" type="date" v-model="wizard.settings[name]" />
                            <code-editor v-else-if="settingControl(name, wizard.settingsMeta) === 'json'" v-model="wizard.settings[name]" mode="application/json" />
                            <code-editor v-else-if="settingControl(name, wizard.settingsMeta) === 'xml'" v-model="wizard.settings[name]" mode="application/xml" />
                            <textarea v-else v-model="wizard.settings[name]" rows="2"></textarea>
                        </div>
                        <p v-if="Object.keys(wizard.settings).length === 0" class="empty">This package declares no settings.</p>
                    </div>
                </template>
                <template #footer>
                    <button v-if="wizard.step === 2" @click="wizardBack" :disabled="deploying">Back</button>
                    <button v-if="wizard.step === 1" :disabled="wizardRuntimeMismatch" @click="wizardNext">Next</button>
                    <button v-else :disabled="deploying" @click="wizardDeploy">{{ deploying ? 'Deploying...' : 'Deploy' }}</button>
                </template>
            </modal>

            <div v-for="(packages, edgeName) in byEdge" :key="edgeName" class="package-group">
                <h3>{{ edgeName }}</h3>
                <table class="data-table">
                    <thead>
                        <tr><th>Name</th><th style="width:110px">Status</th><th style="width:90px">Version</th><th style="width:80px">CPU</th><th style="width:100px">RAM</th><th style="width:390px">Actions</th></tr>
                    </thead>
                    <tbody>
                        <template v-for="pkg in packages" :key="key(pkg)">
                            <tr>
                                <td>{{ pkg.Package.Name }}</td>
                                <td><span class="badge" :class="stateClass(pkg)">{{ pkg.State }}</span></td>
                                <td>{{ pkg.PackageVersion }}</td>
                                <td>{{ pkg.CPU != null ? pkg.CPU.toFixed(1) + '%' : '-' }}</td>
                                <td>{{ pkg.RAM != null ? (pkg.RAM / 1024 / 1024).toFixed(1) + ' MB' : '-' }}</td>
                                <td class="actions">
                                    <button :disabled="busy[key(pkg)]" @click="run(pkg, 'start')">Start</button>
                                    <button :disabled="busy[key(pkg)]" @click="run(pkg, 'stop')">Stop</button>
                                    <button :disabled="busy[key(pkg)]" @click="run(pkg, 'reload')">Reload</button>
                                    <button @click="toggleManage(pkg)">{{ expanded === key(pkg) ? 'Close' : 'Manage' }}</button>
                                    <button class="btn-danger" :disabled="busy[key(pkg)]" @click="remove(pkg)">Remove</button>
                                </td>
                            </tr>
                            <tr v-if="expanded === key(pkg)">
                                <td colspan="6" class="detail-panel">
                                    <p v-if="loadingDetail">Loading...</p>
                                    <div v-else class="detail-columns">
                                        <div>
                                            <h4>Settings</h4>
                                            <p v-if="variablesList.length > 0" class="muted" style="margin-bottom:10px">
                                                Available variables (click to copy the token, paste into a value below):
                                                <button v-for="v in variablesList" :key="v.Name" @click="copyVariableToken(v.Name)">{{'{' + v.Name + '}'}}</button>
                                            </p>
                                            <div v-for="(value, name) in editSettings" :key="name" class="setting-row" :title="editSettingsMeta[name]?.Description || ''">
                                                <label>{{ name }}</label>
                                                <input v-if="settingControl(name) === 'checkbox'" type="checkbox"
                                                       :checked="editSettings[name] === 'true'"
                                                       @change="editSettings[name] = $event.target.checked ? 'true' : 'false'" />
                                                <input v-else-if="settingControl(name) === 'password'" type="password" v-model="editSettings[name]" />
                                                <input v-else-if="settingControl(name) === 'number'" type="text" inputmode="decimal" v-model="editSettings[name]" />
                                                <input v-else-if="settingControl(name) === 'date'" type="date" v-model="editSettings[name]" />
                                                <code-editor v-else-if="settingControl(name) === 'json'" v-model="editSettings[name]" mode="application/json" />
                                                <code-editor v-else-if="settingControl(name) === 'xml'" v-model="editSettings[name]" mode="application/xml" />
                                                <textarea v-else v-model="editSettings[name]" rows="2"></textarea>
                                            </div>
                                            <p v-if="Object.keys(editSettings).length === 0" class="empty">No settings.</p>
                                            <button @click="saveSettings(pkg)">Save settings</button>
                                        </div>
                                        <div>
                                            <h4>Groups</h4>
                                            <input v-model="editGroups" placeholder="comma, separated, groups" style="width:100%" />
                                            <button @click="saveGroups(pkg)">Save groups</button>
                                        </div>
                                        <div v-if="editRecovery">
                                            <h4>Recovery options</h4>
                                            <label><input type="checkbox" v-model="editRecovery.RestartAfterFailure" /> Restart after failure</label>
                                            <label>Number of retry <input type="number" v-model.number="editRecovery.NumberOfRetry" /></label>
                                            <label>Reset counter after (min) <input type="number" v-model.number="editRecovery.ResetCounterAfterMinutes" /></label>
                                            <label>Restart after (sec) <input type="number" v-model.number="editRecovery.RestartPackageAfterSeconds" /></label>
                                            <button @click="saveRecovery(pkg)">Save recovery</button>
                                        </div>
                                    </div>
                                </td>
                            </tr>
                        </template>
                    </tbody>
                </table>
            </div>
            <p v-if="Object.keys(byEdge).length === 0" class="empty">No packages reported yet.</p>

            <modal :show="!!removeDialog" title="Remove package" @close="cancelRemove">
                <template #default>
                    <p>Remove '{{ removeDialog?.pkg?.Package?.Name }}' from '{{ removeDialog?.pkg?.EdgeName }}'? This stops it and deletes its settings/groups/recovery options.</p>
                    <label v-if="removeDialog?.credential">
                        <input type="checkbox" v-model="removeDialog.deleteCredential" /> Also delete its credential ('{{ removeDialog.credential }}')
                    </label>
                    <p v-else class="muted">This package has no dedicated credential to remove.</p>
                </template>
                <template #footer>
                    <button @click="cancelRemove">Cancel</button>
                    <button class="btn-danger" @click="confirmRemove">Remove</button>
                </template>
            </modal>
        </div>
    `
};
