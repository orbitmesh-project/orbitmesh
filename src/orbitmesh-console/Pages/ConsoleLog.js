import { store } from "../Scripts/store.js";

// Plain scrolling div - Vue's reactivity handles append/re-render on its own, no manual DOM
// echo() calls needed. Auto-scroll keeps the container scrolled to bottom on every new entry,
// unless the user has scrolled up to read something (checked via scrollTop).
export default {
    data() {
        return {
            store,
            autoScroll: true,
            filters: { level: "", edgeName: "", packageName: "" }
        };
    },
    computed: {
        levelClass() {
            return (level) => "log-" + level.toLowerCase();
        }
    },
    watch: {
        "store.logs.length"() {
            // Stays cleared while this page is open, not just once on mount - an error arriving while
            // you're already looking at the log shouldn't leave a stale "1" on the nav badge.
            store.clearUnreadErrors();
            if (this.autoScroll) {
                this.$nextTick(() => {
                    const el = this.$refs.logContainer;
                    if (el) {
                        el.scrollTop = el.scrollHeight;
                    }
                });
            }
        }
    },
    mounted() {
        store.clearUnreadErrors();
    },
    methods: {
        applyFilters() {
            store.consoleFilters.level = this.filters.level || null;
            store.consoleFilters.edgeName = this.filters.edgeName || null;
            store.consoleFilters.packageName = this.filters.packageName || null;
        },
        clear() {
            store.logs.splice(0, store.logs.length);
            store.logsCount = 0;
        },
        onScroll() {
            const el = this.$refs.logContainer;
            if (!el) {
                return;
            }
            // Anchored to the bottom (within a small tolerance) = keep auto-scrolling; scrolled up
            // to read older entries = stop yanking the view back down on every new line.
            this.autoScroll = el.scrollHeight - el.scrollTop - el.clientHeight < 40;
        }
    },
    template: `
        <div class="console-log">
            <h2>Console log</h2>
            <div class="log-filters">
                <input v-model="filters.edgeName" @input="applyFilters" placeholder="Edge filter" />
                <input v-model="filters.packageName" @input="applyFilters" placeholder="Package filter" />
                <select v-model="filters.level" @change="applyFilters">
                    <option value="">All levels</option>
                    <option>Debug</option>
                    <option>Info</option>
                    <option>Warn</option>
                    <option>Error</option>
                    <option>Fatal</option>
                </select>
                <button @click="clear">Clear</button>
                <label><input type="checkbox" v-model="autoScroll" /> Auto-scroll</label>
            </div>
            <div class="log-container" ref="logContainer" @scroll="onScroll">
                <div v-for="(log, i) in store.logs" :key="i" :class="levelClass(log.Level)">
                    [{{ log.EdgeName }}/{{ log.PackageName }}] {{ new Date(log.Date).toLocaleTimeString() }} : {{ log.Message }}
                </div>
                <div v-if="store.logs.length === 0" class="log-empty">No log messages yet.</div>
            </div>
        </div>
    `
};
