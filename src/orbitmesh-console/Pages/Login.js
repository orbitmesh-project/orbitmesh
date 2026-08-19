import { store } from "../Scripts/store.js";
import { login, checkControllerAccess, getAccessKey, computeAccessKey } from "../Scripts/auth.js";
import { getSetupStatus, createAdminAccount } from "../Scripts/management-api.js";

export default {
    props: {},
    data() {
        return { needsSetup: false, checkingSetup: true, username: "Administrator", password: "", confirmPassword: "", error: null, submitting: false };
    },
    async mounted() {
        // Access-Key/PAT login was removed from the Console on purpose - Machine credentials are
        // meant for Edges/packages, and a PAT-style credential should never double as a human login.
        // Login is username/password only, which means a fresh install (zero credentials) has nothing
        // to sign in with, hence this first-run check: create the sole admin account before anything else.
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
                    const accessKey = await computeAccessKey(this.username, this.password);
                    await createAdminAccount(store.orbitmeshServerUri, this.username, accessKey);
                    this.needsSetup = false;
                    // IOptionsMonitor only picks up the write above once its file-watcher reload fires,
                    // which doesn't happen instantly - the immediate checkControllerAccess call below can
                    // otherwise race it and see the pre-creation (empty) credential list (same class of
                    // race already worked around in Credentials.js's upsert()).
                    await new Promise((resolve) => setTimeout(resolve, 500));
                }
                await login(this.username, this.password, 30);
                // login() only derives/stores a cookie - it never talks to the server. Without this
                // check, a wrong password used to silently "succeed": you'd land on an empty console
                // shell with a Disconnected badge instead of a clear error here.
                const valid = await checkControllerAccess(store.orbitmeshServerUri, this.username, getAccessKey());
                if (!valid) {
                    this.error = "Invalid credentials, or this credential lacks Console access (\"Live console data\").";
                    return;
                }
                store.signIn();
                this.$router.push("/");
            } catch (err) {
                this.error = err.message || "Login failed";
            } finally {
                this.submitting = false;
            }
        }
    },
    template: `
        <div class="login-box">
            <h2>OrbitMesh</h2>
            <template v-if="checkingSetup">
                <p class="muted">Loading...</p>
            </template>
            <form v-else @submit.prevent="submit">
                <p v-if="needsSetup" class="muted">No account exists yet - create the admin account.</p>
                <label>Username <input v-model="username" autocomplete="username" /></label>
                <label>Password <input v-model="password" type="password" :autocomplete="needsSetup ? 'new-password' : 'current-password'" /></label>
                <label v-if="needsSetup">Confirm password <input v-model="confirmPassword" type="password" autocomplete="new-password" /></label>
                <p v-if="error" class="error">{{ error }}</p>
                <button type="submit" :disabled="submitting">{{ submitting ? 'Signing in...' : (needsSetup ? 'Create admin account' : 'Sign in') }}</button>
            </form>
        </div>
    `
};
