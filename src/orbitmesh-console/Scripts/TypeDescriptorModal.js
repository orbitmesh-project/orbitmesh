import Modal from "./Modal.js";
import { findTypeDescriptor, hasTypeDescriptor, prettyGenericType } from "./typeDescriptors.js";

// Clicking a property's type inside this modal can drill into ANOTHER type descriptor (e.g. a state
// object's Type -> a property typed as another known class) - keeps a small local history stack
// rather than only ever showing the type it was opened with.
export default {
    components: { Modal },
    props: {
        packageName: { type: String, default: null },
        typeName: { type: String, default: null }
    },
    emits: ["close"],
    data() {
        return { current: this.typeName, history: [] };
    },
    watch: {
        typeName(value) {
            if (value !== null) {
                this.current = value;
                this.history = [];
            }
        }
    },
    computed: {
        descriptor() {
            return findTypeDescriptor(this.packageName, this.current);
        }
    },
    methods: {
        hasType(name) {
            return hasTypeDescriptor(this.packageName, name);
        },
        pretty(name) {
            return prettyGenericType(name);
        },
        viewSub(name) {
            this.history.push(this.current);
            this.current = name;
        },
        back() {
            this.current = this.history.pop();
        },
        close() {
            this.$emit("close");
        }
    },
    template: `
        <modal :show="typeName !== null" :title="'Type description: ' + pretty(current)" @close="close">
            <template v-if="descriptor">
                <div v-if="descriptor.Properties && descriptor.Properties.length">
                    <label>Properties:</label>
                    <ul>
                        <li v-for="p in descriptor.Properties" :key="p.Name">
                            <strong>{{ p.Name }}</strong> (
                            <a v-if="hasType(p.TypeName)" href="#" @click.prevent="viewSub(p.TypeName)">{{ pretty(p.TypeName) }}</a><span v-else>{{ pretty(p.TypeName) }}</span>
                            )
                            <div v-if="p.Description" class="empty">{{ p.Description }}</div>
                        </li>
                    </ul>
                </div>
                <div v-if="descriptor.IsEnum">
                    <label>Values:</label>
                    <ul>
                        <li v-for="e in descriptor.EnumValues" :key="e.Name">
                            <strong>{{ e.Name }}</strong><span v-if="e.Description"> : {{ e.Description }}</span>
                        </li>
                    </ul>
                </div>
                <div v-if="descriptor.IsArray || (descriptor.IsGeneric && descriptor.GenericParameters)">
                    <label>{{ descriptor.IsArray ? 'Array of:' : 'Generic of:' }}</label>
                    <ul>
                        <li v-for="g in descriptor.GenericParameters" :key="g">
                            <a v-if="hasType(g)" href="#" @click.prevent="viewSub(g)">{{ pretty(g) }}</a><span v-else>{{ pretty(g) }}</span>
                        </li>
                    </ul>
                </div>
            </template>
            <p v-else class="empty">No further type information available.</p>
            <template #footer>
                <button v-if="history.length > 0" @click="back">Back</button>
                <button @click="close">Close</button>
            </template>
        </modal>
    `
};
