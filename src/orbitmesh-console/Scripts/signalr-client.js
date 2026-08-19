// Thin wrapper over @microsoft/signalr - one small proxy class per hub (Consumer, Controller,
// Package), each exposing a start/stop/onConnectionStateChanged/on/invoke surface.

const nativeSignalR = window.signalR;
if (!nativeSignalR || typeof nativeSignalR.HubConnectionBuilder !== "function") {
    throw new Error("signalR: Libs/microsoft-signalr/signalr.min.js must be loaded before this script.");
}

export const ConnectionState = { connecting: 0, connected: 1, reconnecting: 2, disconnected: 4 };

function buildHubUrl(serverUri, hubName, connectionInfo) {
    const qs = Object.entries(connectionInfo)
        .filter(([, value]) => value !== undefined && value !== null)
        .map(([key, value]) => `${encodeURIComponent(key)}=${encodeURIComponent(value)}`);
    const url = serverUri.replace(/\/+$/, "") + "/signalr/hubs/" + hubName;
    return qs.length ? `${url}?${qs.join("&")}` : url;
}

// Wraps a HubConnection with a small start/stop/onConnectionStateChanged/on/invoke surface -
// every proxy below is built on top of it.
class ConnectionWrapper {
    constructor(serverUri, hubName, connectionInfo) {
        this.hubConnection = new nativeSignalR.HubConnectionBuilder()
            .withUrl(buildHubUrl(serverUri, hubName, connectionInfo))
            .withAutomaticReconnect([0, 2000, 3000, 5000, 10000, 10000])
            .configureLogging(nativeSignalR.LogLevel.Warning)
            .build();

        this._connectionStateListeners = [];
        this._currentState = ConnectionState.disconnected;

        this.hubConnection.onreconnecting(() => this._setState(ConnectionState.reconnecting));
        this.hubConnection.onreconnected(() => this._setState(ConnectionState.connected));
        this.hubConnection.onclose(() => this._setState(ConnectionState.disconnected));
    }

    _setState(newState) {
        const oldState = this._currentState;
        if (oldState === newState) {
            return;
        }
        this._currentState = newState;
        this._connectionStateListeners.forEach((listener) => listener({ oldState, newState }));
    }

    async start() {
        this._setState(ConnectionState.connecting);
        try {
            await this.hubConnection.start();
            this._setState(ConnectionState.connected);
        } catch (err) {
            this._setState(ConnectionState.disconnected);
            throw err;
        }
    }

    stop() {
        return this.hubConnection.stop();
    }

    onConnectionStateChanged(listener) {
        this._connectionStateListeners.push(listener);
        return this;
    }

    on(eventName, callback) {
        this.hubConnection.on(eventName, (...args) => callback(args.length <= 1 ? args[0] : args));
        return this;
    }

    invoke(methodName, ...args) {
        return this.hubConnection.invoke(methodName, ...args);
    }
}

// Shared by Consumer/Package proxies below - both expose the same message/telemetry surface.
function messagingClientMethods(connection) {
    return {
        onReceiveMessage: (callback) => connection.on("ReceiveMessage", callback),
        registerMessageHandler(messageKey, callback) {
            return this.onReceiveMessage((message) => {
                if (callback && messageKey === message.Key) {
                    callback(message);
                }
            });
        },
        onUpdateTelemetryItem: (callback) => connection.on("UpdateTelemetryItem", callback)
    };
}

function messagingServerMethods(connection, clientMethods) {
    const server = {
        requestTelemetryItems: (...args) => connection.invoke("RequestTelemetryItems", ...args),
        sendMessage(scope, key, ...data) {
            const args = data.length === 0 ? [scope, key, "{}"] : [scope, key, data.length === 1 ? data[0] : data];
            return connection.invoke("SendMessage", ...args);
        },
        // Saga = a request/response conversation over what's otherwise a fire-and-forget message bus:
        // a SagaId is stamped on the outgoing message, and the first ReceiveMessage echoing that same
        // SagaId back is treated as the reply and handed to `callback`.
        sendMessageWithSaga(callback, scope, key, ...data) {
            if (scope.SagaId === undefined) {
                scope.SagaId = Date.now() + Math.floor(Math.random() * 1000);
            }
            const args = data.length === 1 ? data[0] : data;
            clientMethods.onReceiveMessage((message) => {
                // Loose equality: the server always echoes SagaId back as a JSON string (see
                // FlexibleStringConverter), but scope.SagaId here is still the original JS number.
                if (message.Scope.SagaId == scope.SagaId) {
                    callback(message);
                }
            });
            return server.sendMessage(scope, key, args);
        },
        subscribeMessages: (...args) => connection.invoke("SubscribeMessages", ...args),
        subscribeTelemetryItems: (...args) => connection.invoke("SubscribeTelemetryItems", ...args),
        unSubscribeMessages: (...args) => connection.invoke("UnSubscribeMessages", ...args),
        unSubscribeTelemetryItems: (...args) => connection.invoke("UnSubscribeTelemetryItems", ...args),
        requestSubscribeTelemetryItems(edgeName, packageName, name, type) {
            server.requestTelemetryItems(edgeName, packageName, name, type);
            return server.subscribeTelemetryItems(edgeName, packageName, name, type);
        }
    };
    return server;
}

class ConsumerProxy {
    constructor(connection) {
        this.connection = connection;
        this.client = messagingClientMethods(connection);
        this.client.registerTelemetryItemLink = (edgeName, packageName, name, type, callback) => {
            const result = this.client.onUpdateTelemetryItem((telemetryItem) => {
                if (callback &&
                    (telemetryItem.EdgeName === edgeName || edgeName === "*") &&
                    (telemetryItem.PackageName === packageName || packageName === "*") &&
                    (telemetryItem.Name === name || name === "*") &&
                    (telemetryItem.Type === type || type === "*")) {
                    callback(telemetryItem);
                }
            });
            this.server.requestSubscribeTelemetryItems(edgeName, packageName, name, type);
            return result;
        };
        this.server = messagingServerMethods(connection, this.client);
    }
}

