import { store } from "../Scripts/store.js";
import { login, getAuthConfig, oidcLoginUrl } from "../Scripts/auth.js";
import { getSetupStatus, createAdminAccount } from "../Scripts/management-api.js";

// Why the server sent the browser back here after a single sign-on attempt (?login_error=...).
const LOGIN_ERRORS = {
    oidc_denied: "Your account is signed in with the identity provider but isn't allowed to use OrbitMesh (none of its groups is mapped to a role).",
    oidc_failed: "Single sign-on didn't complete. Try again, or ask an administrator to check the server log."
};

export default {
    props: {},
    data() {
        return {
            needsSetup: false, checkingSetup: true, username: "Administrator", password: "", confirmPassword: "", error: null, submitting: false,
            // Both default to the classic form until the server says otherwise.
            localLoginEnabled: true, oidc: { Enabled: false, DisplayName: "" }
        };
    },
    async mounted() {
        const reason = new URLSearchParams(window.location.search).get("login_error");
        if (reason) {
            this.error = LOGIN_ERRORS[reason] || "Sign-in failed.";
        }
        try {
            const config = await getAuthConfig(store.orbitmeshServerUri);
            this.localLoginEnabled = config.LocalLoginEnabled;
            this.oidc = config.Oidc;
        } catch {
            // Server unreachable: leave the password form up and let submit() surface the real error.
        }
        // Access-Key/PAT login was removed from the Console on purpose - Machine credentials are
        // meant for Edges/packages, and a PAT-style credential should never double as a human login.
        // Login is username/password (or single sign-on), which means a fresh install (zero credentials
        // and no identity provider) has nothing to sign in with, hence this first-run check: create
        // the sole admin account before anything else.
        try {
            const status = await getSetupStatus(store.orbitmeshServerUri);
            this.needsSetup = status.NeedsSetup;
        } catch {
            // Server unreachable, or setup already gated some other way - fall back to the normal login
            // form and let submit() surface the real error.
        } finally {
            this.checkingSetup = false;
        }
    },
    methods: {
        async submit() {
            this.error = null;
            if (this.needsSetup && this.password !== this.confirmPassword) {
                this.error = "Passwords do not match.";
                return;
            }
            this.submitting = true;
            try {
                if (this.needsSetup) {
                    await createAdminAccount(store.orbitmeshServerUri, this.username, this.password);
                    this.needsSetup = false;
                    // IOptionsMonitor only picks up the write above once its file-watcher reload fires,
                    // which doesn't happen instantly - the sign-in below can otherwise race it and see
                    // the pre-creation (empty) credential list (same class of race already worked
                    // around in Credentials.js's upsert()).
                    await new Promise((resolve) => setTimeout(resolve, 500));
                }
                // The server checks the password and answers with the session (and its cookie): a wrong
                // password or an account with no Console access is refused right here, with the reason.
                const session = await login(store.orbitmeshServerUri, this.username, this.password);
                this.password = "";
                this.confirmPassword = "";
                store.signIn(session);
                this.$router.push("/");
            } catch (err) {
                this.error = err.message || "Login failed";
            } finally {
                this.submitting = false;
            }
        },
        signInWithProvider() {
            // A full-page navigation, not a fetch: the browser goes to the identity provider and comes back
            // through the server, which sets the session cookie and lands on the Console's root.
            const base = this.$router.options.history.base;
            window.location.href = oidcLoginUrl(store.orbitmeshServerUri, base + "/", base + "/login");
        }
    },
    template: `
        <div class="login-box">
            <h2>OrbitMesh</h2>
            <template v-if="checkingSetup">
                <p class="muted">Loading...</p>
            </template>
            <template v-else>
                <p v-if="error" class="error">{{ error }}</p>
                <div v-if="oidc.Enabled" style="margin-bottom: 12px;">
                    <button type="button" @click="signInWithProvider">Sign in with {{ oidc.DisplayName }}</button>
                </div>
                <form v-if="localLoginEnabled || needsSetup" @submit.prevent="submit">
                    <p v-if="oidc.Enabled" class="muted">Or with a local account:</p>
                    <p v-if="needsSetup" class="muted">No account exists yet - create the admin account.</p>
                    <label>Username <input v-model="username" autocomplete="username" /></label>
                    <label>Password <input v-model="password" type="password" :autocomplete="needsSetup ? 'new-password' : 'current-password'" /></label>
                    <label v-if="needsSetup">Confirm password <input v-model="confirmPassword" type="password" autocomplete="new-password" /></label>
                    <button type="submit" :disabled="submitting">{{ submitting ? 'Signing in...' : (needsSetup ? 'Create admin account' : 'Sign in') }}</button>
                </form>
            </template>
        </div>
    `
};
