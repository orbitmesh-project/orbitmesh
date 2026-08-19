import { store } from "../Scripts/store.js";

export default {
    data() {
        return { store };
    },
    computed: {
        edgeStats() {
            const all = Object.values(store.edges);
            const connected = all.filter((s) => s.IsConnected).length;
            return { total: all.length, connected };
        },
        packageStats() {
            const all = Object.values(store.packages);
            let running = 0;
            let attention = 0;
            for (const pkg of all) {
                if (pkg.IsConnected && pkg.State === "Started") {
                    running++;
                } else if (pkg.State !== "Stopped") {
                    // Not cleanly stopped and not cleanly running (starting, disconnected while
                    // supposedly started, ...) - worth a glance, unlike an intentionally stopped package.
                    attention++;
                }
            }
            return { total: all.length, running, attention };
        },
        updateCount() {
            return Object.keys(store.componentUpdates).length;
        },
        // Same Error/Fatal criteria as the nav badge (see store.js's pushLog) - this panel is what
        // that badge is telling you to come look at.
        recentErrors() {
            return store.logs.filter((l) => l.Level === "Error" || l.Level === "Fatal").slice(-10).reverse();
        }
    },
    template: `
        <div>
            <h2>Home</h2>
            <div class="stat-cards">
                <div class="stat-card">
                    <div class="stat-value">{{ edgeStats.connected }}/{{ edgeStats.total }}</div>
                    <div class="stat-label">Edges connected</div>
                </div>
                <div class="stat-card">
                    <div class="stat-value">{{ packageStats.running }}/{{ packageStats.total }}</div>
                    <div class="stat-label">Packages running</div>
                </div>
                <div class="stat-card" :class="{ attention: packageStats.attention > 0 }">
                    <div class="stat-value">{{ packageStats.attention }}</div>
                    <div class="stat-label">Packages needing attention</div>
                </div>
                <div class="stat-card" :class="{ attention: updateCount > 0 }">
                    <div class="stat-value">{{ updateCount }}</div>
                    <div class="stat-label">Updates available</div>
                </div>
            </div>

            <h3>Recent errors</h3>
            <ul v-if="recentErrors.length > 0" class="log-list">
                <li v-for="(log, i) in recentErrors" :key="i" class="log-error">
                    [{{ log.EdgeName }}/{{ log.PackageName }}] {{ new Date(log.Date).toLocaleTimeString() }} - {{ log.Message }}
                </li>
            </ul>
            <p v-else class="empty">No errors logged this session.</p>
        </div>
    `
};
