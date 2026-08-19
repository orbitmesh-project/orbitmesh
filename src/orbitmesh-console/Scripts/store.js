// A single Vue reactive() object holding all live connection/session state. Every SignalR event
// handler below mutates it directly - Vue's reactivity re-renders whatever component is bound to
// the affected field.

import * as signalrClient from "./signalr-client.js";
import * as managementApi from "./management-api.js";
import { getAccessKey, getUsername, isLoggedIn, renewCookie, logout as clearAuthCookies } from "./auth.js";

const { reactive } = Vue;

function createStore() {
    const store = reactive({
        connectionState: "Disconnected",
        orbitmeshServerUri: window.location.origin,
        username: getUsername(),
        accessKey: getAccessKey(),
        isLoggedIn: isLoggedIn(),

        edges: {},
        packages: {},
        packagesDescriptors: {},
        messageHandlers: {},
        telemetryItems: {},
        sagas: {},
        logs: [],
        logsCount: 0,
        // Error/Fatal log lines received while the user isn't looking at Console log - surfaced as a
        // red badge on that nav link (see app.js) so a package failure doesn't go unnoticed just
        // because you're on a different page. Cleared whenever Console log is actually viewed.
        unreadErrorCount: 0,

        managementAvailable: false,
        // installedConsole is filled in from GET update/sites - see rebuildComponentUpdates.
        versions: { installedConsole: null, installedServer: null },
        globalRecoveryOptions: null,
        packagesRepository: null,
        credentials: null,

        serverUpdate: null,
        staticSiteUpdates: null,
        componentUpdates: {},

        consoleFilters: { level: null, edgeName: null, packageName: null },

        // Non-blocking notifications (see Scripts/Toast.js) - replaces the plain alert() dialogs the
        // console used to pop for one-way "this succeeded" / "this field is required" messages, which
        // stole focus and had to be dismissed one by one before you could do anything else.
        toasts: [],

        // The one confirm() dialog open at a time, if any (see Scripts/ConfirmDialog.js) - null when
        // none is open. Native confirm() blocks the entire tab (including any other console tab open
        // in the same browser, since it's a real OS-level dialog) just to ask "remove this?"; this is
        // the same yes/no gate, rendered as an in-page modal instead.
        confirmDialog: null,

        // Populated by connect() below.
        consumer: null,
        controller: null
    });

    let nextToastId = 1;

    function notify(message, type = "info", timeoutMs = type === "error" ? 6000 : 3500) {
        const id = nextToastId++;
        store.toasts.push({ id, message, type });
        setTimeout(() => dismissToast(id), timeoutMs);
        return id;
    }

    function dismissToast(id) {
        const index = store.toasts.findIndex((t) => t.id === id);
        if (index !== -1) {
            store.toasts.splice(index, 1);
        }
    }

    // await store.confirm("...") in place of the blocking confirm(...) - resolves true/false once the
    // user answers. Only one at a time: a second call while one is already open replaces it, same as
    // the native dialog (a page can't have two confirm() prompts open simultaneously either).
    function confirmAction(message) {
        return new Promise((resolve) => {
            store.confirmDialog = {
                message,
                answer: (result) => {
                    store.confirmDialog = null;
                    resolve(result);
                }
            };
        });
    }

    function pushLog(entry) {
        store.logs.push(entry);
        store.logsCount++;
        if (entry.Level === "Error" || entry.Level === "Fatal") {
            store.unreadErrorCount++;
        }
        if (store.logs.length > 500) {
            store.logs.splice(0, store.logs.length - 500);
        }
    }

    function clearUnreadErrors() {
        store.unreadErrorCount = 0;
    }

    function rebuildComponentUpdates() {
        const componentUpdates = {};
        const server = store.serverUpdate;
        if (server && server.IsUpdateAvailable) {
            componentUpdates["Server"] = { version: server.LatestVersion, kind: "server", canAutoApply: server.CanAutoApply, zipUrl: server.ZipUrl };
        }
        for (const site of store.staticSiteUpdates || []) {
            if (site.IsUpdateAvailable) {
                componentUpdates[site.Path] = { version: site.LatestVersion, kind: "site", slug: site.ProjectSlug, canAutoApply: true, zipUrl: site.ZipUrl };
            }
            // The Server already tracks what's actually deployed - read the Console's own version from it.
            if (site.ProjectSlug === "orbitmesh-console" && site.CurrentVersion) {
                store.versions.installedConsole = site.CurrentVersion;
            }
        }
        store.componentUpdates = componentUpdates;
    }

    async function requestLatestVersions() {
        if (!store.managementAvailable) {
            return;
        }
        const [serverUpdate, staticSiteUpdates] = await Promise.all([
            managementApi.getUpdateStatus(),
            managementApi.getStaticSiteUpdates()
        ]);
        store.serverUpdate = serverUpdate;
        store.staticSiteUpdates = staticSiteUpdates;
        rebuildComponentUpdates();
    }

    async function forceCheckForUpdates() {
        if (!store.managementAvailable) {
            return;
        }
        const [serverUpdate, staticSiteUpdates] = await Promise.all([
            managementApi.checkForUpdate(),
            managementApi.checkStaticSiteUpdates()
        ]);
        store.serverUpdate = serverUpdate;
        store.staticSiteUpdates = staticSiteUpdates;
        rebuildComponentUpdates();
    }

    async function loadEdges() {
        if (!store.managementAvailable) {
            return;
        }
        const edges = await managementApi.getEdges();
        for (const edge of edges) {
            if (!store.edges[edge.Name]) {
                store.edges[edge.Name] = { Description: { EdgeName: edge.Name } };
            }
            store.edges[edge.Name].Credential = edge.Credential;
            store.controller.server.requestPackagesList(edge.Name);
        }
        for (const name of Object.keys(store.edges)) {
            if (!edges.some((s) => s.Name === name)) {
                for (const key of Object.keys(store.packages)) {
                    if (store.packages[key].EdgeName === name) {
                        delete store.packages[key];
                    }
                }
                delete store.edges[name];
            }
        }
    }

    async function loadCredentials() {
        if (!store.managementAvailable) {
            return;
        }
        store.credentials = await managementApi.getCredentials(true);
    }

    async function loadPackageRepository() {
        if (!store.managementAvailable) {
            return;
        }
        store.packagesRepository = await managementApi.getPackages();
    }

    async function initManagementFeatures() {
        try {
            await managementApi.checkAccess();
        } catch {
            store.managementAvailable = false;
            return;
        }
        store.managementAvailable = true;
        managementApi.getServerVersion().then((v) => { store.versions.installedServer = v; });
        managementApi.getGlobalRecoveryOptions().then((v) => { store.globalRecoveryOptions = v; });
        loadPackageRepository();
        requestLatestVersions();
    }

    function wireControllerEvents() {
        const controller = store.controller;

        controller.client.onReceiveLogMessage((message) => {
            const filters = store.consoleFilters;
            if ((filters.level && filters.level !== message.Level) ||
                (filters.edgeName && filters.edgeName.toLowerCase() !== message.EdgeName.toLowerCase()) ||
                (filters.packageName && filters.packageName.toLowerCase() !== message.PackageName.toLowerCase())) {
                return;
            }
            pushLog(message);
            if (message.Message.indexOf("Declaring PackageDescriptor") === 0) {
                controller.server.requestPackageDescriptor(message.PackageName);
            }
        });

        controller.client.onUpdateEdge((edge) => {
            const existing = store.edges[edge.Description.EdgeName];
            if (!existing) {
                store.edges[edge.Description.EdgeName] = edge;
            } else {
                existing.IsConnected = edge.IsConnected;
                existing.RegistrationDate = edge.RegistrationDate;
                existing.Description = edge.Description;
            }
            if (!store.managementAvailable) {
                controller.server.requestPackagesList(edge.Description.EdgeName);
            }
        });

        controller.client.onUpdatePackageList((message) => {
            for (const entry of message.List) {
                const key = `${message.EdgeName}/${entry.Package.Name}`;
                const existing = store.packages[key];
                if (!existing) {
                    store.packages[key] = entry;
                } else {
                    Object.assign(existing, entry);
                }
                store.packages[key].Package.Enable = true;
                controller.server.requestPackageDescriptor(entry.Package.Name);
            }
            for (const key of Object.keys(store.packages)) {
                const pkg = store.packages[key];
                if (pkg.EdgeName === message.EdgeName && !message.List.some((e) => e.Package.Name === pkg.Package.Name)) {
                    delete store.packages[key];
                }
            }
        });

        controller.client.onUpdatePackageDescriptor((message) => {
            store.packagesDescriptors[message.PackageName] = message.Descriptor;
            for (const key of Object.keys(store.messageHandlers)) {
                if (key.startsWith(message.PackageName + "/")) {
                    delete store.messageHandlers[key];
                }
            }
            if (message.Descriptor && message.Descriptor.MessageHandlers) {
                for (const handler of message.Descriptor.MessageHandlers) {
                    store.messageHandlers[`${message.PackageName}/${handler.MessageKey}`] = { PackageName: message.PackageName, MessageHandler: handler };
                }
            }
        });

        controller.client.onReportPackageState((message) => {
            const pkg = store.packages[`${message.EdgeName}/${message.PackageName}`];
            if (!pkg) {
                controller.server.requestPackagesList(message.EdgeName);
                return;
            }
            pkg.State = message.State;
            pkg.IsConnected = message.IsConnected;
            pkg.ConnectionId = message.ConnectionId;
            pkg.LastUpdate = message.LastUpdate;
            pkg.PackageVersion = message.PackageVersion;
            pkg.OrbitMeshClientVersion = message.OrbitMeshClientVersion;
            pkg.OrbitMeshClientType = message.OrbitMeshClientType;
            Object.assign(pkg.Package, message.Package);
            if (message.State === "Started" && message.IsConnected) {
                setTimeout(() => controller.server.requestPackageDescriptor(message.PackageName), 1000);
            }
        });

        controller.client.onReportPackageUsage((message) => {
            const pkg = store.packages[`${message.EdgeName}/${message.PackageName}`];
            if (pkg) {
                pkg.CPU = message.CPU;
                pkg.RAM = message.RAM;
            }
        });
    }

    let connectedAtLeastOnce = false;

    function wireConnectionState() {
        store.consumer.connection.onConnectionStateChanged((change) => {
            if (change.newState === signalrClient.ConnectionState.disconnected) {
                store.controller.connection.stop();
            }
        });

        store.controller.connection.onConnectionStateChanged((change) => {
            const { ConnectionState } = signalrClient;
            if (change.newState === ConnectionState.reconnecting) {
                store.connectionState = "Reconnecting";
            } else if (change.newState === ConnectionState.connecting) {
                store.connectionState = "Connecting";
            } else if (change.newState === ConnectionState.connected) {
                renewCookie();
                store.connectionState = "Connected";
                initManagementFeatures();
                connectedAtLeastOnce = true;
                store.controller.server.requestEdgeUpdates();
                loadEdges();
            } else if (change.newState === ConnectionState.disconnected) {
                store.connectionState = "Disconnected";
                store.managementAvailable = false;
                store.packages = {};
                store.edges = {};
                store.telemetryItems = {};
                store.packagesDescriptors = {};
                store.messageHandlers = {};
                store.consumer.connection.stop();
            }
        });
    }

    function connect() {
        const friendlyName = "ControlCenter:" + store.username;
        store.consumer = signalrClient.createOrbitMeshConsumer(store.orbitmeshServerUri, store.accessKey, friendlyName);
        store.controller = signalrClient.createOrbitMeshController(store.orbitmeshServerUri, store.accessKey, friendlyName);
        managementApi.initializeClient(store.orbitmeshServerUri, store.accessKey, friendlyName);

        wireControllerEvents();
        wireConnectionState();

        store.consumer.connection.start();
        store.controller.connection.start();
    }

    function logout() {
        if (store.controller) {
            store.controller.connection.stop();
        }
        if (store.consumer) {
            store.consumer.connection.stop();
        }
        clearAuthCookies();
        store.isLoggedIn = false;
        store.username = "";
        store.accessKey = "";
    }

    // Called after a successful login (see Pages/Login.js) - store.username/accessKey are read from
    // the cookies auth.js just set, then the SignalR connections are opened for the first time.
    function signIn() {
        store.username = getUsername();
        store.accessKey = getAccessKey();
        store.isLoggedIn = true;
        connect();
    }

    // A page component's mounted() hook can run before store.connect() has finished opening the
    // SignalR connections (e.g. a hard reload straight to /telemetry) - calling
    // store.consumer.server.X() at that point throws, since the connection hasn't started yet.
    // Callers that need a live connection should go through this instead of touching
    // store.consumer/store.controller directly on mount.
    function onConnected(callback) {
        if (store.connectionState === "Connected") {
            callback();
            return;
        }
        const stop = Vue.watch(() => store.connectionState, (state) => {
            if (state === "Connected") {
                stop();
                callback();
            }
        });
    }

    // Same race as onConnected, one step later: initManagementFeatures() is an async call kicked off
    // right after connectionState flips to "Connected", so a page whose mounted() only waits on
    // onConnected can still run before managementAvailable itself turns true on a hard reload.
    function onManagementAvailable(callback) {
        if (store.managementAvailable) {
            callback();
            return;
        }
        const stop = Vue.watch(() => store.managementAvailable, (available) => {
            if (available) {
                stop();
                callback();
            }
        });
    }

    store.connect = connect;
    store.signIn = signIn;
    store.logout = logout;
    store.onConnected = onConnected;
    store.onManagementAvailable = onManagementAvailable;
    store.loadEdges = loadEdges;
    store.loadCredentials = loadCredentials;
    store.loadPackageRepository = loadPackageRepository;
    store.notify = notify;
    store.dismissToast = dismissToast;
    store.confirm = confirmAction;
    store.requestLatestVersions = requestLatestVersions;
    store.forceCheckForUpdates = forceCheckForUpdates;
    store.clearUnreadErrors = clearUnreadErrors;

    return store;
}

// One console, one store - a plain module-level singleton is enough (no need for Vue's
// provide/inject ceremony for something that's never instantiated more than once).
export const store = createStore();
