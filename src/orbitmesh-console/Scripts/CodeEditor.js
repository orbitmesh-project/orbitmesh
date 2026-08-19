// Thin wrapper around the vendored CodeMirror 5 (JSON/XML syntax highlighting) - used both as a
// writable editor (Packages.js's JsonObject/XmlDocument/ConfigurationSection settings) and a
// read-only viewer (Telemetry / Message Handlers values), so it needs to support both a
// v-model and a plain display-only mode.
export default {
    props: {
        modelValue: { type: String, default: "" },
        mode: { type: String, default: "application/json" },
        readOnly: { type: Boolean, default: false }
    },
    emits: ["update:modelValue"],
    mounted() {
        this.cm = CodeMirror(this.$el, {
            value: this.modelValue || "",
            mode: this.mode,
            theme: document.documentElement.dataset.theme === "light" ? "default" : "twilight",
            lineNumbers: true,
            lineWrapping: true,
            readOnly: this.readOnly,
            viewportMargin: Infinity
        });
        if (!this.readOnly) {
            this.cm.on("change", () => {
                const value = this.cm.getValue();
                if (value !== this.modelValue) {
                    this.$emit("update:modelValue", value);
                }
            });
        }
        // The theme toggle just flips a data-attribute on <html> (see app.js) - no store/event bus
        // to hook into, so watch that attribute directly instead of wiring cross-component plumbing
        // for a single CSS-ish concern.
        this.themeObserver = new MutationObserver(() => {
            this.cm.setOption("theme", document.documentElement.dataset.theme === "light" ? "default" : "twilight");
        });
        this.themeObserver.observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
    },
    beforeUnmount() {
        this.themeObserver?.disconnect();
    },
    watch: {
        modelValue(value) {
            if (this.cm && value !== this.cm.getValue()) {
                this.cm.setValue(value || "");
            }
        }
    },
    template: `<div class="code-editor"></div>`
};
