import { store } from "../Scripts/store.js";
import * as api from "../Scripts/management-api.js";

const emptyVariable = () => ({ name: "", value: "", isSecret: false });

export default {
    data() {
        return {
            store, variables: [], loading: true, editing: null, editValue: "", editIsSecret: false,
            revealed: {}, revealing: {}, saving: false, removing: {}, newVariable: emptyVariable(), creating: false
        };
    },
    mounted() {
        store.onManagementAvailable(() => this.load());
    },
    methods: {
        async load() {
            this.loading = true;
            try {
                this.variables = await api.getVariables();
            } finally {
                this.loading = false;
            }
        },
        startEdit(v) {
            this.editing = v.Name;
            this.editValue = v.IsSecret ? "" : v.Value;
            this.editIsSecret = v.IsSecret;
        },
        async save(v) {
            this.saving = true;
            try {
                await api.upsertVariable(v.Name, this.editValue, this.editIsSecret);
                await this.load();
                this.editing = null;
                store.notify(`'${v.Name}' saved.`, "success");
            } finally {
                this.saving = false;
            }
        },
        async reveal(v) {
            this.revealing[v.Name] = true;
            try {
                this.revealed[v.Name] = await api.revealVariable(v.Name);
            } catch {
                store.notify("Could not reveal this value.", "error");
            } finally {
                this.revealing[v.Name] = false;
            }
        },
        async remove(v) {
            if (!(await store.confirm(`Remove variable '${v.Name}'? Any package setting still referencing {${v.Name}} will keep that literal text instead of a real value.`))) {
                return;
            }
            this.removing[v.Name] = true;
            try {
                await api.removeVariable(v.Name);
                await this.load();
            } finally {
                this.removing[v.Name] = false;
            }
        },
        async createVariable() {
            const name = this.newVariable.name.trim();
            if (!name) {
                store.notify("Name is required.", "error");
                return;
            }
            this.creating = true;
            try {
                await api.upsertVariable(name, this.newVariable.value, this.newVariable.isSecret);
                await this.load();
                this.newVariable = emptyVariable();
                store.notify(`'${name}' created.`, "success");
            } finally {
                this.creating = false;
            }
        }
    },
    template: `
        <div>
            <h2>Variables</h2>
            <p class="muted">
                Named values any package's settings can reference by writing <code>{Name}</code> anywhere in a
                setting's value - including inside a JSON setting. Substituted only when settings are delivered to
                a connected package, so editing/saving a package's settings never bakes the current value in:
                change a Variable here and every package referencing it picks up the change on its own. A "secret"
                Variable is encrypted at rest and hidden behind "Reveal" instead of shown in the list.
            </p>

            <div v-if="loading" class="empty">Loading...</div>
            <template v-else>
                <table class="data-table" v-if="variables.length > 0">
                    <thead>
                        <tr><th style="width:200px">Name</th><th>Value</th><th style="width:80px">Secret</th><th style="width:260px">Actions</th></tr>
                    </thead>
                    <tbody>
                        <template v-for="v in variables" :key="v.Name">
                            <tr v-if="editing !== v.Name">
                                <td><code>{{'{' + v.Name + '}'}}</code></td>
                                <td>
                                    <span v-if="!v.IsSecret">{{ v.Value }}</span>
                                    <span v-else-if="revealed[v.Name] !== undefined">{{ revealed[v.Name] }}</span>
                                    <span v-else class="muted">***</span>
                                </td>
                                <td>{{ v.IsSecret ? 'Yes' : '' }}</td>
                                <td class="actions">
                                    <button v-if="v.IsSecret && revealed[v.Name] === undefined" :disabled="revealing[v.Name]" @click="reveal(v)">{{ revealing[v.Name] ? '...' : 'Reveal' }}</button>
                                    <button @click="startEdit(v)">Edit</button>
                                    <button class="btn-danger" :disabled="removing[v.Name]" @click="remove(v)">Remove</button>
                                </td>
                            </tr>
                            <tr v-else>
                                <td colspan="4" class="detail-panel">
                                    <div class="setting-row">
                                        <label>Value</label>
                                        <input :type="editIsSecret ? 'password' : 'text'" v-model="editValue"
                                               :placeholder="editIsSecret ? 'Leave blank to keep the current value' : ''" style="flex:1" />
                                    </div>
                                    <label><input type="checkbox" v-model="editIsSecret" /> Secret (encrypted, hidden by default)</label>
                                    <div style="margin-top:10px">
                                        <button :disabled="saving" @click="save(v)">{{ saving ? 'Saving...' : 'Save' }}</button>
                                        <button @click="editing = null">Cancel</button>
                                    </div>
                                </td>
                            </tr>
                        </template>
                    </tbody>
                </table>
                <p v-else class="empty">No variables yet.</p>

                <h3 style="margin-top:24px">Create a new variable</h3>
                <div class="setting-row">
                    <input v-model="newVariable.name" placeholder="Name (e.g. Latitude)" style="width:200px" />
                    <input :type="newVariable.isSecret ? 'password' : 'text'" v-model="newVariable.value" placeholder="Value" style="flex:1" />
                </div>
                <label><input type="checkbox" v-model="newVariable.isSecret" /> Secret (encrypted, hidden by default)</label>
                <div style="margin-top:10px">
                    <button :disabled="creating" @click="createVariable">{{ creating ? 'Creating...' : 'Create variable' }}</button>
                </div>
            </template>
        </div>
    `
};
