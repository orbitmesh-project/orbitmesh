import { store } from "../Scripts/store.js";
import * as api from "../Scripts/management-api.js";

const emptyTask = () => ({
    Name: "", Enable: true, CronExpression: "", Credential: "",
    CatchUpIfMissed: false, EdgeName: "", PackageName: "", MessageKey: ""
});

export default {
    data() {
        return {
            store, tasks: [], loading: false, creating: false, saving: false, removing: {},
            newTask: emptyTask(), newTaskParams: {},
            expanded: null, editTask: null, editParams: {},
            pollHandle: null
        };
    },
    mounted() {
        // A hard reload straight to this route can mount before managementAvailable turns true.
        store.onManagementAvailable(() => {
            store.loadEdges();
            store.loadCredentials();
            this.load();
            // LastRunUtc is updated server-side by ScheduledTaskRunner's own tick, not by anything
            // this page does - poll to reflect it without a manual refresh.
            this.pollHandle = setInterval(() => this.load(), 30000);
        });
    },
    beforeUnmount() {
        if (this.pollHandle) {
            clearInterval(this.pollHandle);
        }
    },
    methods: {
        async load() {
            this.loading = true;
            try {
                this.tasks = await api.getScheduledTasks();
            } catch {
                // Next poll tick just tries again.
            } finally {
                this.loading = false;
            }
        },
        edgeNames() {
            return Object.keys(store.edges).sort();
        },
        packagesForEdge(edgeName) {
            const names = new Set();
            for (const pkg of Object.values(store.packages)) {
                if (pkg.EdgeName === edgeName) {
                    names.add(pkg.Package.Name);
                }
            }
            return [...names].sort();
        },
        handlersForPackage(packageName) {
            return Object.values(store.messageHandlers)
                .filter((e) => e.PackageName === packageName)
                .map((e) => e.MessageHandler)
                .sort((a, b) => a.MessageKey.localeCompare(b.MessageKey));
        },
        handlerFor(packageName, messageKey) {
            return this.handlersForPackage(packageName).find((h) => h.MessageKey === messageKey) || null;
        },
        castValue(raw, typeName) {
            if (raw === undefined || raw === "") {
                return null;
            }
            if (typeName === "System.Int32" || typeName === "System.Int64" || typeName === "System.Double") {
                return Number(raw);
            }
            if (typeName === "System.Boolean") {
                return raw === "true" || raw === true;
            }
            return raw;
        },
        buildDataJson(task, paramValues) {
            const handler = this.handlerFor(task.PackageName, task.MessageKey);
            const params = handler?.Parameters || [];
            if (params.length === 0) {
                return null;
            }
            const values = params.map((p) => this.castValue(paramValues[p.Name], p.TypeName));
            return JSON.stringify(values.length === 1 ? values[0] : values);
        },
        paramValuesFromTask(task) {
            const handler = this.handlerFor(task.PackageName, task.MessageKey);
            const params = handler?.Parameters || [];
            const values = {};
            if (!task.Data || params.length === 0) {
                return values;
            }
            try {
                const parsed = JSON.parse(task.Data);
                const arr = params.length === 1 ? [parsed] : parsed;
                params.forEach((p, i) => { values[p.Name] = arr[i]; });
            } catch {
                // Leave blank rather than fail to open the edit panel over a hand-edited/stale value.
            }
            return values;
        },
        async createTask() {
            if (!this.newTask.Name || !this.newTask.CronExpression || !this.newTask.Credential
                || !this.newTask.EdgeName || !this.newTask.PackageName || !this.newTask.MessageKey) {
                store.notify("Name, cron expression, credential, edge, package and message key are all required.", "error");
                return;
            }
            this.creating = true;
            try {
                const task = { ...this.newTask, Data: this.buildDataJson(this.newTask, this.newTaskParams) };
                await api.upsertScheduledTask(task);
                await this.load();
                this.newTask = emptyTask();
                this.newTaskParams = {};
            } finally {
                this.creating = false;
            }
        },
        toggleManage(task) {
            if (this.expanded === task.Name) {
                this.expanded = null;
                return;
            }
            this.expanded = task.Name;
            this.editTask = { ...task };
            this.editParams = this.paramValuesFromTask(task);
        },
        async saveTask() {
            this.saving = true;
            try {
                const task = { ...this.editTask, Data: this.buildDataJson(this.editTask, this.editParams) };
                await api.upsertScheduledTask(task);
                await this.load();
                this.expanded = null;
            } finally {
                this.saving = false;
            }
        },
        async toggleEnable(task) {
            await api.upsertScheduledTask({ ...task, Enable: !task.Enable });
            await this.load();
        },
        async remove(task) {
            if (!(await store.confirm(`Remove scheduled task '${task.Name}'?`))) {
                return;
            }
            this.removing[task.Name] = true;
            try {
                await api.removeScheduledTask(task.Name);
                await this.load();
            } finally {
                this.removing[task.Name] = false;
            }
        },
        lastRunLabel(task) {
            return task.LastRunUtc ? new Date(task.LastRunUtc).toLocaleString() : "Never";
        }
    },
    template: `
        <div>
            <h2>Scheduled Tasks</h2>
            <p class="empty">Fires a message on a cron schedule, as the chosen credential - same Authorizations/messages:execute check as if that credential had sent it itself.</p>
            <div class="deploy-form">
                <h3>Add scheduled task</h3>
                <input v-model="newTask.Name" placeholder="Name" style="width:180px" />
                <input v-model="newTask.CronExpression" placeholder="Cron: 0 20 * * *" style="width:140px" title="Standard 5-field cron: minute hour day month day-of-week" />
                <select v-model="newTask.Credential">
                    <option value="" disabled>Credential...</option>
                    <option v-for="c in (store.credentials || [])" :key="c.Name" :value="c.Name">{{ c.Name }}</option>
                </select>
                <label><input type="checkbox" v-model="newTask.CatchUpIfMissed" /> Catch up if missed</label>
                <label><input type="checkbox" v-model="newTask.Enable" /> Enable</label>
                <div class="rule-row">
                    <div class="rule-field">
                        <label>Edge</label>
                        <select v-model="newTask.EdgeName" @change="newTask.PackageName = ''; newTask.MessageKey = ''; newTaskParams = {}">
                            <option value="" disabled>Edge...</option>
                            <option v-for="e in edgeNames()" :key="e" :value="e">{{ e }}</option>
                        </select>
                    </div>
                    <div class="rule-field">
                        <label>Package</label>
                        <select v-model="newTask.PackageName" :disabled="!newTask.EdgeName" @change="newTask.MessageKey = ''; newTaskParams = {}">
                            <option value="" disabled>Package...</option>
                            <option v-for="p in packagesForEdge(newTask.EdgeName)" :key="p" :value="p">{{ p }}</option>
                        </select>
                    </div>
                    <div class="rule-field">
                        <label>Message handler</label>
                        <select v-model="newTask.MessageKey" :disabled="!newTask.PackageName" @change="newTaskParams = {}">
                            <option value="" disabled>Handler...</option>
                            <option v-for="h in handlersForPackage(newTask.PackageName)" :key="h.MessageKey" :value="h.MessageKey" :title="h.Description">{{ h.MessageKey.split('/').pop() }}</option>
                        </select>
                    </div>
                </div>
                <div class="rule-row" v-if="handlerFor(newTask.PackageName, newTask.MessageKey)?.Parameters.length">
                    <div class="rule-field" v-for="p in handlerFor(newTask.PackageName, newTask.MessageKey).Parameters" :key="p.Name">
                        <label>{{ p.Name }} ({{ p.TypeName.split('.').pop() }})</label>
                        <input v-model="newTaskParams[p.Name]" />
                    </div>
                </div>
                <button :disabled="creating" @click="createTask">{{ creating ? 'Adding...' : 'Add' }}</button>
            </div>
            <table class="data-table">
                <thead>
                    <tr>
                        <th style="width:140px">Name</th><th style="width:110px">Cron</th><th>Target</th>
                        <th style="width:130px">Credential</th><th style="width:90px">Enable</th>
                        <th style="width:150px">Last run</th><th style="width:220px">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    <template v-for="task in tasks" :key="task.Name">
                        <tr>
                            <td>{{ task.Name }}</td>
                            <td><code>{{ task.CronExpression }}</code></td>
                            <td>{{ task.EdgeName }}/{{ task.PackageName }} - {{ task.MessageKey.split('/').pop() }}</td>
                            <td>{{ task.Credential }}</td>
                            <td><span class="badge" :class="task.Enable ? 'connected' : 'disconnected'">{{ task.Enable ? 'Enabled' : 'Disabled' }}</span></td>
                            <td>{{ lastRunLabel(task) }}</td>
                            <td class="actions">
                                <button @click="toggleEnable(task)">{{ task.Enable ? 'Disable' : 'Enable' }}</button>
                                <button @click="toggleManage(task)">{{ expanded === task.Name ? 'Close' : 'Manage' }}</button>
                                <button class="btn-danger" :disabled="removing[task.Name]" @click="remove(task)">Remove</button>
                            </td>
                        </tr>
                        <tr v-if="expanded === task.Name">
                            <td colspan="7" class="detail-panel">
                                <div class="detail-columns" style="grid-template-columns: 1fr;">
                                    <div>
                                        <h4>Edit '{{ editTask.Name }}'</h4>
                                        <div class="setting-row">
                                            <label>Cron</label>
                                            <input v-model="editTask.CronExpression" style="width:140px" />
                                        </div>
                                        <div class="setting-row">
                                            <label>Credential</label>
                                            <select v-model="editTask.Credential">
                                                <option v-for="c in (store.credentials || [])" :key="c.Name" :value="c.Name">{{ c.Name }}</option>
                                            </select>
                                        </div>
                                        <label><input type="checkbox" v-model="editTask.CatchUpIfMissed" /> Catch up if missed</label>
                                        <div class="rule-row" v-if="handlerFor(editTask.PackageName, editTask.MessageKey)?.Parameters.length">
                                            <div class="rule-field" v-for="p in handlerFor(editTask.PackageName, editTask.MessageKey).Parameters" :key="p.Name">
                                                <label>{{ p.Name }} ({{ p.TypeName.split('.').pop() }})</label>
                                                <input v-model="editParams[p.Name]" />
                                            </div>
                                        </div>
                                        <p class="empty">Target (Edge/Package/Handler) can't be changed here - remove and re-add to retarget.</p>
                                        <div style="margin-top:8px;"><button :disabled="saving" @click="saveTask">{{ saving ? 'Saving...' : 'Save' }}</button></div>
                                    </div>
                                </div>
                            </td>
                        </tr>
                    </template>
                    <tr v-if="tasks.length === 0"><td colspan="7" class="empty">No scheduled tasks yet.</td></tr>
                </tbody>
            </table>
        </div>
    `
};
