import { store } from "../Scripts/store.js";
import * as api from "../Scripts/management-api.js";

export default {
    data() {
        return { store, text: "", loading: false, saving: false, error: null, info: null };
    },
    mounted() {
        // A hard reload straight to this route can mount before store.connect() has called
        // management-api.js's initializeClient(), leaving its urlBase null.
        store.onConnected(() => this.load());
    },
    methods: {
        async load() {
            this.loading = true;
            this.error = null;
            try {
                // The endpoint responds with Content-Type: application/json, so management-api.js's
                // generic request() helper already parses it into an object - re-stringify for the
                // textarea rather than teaching that shared helper about one raw-text special case.
                const config = await api.getServerConfiguration();
                this.text = JSON.stringify(config, null, 2);
            } catch (err) {
                this.error = err.body || err.message;
            } finally {
                this.loading = false;
            }
        },
        // A malformed edit here can take down every credential's Management API access at once (see
        // ManagementController.SetServerConfiguration's self-lockout check) - JSON.parse locally first
        // so a typo is caught before it ever reaches the server.
        validate() {
            try {
                return JSON.parse(this.text);
            } catch (err) {
                this.error = "Invalid JSON: " + err.message;
                return undefined;
            }
        },
        async save(deploy) {
            this.error = null;
            this.info = null;
            const parsed = this.validate();
            if (parsed === undefined) {
                return;
            }
            this.saving = true;
            try {
                await api.setServerConfiguration(parsed);
                if (deploy === undefined) {
                    this.info = "Configuration saved.";
                } else {
                    await store.controller.server.reloadServerConfiguration(deploy);
                    this.info = "Configuration saved and deployed.";
                    setTimeout(() => store.loadEdges(), 1000);
                }
            } catch (err) {
                this.error = err.body || err.message;
            } finally {
                this.saving = false;
            }
        },
        download() {
            const blob = new Blob([this.text], { type: "application/json" });
            const url = URL.createObjectURL(blob);
            const anchor = document.createElement("a");
            anchor.href = url;
            anchor.download = "appsettings.json";
            anchor.click();
            URL.revokeObjectURL(url);
        }
    },
    template: `
        <div>
            <h2>Configuration Editor</h2>
            <p class="empty">Editing the raw server configuration. Saving validates the JSON structurally and refuses
            a change that would revoke your own Management API access, but a bad edit can still disrupt edges/packages - review before deploying.</p>
            <div class="actions" style="margin-bottom:10px;">
                <button :disabled="loading" @click="load">Reload from server</button>
                <button :disabled="saving" @click="save()">Save</button>
                <button :disabled="saving" @click="save(true)">Save &amp; Deploy</button>
                <button @click="download">Download</button>
            </div>
            <p v-if="error" class="error">{{ error }}</p>
            <p v-if="info" class="success-text">{{ info }}</p>
            <textarea v-model="text" spellcheck="false" class="config-editor"></textarea>
        </div>
    `
};
