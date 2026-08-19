import { store } from "../Scripts/store.js";
import CodeEditor from "../Scripts/CodeEditor.js";
import Modal from "../Scripts/Modal.js";
import TypeDescriptorModal from "../Scripts/TypeDescriptorModal.js";
import { hasTypeDescriptor } from "../Scripts/typeDescriptors.js";

export default {
    components: { CodeEditor, Modal, TypeDescriptorModal },
    data() {
        return { store, filter: "", packageFilter: "", viewingKey: null, viewingType: null, showPurge: false, purgeTarget: "", purgeOnlyExpired: true, purging: false };
    },
    computed: {
        packages() {
            return [...new Set(Object.values(store.telemetryItems).map((so) => so.PackageName))].sort();
        },
        // "Edge/Package" pairs currently known, offered as a purge scope alongside "everything".
        pairs() {
            const seen = new Set();
            const list = [];
            for (const so of Object.values(store.telemetryItems)) {
                const key = `${so.EdgeName}/${so.PackageName}`;
                if (!seen.has(key)) {
                    seen.add(key);
                    list.push(key);
                }
            }
            return list.sort();
        },
        filtered() {
            const term = this.filter.toLowerCase();
            return Object.entries(store.telemetryItems)
                .filter(([key, so]) => (!this.packageFilter || so.PackageName === this.packageFilter) && (!term || key.toLowerCase().includes(term)));
        },
        // Grouped by Edge/Package instead of one flat table - every row otherwise repeats the same
        // "PC-ELIE/TPLinkSmartHome/" prefix, which is most of what was stretching that column out to
        // hundreds of empty pixels on a wide screen.
        grouped() {
            const groups = {};
            for (const [, so] of this.filtered) {
                const groupKey = `${so.EdgeName}/${so.PackageName}`;
                (groups[groupKey] ||= []).push(so);
            }
            for (const list of Object.values(groups)) {
                list.sort((a, b) => a.Name.localeCompare(b.Name));
            }
            return groups;
        },
        // Looked up live by key (not the object captured at click time) so "Refresh" and incoming
        // pushes from the wildcard subscription - which replace the whole entry at that key - are
        // reflected in the open modal instead of it going on showing a stale snapshot.
        viewing() {
            return this.viewingKey ? store.telemetryItems[this.viewingKey] : null;
        }
    },
    mounted() {
        // A hard reload straight to this route can mount before store.connect() has finished
        // opening the SignalR connections - defer until there's actually a live one to subscribe on.
        store.onConnected(() => {
            // registerTelemetryItemLink("*","*","*","*", cb) both subscribes to future UpdateTelemetryItem
            // pushes AND requests the current snapshot in one call.
            store.consumer.client.registerTelemetryItemLink("*", "*", "*", "*", (so) => {
                store.telemetryItems[`${so.EdgeName}/${so.PackageName}/${so.Name}`] = so;
            });
        });
    },
    methods: {
        view(so) {
            this.viewingKey = `${so.EdgeName}/${so.PackageName}/${so.Name}`;
        },
        hasType(so) {
            return hasTypeDescriptor(so.PackageName, so.Type);
        },
        // The full type is still available via the title tooltip and the detail modal - showing just
        // the class name here (not the whole "OrbitMesh.TPLinkSmartHome." namespace) is most of
        // what was pushing that column far wider than the content needed.
        shortType(type) {
            return type ? type.split(".").pop() : "N/A";
        },
        previewValue(value) {
            if (value === null || value === undefined) {
                return "N/A";
            }
            if (typeof value === "string") {
                return value;
            }
            if (typeof value === "number" || typeof value === "boolean") {
                return String(value);
            }
            try {
                return JSON.stringify(value);
            } catch {
                return String(value);
            }
        },
        formatValue(value) {
            return JSON.stringify(value, null, 2);
        },
        refresh(so) {
            store.consumer.server.requestTelemetryItems(so.EdgeName, so.PackageName, so.Name, so.Type);
        },
        copyValue(so) {
            navigator.clipboard.writeText(this.formatValue(so.Value));
        },
        async purgeOne(so) {
            if (!(await store.confirm(`Purge ${so.EdgeName}/${so.PackageName}/${so.Name}?`))) {
                return;
            }
            store.controller.server.purgeTelemetryItems(so.EdgeName, so.PackageName, so.Name, so.Type);
            delete store.telemetryItems[`${so.EdgeName}/${so.PackageName}/${so.Name}`];
            this.viewingKey = null;
        },
        // A single scoped action instead of one Purge button per row: either everything under a
        // chosen Edge/Package (server-side wildcard), or just the currently-expired entries
        // (no server-side "expired only" filter exists, so that case is done client-side, one call
        // per already-known expired object).
        async purgeSelection() {
            this.purging = true;
            try {
                if (this.purgeOnlyExpired) {
                    const targets = Object.entries(store.telemetryItems).filter(([, so]) => so.IsExpired && (!this.purgeTarget || `${so.EdgeName}/${so.PackageName}` === this.purgeTarget));
                    for (const [key, so] of targets) {
                        store.controller.server.purgeTelemetryItems(so.EdgeName, so.PackageName, so.Name, so.Type);
                        delete store.telemetryItems[key];
                    }
                } else if (this.purgeTarget) {
                    const [edgeName, packageName] = this.purgeTarget.split("/");
                    store.controller.server.purgeTelemetryItems(edgeName, packageName, "*", "*");
                    for (const key of Object.keys(store.telemetryItems)) {
                        if (`${store.telemetryItems[key].EdgeName}/${store.telemetryItems[key].PackageName}` === this.purgeTarget) {
                            delete store.telemetryItems[key];
                        }
                    }
                } else if (await store.confirm("Purge every telemetry item from every package?")) {
                    store.controller.server.purgeTelemetryItems("*", "*", "*", "*");
                    for (const key of Object.keys(store.telemetryItems)) {
                        delete store.telemetryItems[key];
                    }
                }
                this.showPurge = false;
            } finally {
                this.purging = false;
            }
        }
    },
    template: `
        <div>
            <h2>Telemetry</h2>
            <div class="log-filters">
                <input v-model="filter" placeholder="Filter by edge/package/name" style="width:300px" />
                <select v-model="packageFilter">
                    <option value="">All packages</option>
                    <option v-for="p in packages" :key="p" :value="p">{{ p }}</option>
                </select>
                <button @click="showPurge = !showPurge">{{ showPurge ? 'Close' : 'Purge...' }}</button>
            </div>
            <div v-if="showPurge" class="deploy-form">
                <div class="rule-field">
                    <label>Scope</label>
                    <select v-model="purgeTarget">
                        <option value="">Everything</option>
                        <option v-for="p in pairs" :key="p" :value="p">{{ p }}</option>
                    </select>
                </div>
                <label><input type="checkbox" v-model="purgeOnlyExpired" /> Only expired</label>
                <button class="btn-danger" :disabled="purging" @click="purgeSelection">{{ purging ? 'Purging...' : 'Purge' }}</button>
            </div>
            <div v-for="(objects, groupKey) in grouped" :key="groupKey" class="package-group">
                <h3>{{ groupKey }}</h3>
                <table class="data-table so-table">
                    <thead>
                        <tr><th>Name</th><th>Value</th><th>Type</th><th>Validity</th><th>Last update</th><th>Actions</th></tr>
                    </thead>
                    <tbody>
                        <tr v-for="so in objects" :key="so.Name">
                            <td>{{ so.Name }}</td>
                            <td :title="previewValue(so.Value)"><span class="telemetry-value">{{ previewValue(so.Value) }}</span></td>
                            <td :title="so.Type">{{ shortType(so.Type) }}</td>
                            <td><span class="badge" :class="so.IsExpired ? 'disconnected' : 'connected'">{{ so.IsExpired ? 'Expired' : 'Valid' }}</span></td>
                            <td>{{ new Date(so.LastUpdate).toLocaleTimeString() }}</td>
                            <td class="actions"><button @click="view(so)">View</button></td>
                        </tr>
                    </tbody>
                </table>
            </div>
            <p v-if="Object.keys(grouped).length === 0" class="empty">No telemetry items yet.</p>
            <modal :show="viewing !== null" :title="viewing ? viewing.EdgeName + '/' + viewing.PackageName + '/' + viewing.Name : ''" @close="viewingKey = null">
                <template v-if="viewing">
                    <div class="modal-line">
                        <span>Name: <strong>{{ viewing.Name }}</strong></span>
                        <span class="muted">Pushed by: {{ viewing.EdgeName }}/{{ viewing.PackageName }}</span>
                    </div>
                    <div class="modal-line">
                        <span>Last update: {{ new Date(viewing.LastUpdate).toLocaleString() }}</span>
                    </div>
                    <div class="modal-line">
                        <span>Lifetime: {{ viewing.Lifetime === 0 ? 'never expires' : viewing.Lifetime + ' sec' }}</span>
                        <span class="badge" :class="viewing.IsExpired ? 'disconnected' : 'connected'">{{ viewing.IsExpired ? 'Expired' : 'Valid' }}</span>
                    </div>
                    <div class="modal-line">
                        <span>
                            Type:
                            <a v-if="hasType(viewing)" href="#" @click.prevent="viewingType = viewing.Type">{{ viewing.Type }}</a>
                            <span v-else>{{ viewing.Type || 'N/A' }}</span>
                        </span>
                    </div>
                    <div class="modal-block">
                        <span class="modal-block-label">Metadata</span>
                        <div v-if="viewing.Metadatas && Object.keys(viewing.Metadatas).length">
                            <div v-for="(v, k) in viewing.Metadatas" :key="k">{{ k }}: {{ v }}</div>
                        </div>
                        <span v-else class="empty">N/A</span>
                    </div>
                    <div class="modal-block">
                        <span class="modal-block-label">Value</span>
                        <code-editor :model-value="formatValue(viewing.Value)" mode="application/json" read-only />
                    </div>
                </template>
                <template #footer>
                    <div class="modal-footer-left">
                        <button @click="refresh(viewing)">Refresh</button>
                        <button class="btn-danger" @click="purgeOne(viewing)">Purge</button>
                        <button @click="copyValue(viewing)">Copy value</button>
                    </div>
                    <button @click="viewingKey = null">Close</button>
                </template>
            </modal>
            <type-descriptor-modal :package-name="viewing?.PackageName" :type-name="viewingType" @close="viewingType = null" />
        </div>
    `
};
