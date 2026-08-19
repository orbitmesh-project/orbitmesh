import { store } from "./store.js";

// Non-blocking replacement for the alert() dialogs the console used to pop for one-way messages -
// mounted once at the app root (see app.js) so any page can just call store.notify(...).
export default {
    data() {
        return { store };
    },
    template: `
        <div class="toast-stack">
            <transition-group name="toast">
                <div v-for="t in store.toasts" :key="t.id" class="toast" :class="'toast-' + t.type">
                    <span>{{ t.message }}</span>
                    <button class="toast-close" @click="store.dismissToast(t.id)">&times;</button>
                </div>
            </transition-group>
        </div>
    `
};
