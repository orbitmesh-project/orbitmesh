import { store } from "./store.js";
import Modal from "./Modal.js";

// Non-blocking replacement for confirm() - mounted once at the app root (see app.js), driven by
// store.confirmDialog (set by store.confirm(message), see store.js).
export default {
    components: { Modal },
    data() {
        return { store };
    },
    template: `
        <modal :show="!!store.confirmDialog" title="Confirm" @close="store.confirmDialog?.answer(false)">
            <template #default>
                <p>{{ store.confirmDialog?.message }}</p>
            </template>
            <template #footer>
                <button @click="store.confirmDialog?.answer(false)">Cancel</button>
                <button class="btn-danger" @click="store.confirmDialog?.answer(true)">Confirm</button>
            </template>
        </modal>
    `
};
