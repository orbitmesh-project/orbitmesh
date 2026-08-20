import { store } from "../Scripts/store.js";
import * as api from "../Scripts/management-api.js";
import Modal from "../Scripts/Modal.js";

export default {
    components: { Modal },
    data() {
        return {
            store, renaming: null, newName: "", uploading: false, showBrowse: false,
            nuGetQuery: "", nuGetResults: [], nuGetSearched: false, nuGetSearching: false, installing: null,
            nuGetUpdates: {}, // PackageFile (no .zip) -> NuGetPackageUpdateStatus
            // checkNuGetUpdates() calls out to each feed and is much slower than loading the repository
            // listing itself - without this, every row shows "local upload" for that first stretch (an
            // empty nuGetUpdates looks identical to "checked, no feed found") and then jarringly flips
            // to the real feed once the response lands.
            nuGetUpdatesLoaded: false
        };
    },
    computed: {
        // NuGet Id -> installed version, for any package on disk that has feed provenance -
        // lets the browse modal show "Deploy" (not installed) vs "Update" (installed, older).
        installedNuGetVersions() {
            const map = {};
            for (const status of Object.values(this.nuGetUpdates)) {
                map[status.Id] = status.InstalledVersion;
            }
            return map;
        }
    },
    mounted() {
        // A hard reload straight to this route can mount before managementAvailable turns true.
        store.onManagementAvailable(() => {
            store.loadPackageRepository();
            this.refreshUpdates();
        });
    },
    methods: {
        packageKey(pkg) {
            return pkg.Filename.replace(/\.zip$/i, "");
        },
        updateStatus(pkg) {
            return this.nuGetUpdates[this.packageKey(pkg)];
        },
        async searchNuGet() {
            this.nuGetSearching = true;
            this.nuGetSearched = false;
            try {
                this.nuGetResults = await api.searchNuGetFeeds(this.nuGetQuery);
            } finally {
                this.nuGetSearching = false;
                this.nuGetSearched = true;
            }
        },
        // Deploy (not installed yet) vs Update (installed, feed has a different version) vs a plain
        // disabled "Installed".
        feedAction(hit) {
            const installedVersion = this.installedNuGetVersions[hit.Id];
            if (installedVersion === undefined) {
                return { label: "Deploy", cls: "btn-deploy" };
            }
            if (installedVersion !== hit.Version) {
                return { label: "Update", cls: "btn-deploy" };
            }
            return { label: "Installed", cls: "", disabled: true };
        },
        async installFromFeed(hit) {
            const versions = await api.getNuGetPackageVersions(hit.FeedName, hit.Id);
            const latest = versions[0];
            if (!(await store.confirm(`Install ${hit.Id} ${latest} from '${hit.FeedName}'?`))) {
                return;
            }
            this.installing = hit.Id;
            try {
                await api.installNuGetPackage(hit.FeedName, hit.Id, latest);
                await store.loadPackageRepository();
                await this.refreshUpdates();
            } finally {
                this.installing = null;
            }
        },
        async refreshUpdates() {
            try {
                const statuses = await api.checkNuGetUpdates();
                this.nuGetUpdates = Object.fromEntries(statuses.map((s) => [s.PackageFile, s]));
            } finally {
                this.nuGetUpdatesLoaded = true;
            }
        },
        async updateFromFeed(pkg) {
            const status = this.updateStatus(pkg);
            if (!status || !(await store.confirm(`Update ${this.packageKey(pkg)} to ${status.LatestVersion}?`))) {
                return;
            }
            this.installing = status.Id;
            try {
                await api.installNuGetPackage(status.Feed, status.Id, status.LatestVersion);
                await store.loadPackageRepository();
                await this.refreshUpdates();
            } finally {
                this.installing = null;
            }
        },
        async remove(pkg) {
            if (!(await store.confirm(`Remove ${pkg.Filename} from the repository?`))) {
                return;
            }
            await api.removePackageFile(pkg.Filename);
            store.loadPackageRepository();
        },
        startRename(pkg) {
            this.renaming = pkg.Filename;
            this.newName = pkg.Filename;
        },
        async confirmRename() {
            await api.renamePackageFile(this.renaming, this.newName);
            this.renaming = null;
            store.loadPackageRepository();
        },
        async onFileSelected(event) {
            const file = event.target.files[0];
            if (!file) {
                return;
            }
            this.uploading = true;
            try {
                await api.uploadPackage(file);
                store.loadPackageRepository();
            } finally {
                this.uploading = false;
                event.target.value = "";
            }
        }
    },
    template: `
        <div>
            <h2>Package Repository</h2>
            <p style="display:flex; gap:10px; align-items:center;">
                <label class="upload-button">
                    {{ uploading ? 'Uploading...' : 'Upload package .zip' }}
                    <input type="file" accept=".zip" @change="onFileSelected" :disabled="uploading" style="display:none" />
                </label>
                <button @click="showBrowse = true; searchNuGet()">Online Package Repository</button>
                <button @click="store.loadPackageRepository(); refreshUpdates()">Refresh</button>
            </p>

            <modal :show="showBrowse" title="Online Package Repository" @close="showBrowse = false">
                <template #default>
                    <p>
                        <input v-model="nuGetQuery" placeholder="Search..." @keyup.enter="searchNuGet" style="width:100%" />
                    </p>
                    <div v-if="nuGetSearching" class="empty">Searching...</div>
                    <template v-else>
                        <div v-for="hit in nuGetResults" :key="hit.FeedName + '/' + hit.Id" class="repo-row">
                            <div class="repo-main">
                                <div class="repo-name">{{ hit.Id }} <span class="muted">{{ hit.Version }}</span></div>
                                <div class="repo-desc">{{ hit.Description || 'No description.' }}</div>
                                <div class="repo-desc muted">{{ hit.Authors || 'Unknown author' }} — {{ hit.FeedName }}</div>
                            </div>
                            <button :class="feedAction(hit).cls" :disabled="feedAction(hit).disabled || installing === hit.Id"
                                    @click="installFromFeed(hit)">
                                {{ installing === hit.Id ? '...' : feedAction(hit).label }}
                            </button>
                        </div>
                        <p v-if="nuGetResults.length === 0 && nuGetSearched" class="empty">No matching packages found on the configured feeds.</p>
                    </template>
                </template>
                <template #footer>
                    <button @click="showBrowse = false">Close</button>
                </template>
            </modal>

            <table class="data-table">
                <thead>
                    <tr><th style="width:140px">Name</th><th style="width:80px">Version</th><th style="width:80px">Runtime</th><th>Description</th><th style="width:110px">Feed</th><th style="width:160px">File</th><th style="width:90px">Size</th><th style="width:170px">Last update</th><th style="width:340px">Actions</th></tr>
                </thead>
                <tbody>
                    <tr v-for="pkg in (store.packagesRepository || [])" :key="pkg.Filename">
                        <td>{{ pkg.PackageInfo ? pkg.PackageInfo.Name : '(no manifest)' }}</td>
                        <td>{{ pkg.PackageInfo ? pkg.PackageInfo.Version : '-' }}</td>
                        <td><span v-if="pkg.PackageInfo" class="badge">{{ pkg.PackageInfo.Runtime === 'Python' ? 'Python' : '.NET' }}</span></td>
                        <td>
                            <div>{{ pkg.PackageInfo ? pkg.PackageInfo.Description : '-' }}</div>
                            <div class="muted" v-if="pkg.PackageInfo && pkg.PackageInfo.Author">{{ pkg.PackageInfo.Author }}</div>
                        </td>
                        <td>
                            <span v-if="!nuGetUpdatesLoaded" class="muted">…</span>
                            <span v-else-if="updateStatus(pkg)" class="badge" :title="updateStatus(pkg).Id">{{ updateStatus(pkg).Feed }}</span>
                            <span v-else class="muted">local upload</span>
                        </td>
                        <td v-if="renaming === pkg.Filename">
                            <input v-model="newName" @keyup.enter="confirmRename" />
                            <button @click="confirmRename">Save</button>
                            <button @click="renaming = null">Cancel</button>
                        </td>
                        <td v-else>{{ pkg.Filename }}</td>
                        <td>{{ (pkg.Size / 1024).toFixed(1) }} KB</td>
                        <td>{{ new Date(pkg.LastUpdate).toLocaleString() }}</td>
                        <td class="actions">
                            <button v-if="updateStatus(pkg)?.UpdateAvailable" class="btn-deploy"
                                    @click="updateFromFeed(pkg)" :disabled="installing"
                                    :title="'New version available: ' + updateStatus(pkg).LatestVersion">
                                Update to {{ updateStatus(pkg).LatestVersion }}
                            </button>
                            <button @click="startRename(pkg)" v-if="renaming !== pkg.Filename">Rename</button>
                            <button class="btn-danger" @click="remove(pkg)">Remove</button>
                        </td>
                    </tr>
                    <tr v-if="!store.packagesRepository || store.packagesRepository.length === 0">
                        <td colspan="9" class="empty">No packages in the repository.</td>
                    </tr>
                </tbody>
            </table>
        </div>
    `
};
