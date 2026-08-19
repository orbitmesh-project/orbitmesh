// Minimal reusable modal - console v2 otherwise favors inline expand panels (see Packages.js's
// "Manage" row), but a full value/detail view reads better as an overlay that doesn't reshuffle
// the table underneath it, and both Telemetry and Message Handlers need one.
export default {
    props: { show: Boolean, title: { type: String, default: "" } },
    emits: ["close"],
    template: `
        <div v-if="show" class="modal-overlay" @click.self="$emit('close')">
            <div class="modal-box">
                <div class="modal-header">
                    <h3>{{ title }}</h3>
                    <button class="modal-close" @click="$emit('close')">&times;</button>
                </div>
                <div class="modal-body"><slot></slot></div>
                <div class="modal-footer"><slot name="footer"></slot></div>
            </div>
        </div>
    `
};
