import { store } from "../Scripts/store.js";
import * as api from "../Scripts/management-api.js";
import { scopeCatalog } from "./Credentials.js";

const allScopes = scopeCatalog.flatMap((g) => g.scopes.map((s) => s.value));
const readScopes = allScopes.filter((s) => s.endsWith(":read"));

// The configuration file's own spelling of a key (camelCase by default, but a hand-edited file may use
// another casing): reusing it keeps this page from adding a second, differently-cased copy of a setting.
function keyOf(obj, name) {
    return Object.keys(obj).find((k) => k.toLowerCase() === name.toLowerCase()) || name;
}
function child(obj, name) {
    const key = keyOf(obj, name);
    if (typeof obj[key] !== "object" || obj[key] === null || Array.isArray(obj[key])) {
        obj[key] = {};
    }
    return obj[key];
}
function read(obj, name, fallback) {
    const value = obj[keyOf(obj, name)];
    return value === undefined || value === null ? fallback : value;
}
function write(obj, name, value) {
    obj[keyOf(obj, name)] = value;
}

// How a mapping's scopes are shown: everything, everything read-only, or an explicit list.
function modeOf(scopes) {
    if (scopes.includes("*")) {
        return "all";
    }
    const sameAsRead = scopes.length === readScopes.length && readScopes.every((s) => scopes.includes(s));
    return sameAsRead ? "readonly" : "custom";
}
function scopesOf(mapping) {
    return mapping.mode === "all" ? ["*"] : mapping.mode === "readonly" ? [...readScopes] : [...mapping.scopes];
}

const defaults = () => ({
    localLoginEnabled: true, sessionIdleMinutes: 30, sessionMaxHours: 24,
    enable: false, displayName: "SSO", authority: "", clientId: "orbitmesh", clientSecret: "",
    requireHttpsMetadata: true, requestedScopes: "openid, profile, email, groups",
    usernameClaim: "preferred_username", groupsClaim: "groups", roleMappings: []
});

