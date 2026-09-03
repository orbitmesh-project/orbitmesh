import { store } from "../Scripts/store.js";
import CodeEditor from "../Scripts/CodeEditor.js";
import Modal from "../Scripts/Modal.js";

export default {
    components: { CodeEditor, Modal },
    data() {
        return { store, inputs: {}, sagas: {}, invoking: {}, filter: "", packageFilter: "", viewingKey: null };
    },
    computed: {
        activePackageNames() {
            const active = new Set();
            for (const pkg of Object.values(store.packages)) {
                if (pkg.IsConnected && pkg.State === "Started" && pkg.Package?.Name) {
                    active.add(pkg.Package.Name);
                }
            }
            return active;
        },
        packages() {
            const names = new Set();
            for (const entry of Object.values(store.messageHandlers)) {
                if (this.activePackageNames.has(entry.PackageName)) {
                    names.add(entry.PackageName);
                }
            }
            return [...names].sort();
        },
        // Grouped by package instead of one flat table - a fixed 3-column grid forced every row to the
        // same column widths regardless of content, leaving a huge empty "Parameters" cell next to
        // handlers that take none, and a huge empty "Description" cell next to short ones. A per-row
        // flex layout inside a package group lets each part size to what it actually holds.
        grouped() {
            const term = this.filter.toLowerCase();
            const groups = {};
            for (const [key, entry] of Object.entries(store.messageHandlers)) {
                if (!this.activePackageNames.has(entry.PackageName)) {
                    continue;
                }
                if ((this.packageFilter && entry.PackageName !== this.packageFilter) || (term && !key.toLowerCase().includes(term))) {
                    continue;
                }
                (groups[entry.PackageName] ||= []).push([key, entry]);
            }
            for (const list of Object.values(groups)) {
                list.sort(([a], [b]) => a.localeCompare(b));
            }
            return groups;
        }
    },
    methods: {
        paramKey(key, paramName) {
            return `${key}/${paramName}`;
        },
        async invoke(key, entry) {
            const params = entry.MessageHandler.Parameters || [];
            const values = params.map((p) => this.castValue(this.inputs[this.paramKey(key, p.Name)], p.TypeName));
            // Positional args for a multi-parameter handler travel as a JSON array; a single-parameter
            // one receives its raw value directly (see PackageHost.GetMessageArguments server-side).
            const data = values.length === 1 ? values[0] : values;
            // Package scope routes by group name, and every package process also joins a group named
            // after its *edge* (see OrbitMeshHub.OnConnectedAsync) - so including the edge
            // name here would broadcast to every package on that edge, not just this one. Any of
            // them sharing the same message key (e.g. "Ping") would reply too, and since
            // sendMessageWithSaga never unregisters its listener, whichever answer lands last silently
            // overwrites the right one.
            const scope = { Scope: "Package", Args: [entry.PackageName] };
            // The server already namespaces this under the package's own name unless the handler opted
            // into Shared - use it as-is rather than deriving it from the store's own dictionary key.
            const messageKey = entry.MessageHandler.MessageKey;
            this.invoking[key] = true;
            this.sagas[key] = { messageKey, scope, requestDate: new Date(), requestData: data, responseDate: null, response: null, showRequest: false };
            try {
                await new Promise((resolve) => {
                    store.consumer.server.sendMessageWithSaga(
                        (message) => {
                            const saga = this.sagas[key];
                            saga.responseDate = new Date();
                            saga.response = message;
                            saga.scope.SagaId = message.Scope.SagaId;
                            resolve();
                        },
                        scope,
                        messageKey,
                        data
                    );
                    setTimeout(resolve, 5000);
                });
            } finally {
                this.invoking[key] = false;
            }
        },
        elapsedSeconds(saga) {
            return ((saga.responseDate - saga.requestDate) / 1000).toFixed(3);
        },
        copyRequestData(saga) {
            navigator.clipboard.writeText(JSON.stringify(saga.requestData, null, 2));
        },
        copyResponseData(saga) {
            navigator.clipboard.writeText(JSON.stringify(saga.response?.Data, null, 2));
        },
        hasResponse(key) {
            return !!this.sagas[key]?.response;
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
        }
    },
    template: `
        <div>
            <h2>Message Handlers</h2>
            <div class="log-filters">
                <input v-model="filter" placeholder="Filter by package/key" style="width:220px" />
                <select v-model="packageFilter">
                    <option value="">All packages</option>
                    <option v-for="p in packages" :key="p" :value="p">{{ p }}</option>
                </select>
            </div>
            <div v-for="(handlers, pkg) in grouped" :key="pkg" class="package-group">
                <h3>{{ pkg }}</h3>
                <div v-for="[key, entry] in handlers" :key="key" class="handler-row">
                    <div class="handler-info">
                        <strong>{{ entry.MessageHandler.MessageKey.split('/').pop() }}</strong>
                        <div v-if="entry.MessageHandler.Description" class="empty">{{ entry.MessageHandler.Description }}</div>
                    </div>
                    <div class="handler-params">
                        <div v-for="p in entry.MessageHandler.Parameters" :key="p.Name" class="param-row">
                            <label>{{ p.Name }} ({{ p.TypeName.split('.').pop() }})</label>
                            <input v-model="inputs[paramKey(key, p.Name)]" />
                        </div>
                    </div>
                    <div class="handler-actions">
                        <button :disabled="invoking[key]" @click="invoke(key, entry)">{{ invoking[key] ? 'Invoking...' : 'Invoke' }}</button>
                        <button v-if="hasResponse(key)" @click="viewingKey = key">View result</button>
                    </div>
                </div>
            </div>
            <p v-if="Object.keys(grouped).length === 0" class="empty">No message handlers declared yet.</p>
            <modal :show="viewingKey !== null" :title="sagas[viewingKey] ? sagas[viewingKey].messageKey + (sagas[viewingKey].scope.SagaId ? ' (Saga #' + sagas[viewingKey].scope.SagaId + ')' : '') : ''" @close="viewingKey = null">
                <template v-if="sagas[viewingKey]">
                    <div class="modal-line">
                        <span>MessageKey: <strong>{{ sagas[viewingKey].messageKey }}</strong></span>
                        <span class="muted">Sent to: {{ sagas[viewingKey].scope.Scope }} [{{ sagas[viewingKey].scope.Args.join(', ') }}]</span>
                    </div>
                    <div class="modal-line">
                        <span>Request date: {{ sagas[viewingKey].requestDate.toLocaleString() }}</span>
                        <span class="muted" v-if="sagas[viewingKey].scope.SagaId">Saga Id: {{ sagas[viewingKey].scope.SagaId }}</span>
                    </div>
                    <template v-if="sagas[viewingKey].responseDate">
                        <div class="modal-line">
                            <span>Response date: {{ sagas[viewingKey].responseDate.toLocaleString() }}</span>
                            <span class="muted">Elapsed time: {{ elapsedSeconds(sagas[viewingKey]) }} sec</span>
                        </div>
                        <div class="modal-line">
                            <span>Response from: <strong>{{ sagas[viewingKey].response.Sender.FriendlyName }}</strong></span>
                            <button @click="sagas[viewingKey].showRequest = !sagas[viewingKey].showRequest">Show {{ sagas[viewingKey].showRequest ? 'Response' : 'Request' }}</button>
                        </div>
                    </template>
                    <div v-else class="modal-line"><span class="empty">Waiting for a response...</span></div>
                    <div class="modal-block" v-if="sagas[viewingKey].showRequest">
                        <span class="modal-block-label">Request data</span>
                        <code-editor :model-value="JSON.stringify(sagas[viewingKey].requestData, null, 2)" mode="application/json" read-only />
                    </div>
                    <div class="modal-block" v-else-if="sagas[viewingKey].response">
                        <span class="modal-block-label">Response data</span>
                        <code-editor :model-value="JSON.stringify(sagas[viewingKey].response.Data, null, 2)" mode="application/json" read-only />
                    </div>
                </template>
                <template #footer>
                    <div class="modal-footer-left">
                        <button @click="copyRequestData(sagas[viewingKey])">Copy request data</button>
                        <button :disabled="!sagas[viewingKey]?.response" @click="copyResponseData(sagas[viewingKey])">Copy response data</button>
                    </div>
                    <button @click="viewingKey = null">Close</button>
                </template>
            </modal>
        </div>
    `
};