class PackageProxy {
    constructor(connection) {
        this.connection = connection;
        this.client = {
            ...messagingClientMethods(connection),
            onPackageControlAction: (callback) => connection.on("PackageControlAction", callback),
            onReceiveLastTelemetryItems: (callback) => connection.on("ReceiveLastTelemetryItems", callback),
            onUpdateSettings: (callback) => connection.on("UpdateSettings", callback)
        };
        this.client.registerTelemetryItemLink = (edgeName, packageName, name, type, callback) => {
            const result = this.client.onUpdateTelemetryItem((telemetryItem) => {
                if (callback &&
                    (telemetryItem.EdgeName === edgeName || edgeName === "*") &&
                    (telemetryItem.PackageName === packageName || packageName === "*") &&
                    (telemetryItem.Name === name || name === "*") &&
                    (telemetryItem.Type === type || type === "*")) {
                    callback(telemetryItem);
                }
            });
            this.server.requestSubscribeTelemetryItems(edgeName, packageName, name, type);
            return result;
        };
        this.server = {
            ...messagingServerMethods(connection, this.client),
            declarePackageDescriptor: (...args) => connection.invoke("DeclarePackageDescriptor", ...args),
            purgeTelemetryItems: (...args) => connection.invoke("PurgeTelemetryItems", ...args),
            pushTelemetryItem: (name, value, type = "", metadatas = {}, lifetime = 0) =>
                connection.invoke("PushTelemetryItem", name, value, type, metadatas, lifetime),
            requestSettings: (...args) => connection.invoke("RequestSettings", ...args),
            writeLog: (message, level = "Info") => connection.invoke("WriteLog", message, level)
        };
    }
}

class ControllerProxy {
    constructor(connection) {
        this.connection = connection;
        this._controlGroups = {};
        this.client = {
            onReceiveLogMessage: (callback) => { this._controlGroups.PackagesLog = true; return connection.on("ReceiveLog", callback); },
            onUpdateEdge: (callback) => { this._controlGroups.Edges = true; return connection.on("UpdateEdge", callback); },
            onUpdateEdgesList: (callback) => connection.on("UpdateEdgesList", callback),
            onReportPackageState: (callback) => { this._controlGroups.PackagesState = true; return connection.on("ReportPackageState", callback); },
            onReportPackageUsage: (callback) => { this._controlGroups.PackagesUsage = true; return connection.on("ReportPackageUsage", callback); },
            onUpdatePackageList: (callback) => connection.on("UpdatePackageList", callback),
            onUpdatePackageDescriptor: (callback) => connection.on("UpdatePackageDescriptor", callback)
        };
        this.server = {
            addToControlGroup: (...args) => connection.invoke("AddToControlGroup", ...args),
            purgeTelemetryItems: (...args) => connection.invoke("PurgeTelemetryItems", ...args),
            reloadServerConfiguration: (...args) => connection.invoke("ReloadServerConfiguration", ...args),
            reload: (...args) => connection.invoke("Reload", ...args),
            removeToControlGroup: (...args) => connection.invoke("RemoveToControlGroup", ...args),
            requestPackageDescriptor: (...args) => connection.invoke("RequestPackageDescriptor", ...args),
            requestPackagesList: (...args) => connection.invoke("RequestPackagesList", ...args),
            requestEdgesList: (...args) => connection.invoke("RequestEdgesList", ...args),
            requestEdgeUpdates: (...args) => connection.invoke("RequestEdgeUpdates", ...args),
            restartEdge: (...args) => connection.invoke("RestartEdge", ...args),
            checkForEdgeUpdate: (...args) => connection.invoke("CheckForEdgeUpdate", ...args),
            restart: (...args) => connection.invoke("Restart", ...args),
            start: (...args) => connection.invoke("Start", ...args),
            stop: (...args) => connection.invoke("Stop", ...args),
            updatePackageSettings: (...args) => connection.invoke("UpdatePackageSettings", ...args)
        };
        connection.onConnectionStateChanged((change) => {
            if (change.newState === ConnectionState.connected) {
                for (const group of Object.keys(this._controlGroups)) {
                    if (this._controlGroups[group]) {
                        this.server.addToControlGroup(group);
                    }
                }
            }
        });
    }
}

export function createOrbitMeshConsumer(serverUri, accessKey, friendlyName) {
    const connection = new ConnectionWrapper(serverUri, "ConsumerHub", { EdgeName: "Consumer", PackageName: friendlyName, AccessKey: accessKey });
    return new ConsumerProxy(connection);
}

export function createOrbitMeshController(serverUri, accessKey, friendlyName) {
    const connection = new ConnectionWrapper(serverUri, "ControlHub", { EdgeName: "Controller", PackageName: friendlyName, AccessKey: accessKey });
    return new ControllerProxy(connection);
}

export function createOrbitMeshPackage(serverUri, edgeName, packageName, accessKey, packageVersion) {
    const connectionInfo = { EdgeName: edgeName, PackageName: packageName, AccessKey: accessKey, OrbitMeshClientVersion: "2.0", OrbitMeshClientType: "js" };
    if (packageVersion !== undefined) {
        connectionInfo.PackageVersion = packageVersion;
    }
    const connection = new ConnectionWrapper(serverUri, "OrbitMeshHub", connectionInfo);
    return new PackageProxy(connection);
}