export default {
    data() {
        return { store, scopeCatalog, form: defaults(), loading: false, saving: false, error: null, info: null, expandedMapping: null };
    },
    computed: {
        redirectUri() {
            return `${String(store.orbitmeshServerUri).replace(/\/$/, "")}/rest/auth/oidc/callback`;
        },
        oidcComplete() {
            const f = this.form;
            return f.enable && f.authority.trim() !== "" && f.clientId.trim() !== "" && f.clientSecret !== "";
        }
    },
    mounted() {
        store.onConnected(() => this.load());
    },
    methods: {
        async load() {
            this.loading = true;
            this.error = null;
            try {
                const config = await api.getServerConfiguration();
                const auth = child(config, "auth");
                const oidc = child(auth, "oidc");
                const d = defaults();
                this.form = {
                    localLoginEnabled: read(auth, "localLoginEnabled", d.localLoginEnabled),
                    sessionIdleMinutes: read(auth, "sessionIdleMinutes", d.sessionIdleMinutes),
                    sessionMaxHours: read(auth, "sessionMaxHours", d.sessionMaxHours),
                    enable: read(oidc, "enable", d.enable),
                    displayName: read(oidc, "displayName", d.displayName),
                    authority: read(oidc, "authority", d.authority),
                    clientId: read(oidc, "clientId", d.clientId),
                    // The server hands back a mask for an existing secret and restores it when the mask comes
                    // back unchanged, so the real value is never in the browser.
                    clientSecret: read(oidc, "clientSecret", d.clientSecret),
                    requireHttpsMetadata: read(oidc, "requireHttpsMetadata", d.requireHttpsMetadata),
                    requestedScopes: read(oidc, "requestedScopes", d.requestedScopes.split(", ")).join(", "),
                    usernameClaim: read(oidc, "usernameClaim", d.usernameClaim),
                    groupsClaim: read(oidc, "groupsClaim", d.groupsClaim),
                    roleMappings: read(oidc, "roleMappings", []).map((m) => {
                        const scopes = read(m, "scopes", []);
                        return { group: read(m, "group", ""), scopes: scopes.filter((s) => s !== "*"), mode: modeOf(scopes) };
                    })
                };
            } catch (err) {
                this.error = err.body || err.message;
            } finally {
                this.loading = false;
            }
        },
        addMapping() {
            this.form.roleMappings.push({ group: "", scopes: [], mode: "readonly" });
            this.expandedMapping = this.form.roleMappings.length - 1;
        },
        removeMapping(index) {
            this.form.roleMappings.splice(index, 1);
            this.expandedMapping = null;
        },
        // Taking "custom" over from a preset starts from what the preset granted.
        setMode(mapping, mode) {
            if (mode === "custom" && mapping.mode !== "custom") {
                mapping.scopes = mapping.mode === "all" ? [...allScopes] : [...readScopes];
            }
            mapping.mode = mode;
        },
        mappingSummary(mapping) {
            if (mapping.mode === "all") {
                return "Administrator (every scope)";
            }
            return mapping.mode === "readonly" ? "Read-only" : `${mapping.scopes.length} scope${mapping.scopes.length === 1 ? "" : "s"}`;
        },
        validate() {
            const f = this.form;
            if (f.enable && !this.oidcComplete) {
                return "Single sign-on needs an authority, a client ID and a client secret.";
            }
            if (f.enable && f.roleMappings.filter((m) => m.group.trim() !== "").length === 0) {
                return "Add at least one role mapping - without one, nobody is allowed to sign in through the provider.";
            }
            if (!f.localLoginEnabled && !this.oidcComplete) {
                return "Local sign-in can only be turned off once single sign-on is enabled and complete - otherwise nobody could sign in.";
            }
            if (f.roleMappings.some((m) => m.group.trim() === "")) {
                return "Every role mapping needs a group name.";
            }
            if (f.roleMappings.some((m) => m.mode === "custom" && m.scopes.length === 0)) {
                return "A custom role mapping needs at least one scope.";
            }
            if (!(f.sessionIdleMinutes >= 1) || !(f.sessionMaxHours >= 1)) {
                return "Session durations must be at least 1.";
            }
            return null;
        },
        async save() {
            this.error = null;
            this.info = null;
            const problem = this.validate();
            if (problem) {
                this.error = problem;
                return;
            }
            if (!this.form.localLoginEnabled && !(await store.confirm("Turn off local sign-in? Only single sign-on will work afterwards - if the provider is unreachable or misconfigured, nobody can sign in until you edit appsettings.json on the server."))) {
                return;
            }
            this.saving = true;
            try {
                // Patch a fresh copy of the configuration rather than the one loaded when the page opened:
                // the document is replaced as a whole, so an older copy would undo changes made meanwhile.
                const config = await api.getServerConfiguration();
                const f = this.form;
                const auth = child(config, "auth");
                const oidc = child(auth, "oidc");
                write(auth, "localLoginEnabled", f.localLoginEnabled);
                write(auth, "sessionIdleMinutes", Number(f.sessionIdleMinutes));
                write(auth, "sessionMaxHours", Number(f.sessionMaxHours));
                write(oidc, "enable", f.enable);
                write(oidc, "displayName", f.displayName.trim() || "SSO");
                write(oidc, "authority", f.authority.trim());
                write(oidc, "clientId", f.clientId.trim());
                write(oidc, "clientSecret", f.clientSecret);
                write(oidc, "requireHttpsMetadata", f.requireHttpsMetadata);
                write(oidc, "requestedScopes", f.requestedScopes.split(/[\s,]+/).filter(Boolean));
                write(oidc, "usernameClaim", f.usernameClaim.trim());
                write(oidc, "groupsClaim", f.groupsClaim.trim());
                write(oidc, "roleMappings", f.roleMappings.map((m) => ({ group: m.group.trim(), scopes: scopesOf(m) })));
                await api.setServerConfiguration(config);
                this.info = "Saved. Role mappings apply right away (open sessions included); everything else takes effect once the Server restarts.";
                await this.load();
            } catch (err) {
                this.error = err.body || err.message;
            } finally {
                this.saving = false;
            }
        },
        async copyRedirectUri() {
            try {
                await navigator.clipboard.writeText(this.redirectUri);
                store.notify("Copied to clipboard.", "success");
            } catch {
                store.notify("Could not copy automatically - select and copy the value manually.", "error");
            }
        }
    },
    template: `
        <div>
            <h2>Single sign-on</h2>
            <p class="empty">Let people sign in to the Console with an OpenID Connect provider (Authelia, Keycloak, Entra ID...).
            Their provider groups decide what they can do. Local accounts and machine credentials are not affected.</p>
            <p v-if="error" class="error">{{ error }}</p>
            <p v-if="info" class="success-text">{{ info }}</p>

            <div class="deploy-form sso-form">
                <h3>Provider</h3>
                <label class="sso-row"><input type="checkbox" v-model="form.enable" /> Enable single sign-on</label>
                <label class="sso-row">Button label <input v-model="form.displayName" placeholder="SSO" /> <span class="muted">Shown as &laquo;Sign in with {{ form.displayName || 'SSO' }}&raquo;.</span></label>
                <label class="sso-row">Authority <input v-model="form.authority" placeholder="https://auth.example.com" class="sso-wide" /> <span class="muted">The provider's issuer URL.</span></label>
                <label class="sso-row">Client ID <input v-model="form.clientId" /></label>
                <label class="sso-row">Client secret <input v-model="form.clientSecret" type="password" autocomplete="new-password" /> <span class="muted">The plain secret, not the hash the provider stores. Kept as is while it shows the mask.</span></label>
                <label class="sso-row"><input type="checkbox" v-model="form.requireHttpsMetadata" /> Require HTTPS to reach the provider <span class="muted">(turn off only for a local test provider)</span></label>
                <div class="sso-row">
                    Redirect URI to register at the provider
                    <div><code>{{ redirectUri }}</code> <button type="button" @click="copyRedirectUri">Copy</button></div>
                </div>
            </div>

            <div class="deploy-form sso-form">
                <h3>Claims</h3>
                <label class="sso-row">Username claim <input v-model="form.usernameClaim" /> <span class="muted">Shown in the Console as oidc:&lt;value&gt;.</span></label>
                <label class="sso-row">Groups claim <input v-model="form.groupsClaim" /></label>
                <label class="sso-row">Requested scopes <input v-model="form.requestedScopes" class="sso-wide" /> <span class="muted">Must let the provider return the groups claim.</span></label>
            </div>

            <div class="deploy-form sso-form">
                <h3>Role mappings</h3>
                <p class="muted" style="width:100%;margin:0;">A person gets the scopes of every mapping whose group they belong to, and is refused when none matches.</p>
                <p v-if="form.roleMappings.length === 0" class="empty" style="width:100%;margin:0;">No mapping yet.</p>
                <div v-for="(mapping, index) in form.roleMappings" :key="index" class="sso-mapping">
                    <div class="sso-mapping-head">
                        <input v-model="mapping.group" placeholder="Group name" />
                        <select :value="mapping.mode" @change="setMode(mapping, $event.target.value)">
                            <option value="all">Administrator (every scope)</option>
                            <option value="readonly">Read-only</option>
                            <option value="custom">Custom...</option>
                        </select>
                        <span class="muted">{{ mappingSummary(mapping) }}</span>
                        <button type="button" v-if="mapping.mode === 'custom'" @click="expandedMapping = expandedMapping === index ? null : index">{{ expandedMapping === index ? 'Hide scopes' : 'Edit scopes' }}</button>
                        <button type="button" @click="removeMapping(index)">Remove</button>
                    </div>
                    <div v-if="mapping.mode === 'custom' && expandedMapping === index" class="scope-matrix">
                        <div v-for="group in scopeCatalog" :key="group.category" class="scope-group">
                            <strong>{{ group.category }}</strong>
                            <label v-for="s in group.scopes" :key="s.value" :title="s.description">
                                <input type="checkbox" :value="s.value" v-model="mapping.scopes" /> {{ s.label }}
                            </label>
                        </div>
                    </div>
                </div>
                <button type="button" @click="addMapping">Add mapping</button>
            </div>

            <div class="deploy-form sso-form">
                <h3>Sessions &amp; local sign-in</h3>
                <label class="sso-row">Idle timeout (minutes) <input v-model.number="form.sessionIdleMinutes" type="number" min="1" style="width:90px" /></label>
                <label class="sso-row">Maximum lifetime (hours) <input v-model.number="form.sessionMaxHours" type="number" min="1" style="width:90px" /></label>
                <label class="sso-row"><input type="checkbox" v-model="form.localLoginEnabled" /> Allow signing in with a local account <span class="muted">(username and password)</span></label>
                <p v-if="!form.localLoginEnabled" class="error" style="width:100%;margin:0;">With local sign-in off, there is no break-glass account: if the provider is down or misconfigured, nobody can sign in until appsettings.json is edited on the server.</p>
            </div>

            <div class="actions">
                <button :disabled="loading || saving" @click="save">{{ saving ? 'Saving...' : 'Save' }}</button>
                <button :disabled="loading || saving" @click="load">Discard changes</button>
            </div>
        </div>
    `
};
