import { router } from "./router.js";
import { store } from "./store.js";
import * as managementApi from "./management-api.js";
import { isLoggedIn as cookieIsLoggedIn } from "./auth.js";
import Toast from "./Toast.js";
import ConfirmDialog from "./ConfirmDialog.js";

const { createApp } = Vue;

const App = {
    components: { Toast, ConfirmDialog },
    data() {
        // index.html already set this synchronously (before Vue even loads) to avoid a flash of the
        // wrong theme - just read it back here so the toggle button reflects the real state.
        return { store, showUpdates: false, applying: {}, theme: document.documentElement.dataset.theme };
    },
    computed: {
        updateCount() {
            return Object.keys(this.store.componentUpdates).length;
        },
        fleetGroupActive() {
            return ["/edges", "/packages", "/variables"].includes(this.$route.path);
        },
        administrationGroupActive() {
            return ["/repository", "/credentials", "/configuration", "/scheduledtasks"].includes(this.$route.path);
        }
    },
    methods: {
        toggleTheme() {
            this.theme = this.theme === "light" ? "dark" : "light";
            document.documentElement.dataset.theme = this.theme;
            localStorage.setItem("theme_v2", this.theme);
        },
        async applyUpdate(name, update) {
            this.applying[name] = true;
            try {
                if (update.kind === "server") {
                    await managementApi.applyUpdate();
                } else {
                    await managementApi.applyStaticSiteUpdate(update.slug);
                }
                await this.store.requestLatestVersions();
            } finally {
                this.applying[name] = false;
            }
        }
    },
    template: `
        <div id="shell">
            <header>
                <strong>OrbitMesh</strong>
                <span class="badge" :class="store.connectionState.toLowerCase()">{{ store.connectionState }}</span>
                <nav v-if="store.isLoggedIn">
                    <router-link to="/">Home</router-link>
                    <router-link to="/telemetry">Telemetry</router-link>
                    <router-link to="/messages">Messages</router-link>
                    <router-link to="/console">Console log<span v-if="store.unreadErrorCount > 0" class="nav-badge">{{ store.unreadErrorCount }}</span></router-link>
                    <div class="nav-group" :class="{ 'router-link-active': fleetGroupActive }">
                        <span class="nav-group-label">Fleet</span>
                        <div class="nav-dropdown">
                            <router-link to="/edges">Edges</router-link>
                            <router-link to="/packages">Packages</router-link>
                            <router-link to="/variables">Variables</router-link>
                        </div>
                    </div>
                    <div class="nav-group" :class="{ 'router-link-active': administrationGroupActive }">
                        <span class="nav-group-label">Administration</span>
                        <div class="nav-dropdown">
                            <router-link to="/repository">Repository</router-link>
                            <router-link to="/credentials">Credentials</router-link>
                            <router-link to="/configuration">Configuration</router-link>
                            <router-link to="/scheduledtasks">Scheduled Tasks</router-link>
                        </div>
                    </div>
                </nav>
                <div class="header-actions">
                    <div v-if="store.isLoggedIn && store.managementAvailable" class="update-bell">
                        <button class="header-btn" @click="showUpdates = !showUpdates" :class="{ badge: updateCount > 0 }" title="Component updates">
                            &#128276;<span v-if="updateCount > 0">&nbsp;{{ updateCount }}</span>
                        </button>
                        <div v-if="showUpdates" class="update-panel">
                            <div v-if="updateCount === 0" class="empty">Everything is up to date.</div>
                            <div v-for="(update, name) in store.componentUpdates" :key="name" class="update-row">
                                <span>{{ name }} &rarr; {{ update.version }}</span>
                                <button :disabled="applying[name]" @click="applyUpdate(name, update)">{{ applying[name] ? 'Applying...' : 'Apply' }}</button>
                            </div>
                            <button @click="store.forceCheckForUpdates()">Check now</button>
                        </div>
                    </div>
                    <button class="header-btn theme-toggle" @click="toggleTheme" :title="theme === 'light' ? 'Switch to dark theme' : 'Switch to light theme'">
                        {{ theme === 'light' ? '\u{1F319}' : '\u{2600}\u{FE0F}' }}
                    </button>
                    <button v-if="store.isLoggedIn" class="header-btn logout-button" @click="store.logout(); $router.push('/login')">Logout</button>
                </div>
            </header>
            <main>
                <router-view></router-view>
            </main>
            <footer v-if="store.isLoggedIn" class="app-footer">
                <span>Connected to {{ store.orbitmeshServerUri }} as {{ store.username }}<span v-if="store.versions.installedServer"> - server v{{ store.versions.installedServer }}</span></span>
                <span v-if="store.versions.installedConsole">Console v{{ store.versions.installedConsole }}</span>
                <span>&copy; {{ new Date().getFullYear() }} OrbitMesh</span>
            </footer>
            <toast></toast>
            <confirm-dialog></confirm-dialog>
        </div>
    `
};

const app = createApp(App);
app.use(router);
app.mount("#app");

// A page reload with valid auth cookies should reconnect automatically instead of bouncing through
// the login form again - store.isLoggedIn already reflects the cookies (see store.js's initial
// state), so this just needs to open the SignalR connections to match.
if (store.isLoggedIn) {
    store.connect();
}

// store.isLoggedIn otherwise only ever changes on an explicit signIn()/logout() call - it does not
// react on its own to the AccessKey cookie actually expiring, or to the server rejecting it (access
// revoked, password changed elsewhere). Without this, a user sitting on a page with no navigation
// event to trigger the router guard would be left staring at a "logged in" shell that can't load or
// save anything, instead of being bounced to /login.
function forceLogout(message) {
    if (!store.isLoggedIn) {
        return;
    }
    store.logout();
    router.push("/login");
    store.notify(message, "error");
}

managementApi.setUnauthorizedHandler(() => forceLogout("Your session has expired. Please log in again."));

function checkCookieStillValid() {
    if (store.isLoggedIn && !cookieIsLoggedIn()) {
        forceLogout("Your session has expired. Please log in again.");
    }
}

setInterval(checkCookieStillValid, 15000);
document.addEventListener("visibilitychange", () => {
    if (!document.hidden) {
        checkCookieStillValid();
    }
});
