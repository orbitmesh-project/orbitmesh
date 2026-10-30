// getLicense/setLicense are dropped on purpose - ManagementController's license endpoints are a
// confirmed dead stub, never wired up to anything real.

let urlBase = null;
let defaultHeaders = {};

// No credential travels in these headers: the request is authenticated by the HttpOnly session cookie the
// browser attaches on its own. EdgeName/PackageName are still required - for unsafe methods the server
// insists on EdgeName being a real header, which a page on another origin can't set (CSRF protection).
export function initializeClient(serverUri, friendlyName) {
    urlBase = serverUri.replace(/\/+$/, "") + "/rest/management/";
    defaultHeaders = { EdgeName: "Management", PackageName: friendlyName };
}

// Fired whenever a management API call comes back 401/403 - the session has expired or been revoked
// server-side. The console shell (app.js) uses this to force a logout and bounce to /login
// instead of leaving the user stranded on a page that silently can't load or save anything.
let unauthorizedHandler = null;
export function setUnauthorizedHandler(handler) {
    unauthorizedHandler = handler;
}

function buildUrl(method, parameters) {
    const params = new URLSearchParams(parameters || {});
    if (!params.has("timestamp")) {
        params.set("timestamp", Date.now().toString());
    }
    return urlBase + method + "?" + params.toString();
}

class ManagementApiError extends Error {
    constructor(status, body) {
        super(body || `Management API request failed (${status})`);
        this.status = status;
        this.body = body;
    }
}

async function request(method, httpMethod, parameters, body) {
    const hasBody = body !== undefined;
    const response = await fetch(buildUrl(method, parameters), {
        method: httpMethod,
        headers: hasBody ? { ...defaultHeaders, "Content-Type": "application/json" } : defaultHeaders,
        body: hasBody ? JSON.stringify(body) : undefined
    });
    if (!response.ok) {
        const text = await response.text().catch(() => "");
        if ((response.status === 401 || response.status === 403) && unauthorizedHandler) {
            unauthorizedHandler();
        }
        throw new ManagementApiError(response.status, text);
    }
    const contentType = response.headers.get("content-type") || "";
    if (contentType.includes("application/json")) {
        return response.json();
    }
    return response.text();
}

// Every value interpolated into a path below goes through this: names/ids come from the server and some
// are attacker-influenced (a pending Edge's self-declared InstanceId, package names), and an unencoded
// "../" would be normalized by the browser into a different management endpoint, called with the
// signed-in admin's own AccessKey.
const enc = encodeURIComponent;

const get = (method, parameters) => request(method, "GET", parameters);
const post = (method, body, parameters) => request(method, "POST", parameters, body === undefined ? {} : body);
const del = (method, parameters) => request(method, "DELETE", parameters);

export function getRequestUri(method, parameters) {
    return buildUrl(method, parameters);
}

// Common
export const checkAccess = () => get("CheckAccess");
export const getServerVersion = () => get("server/version");

// Server configuration
export const getServerConfiguration = () => get("configuration");
export const setServerConfiguration = (configuration) => post("configuration", configuration);
export const reloadServerConfiguration = (deployConfiguration) => get("configuration/reload", { deployConfiguration: !!deployConfiguration });
export const getGlobalRecoveryOptions = () => get("configuration/recoveryoptions");
export const setGlobalRecoveryOptions = (recoveryOptions) => post("configuration/recoveryoptions", recoveryOptions);

// Edges
export const getEdges = () => get("edges");
export const upsertEdge = (edgeName, credential) => post("edges", { Name: edgeName, Credential: credential });
export const removeEdge = (edgeName) => del(`edges/${enc(edgeName)}`);

// Edges - pending (connected but not yet authorized - see IPendingEdgeRegistry)
export const getPendingEdges = () => get("edges/pending");
export const approvePendingEdge = (instanceId, name) => post(`edges/pending/${enc(instanceId)}/approve`, { Name: name });
export const dismissPendingEdge = (instanceId) => del(`edges/pending/${enc(instanceId)}`);

// Security - IPs currently locked out by the brute-force limiter (see LoginAttemptLimiter)
export const getLockedOutIps = () => get("security/lockouts");
export const unlockIp = (ip) => del(`security/lockouts/${enc(ip)}`);

// Packages
export const getPackageInstances = (edgeName) => get(`edges/${enc(edgeName)}/packages`);
export const getPackageInstance = (edgeName, packageName) => get(`edges/${enc(edgeName)}/packages/${enc(packageName)}`);
export const upsertPackage = (edgeName, packageName, credential, packageFile, enable, autoStart) =>
    post(`edges/${enc(edgeName)}/packages`, { Name: packageName, Credential: credential, PackageFile: packageFile, Enable: enable, AutoStart: autoStart });
export const removePackage = (edgeName, packageName) => del(`edges/${enc(edgeName)}/packages/${enc(packageName)}`);
export const getPackageRecoveryOptions = (edgeName, packageName) => get(`edges/${enc(edgeName)}/packages/${enc(packageName)}/recoveryoptions`);
export const setPackageRecoveryOptions = (edgeName, packageName, recoveryOptions) =>
    post(`edges/${enc(edgeName)}/packages/${enc(packageName)}/recoveryoptions`, recoveryOptions);
export const getPackageGroups = (edgeName, packageName) => get(`edges/${enc(edgeName)}/packages/${enc(packageName)}/groups`);
export const setPackageGroups = (edgeName, packageName, groups) => post(`edges/${enc(edgeName)}/packages/${enc(packageName)}/groups`, groups);
export const getPackageSettings = (edgeName, packageName) => get(`edges/${enc(edgeName)}/packages/${enc(packageName)}/settings`);
export const setPackageSettings = (edgeName, packageName, settings, refreshPackage) =>
    post(`edges/${enc(edgeName)}/packages/${enc(packageName)}/settings`, { settings, refreshPackage: !!refreshPackage });

// Variables - a secret's Value is never included here; revealVariable() decrypts it on demand.
export const getVariables = () => get("variables");
export const revealVariable = (name) => get(`variables/${enc(name)}/reveal`);
export const upsertVariable = (name, value, isSecret) => post("variables", { Name: name, Value: value, IsSecret: !!isSecret });
export const removeVariable = (name) => del(`variables/${enc(name)}`);

// Scheduled tasks - fires a message on a cron schedule, see OrbitMesh.Server's ScheduledTaskRunner.
export const getScheduledTasks = () => get("scheduledtasks");
export const upsertScheduledTask = (task) => post("scheduledtasks", task);
export const removeScheduledTask = (name) => del(`scheduledtasks/${enc(name)}`);

// First-run bootstrap - unauthenticated (see AccessKeyAuthenticationMiddleware's /rest/management/setup
// bypass), called from Login.js before any credential exists, so both take serverUri directly instead
// of going through initializeClient()/urlBase (nothing to log in with yet).
export async function getSetupStatus(serverUri) {
    const response = await fetch(serverUri.replace(/\/+$/, "") + "/rest/management/setup/status");
    if (!response.ok) {
        throw new ManagementApiError(response.status, await response.text().catch(() => ""));
    }
    return response.json();
}
export async function createAdminAccount(serverUri, name, password) {
    const response = await fetch(serverUri.replace(/\/+$/, "") + "/rest/management/setup/create-admin", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ Name: name, Password: password })
    });
    if (!response.ok) {
        throw new ManagementApiError(response.status, await response.text().catch(() => ""));
    }
}

// Credentials
export const getCredentials = (includeAccessKeys) => get("credentials", { includeAccessKeys: !!includeAccessKeys });
// accessKey is a Machine credential's key, or - for a Human one - the password as typed (the server only
// ever stores a salted hash of it). "" means "leave the stored secret alone" (role/enable-only edit) - the
// server preserves the existing one in that case, see ManagementController.InsertOrUpdateCredential. scopes is an array of
// OrbitMeshScope strings (see Pages/Credentials.js's scope matrix).
export const upsertCredential = (name, accessKey, kind, enable, scopes, expiresUtc) =>
    post("credentials", { Name: name, AccessKey: accessKey || "", Kind: kind, Enable: enable, Scopes: scopes || [], ExpiresUtc: expiresUtc || null });
// An API token is made by the Server, which answers with the token itself - the only time it is ever visible:
// { Name, TokenId, Token, ExpiresUtc }. expiresUtc is an ISO date or null for "never".
export const createToken = (name, scopes, expiresUtc, enable) =>
    post("credentials/tokens", { Name: name, Scopes: scopes || [], ExpiresUtc: expiresUtc || null, Enable: enable !== false });
// Replaces the token's secret (and id) and answers with the new one, once; the old one stops working.
export const rotateToken = (name) => post(`credentials/${enc(name)}/rotate`);
export const removeCredential = (name) => del(`credentials/${enc(name)}`);
export const getCredentialAuthorizations = (name) => get(`credentials/${enc(name)}/authorizations`);
export const setCredentialAuthorizations = (name, authorizations) => post(`credentials/${enc(name)}/authorizations`, authorizations);

// Package Repository - local upload/download, plus browsing/installing from configured NuGet feeds.
export const getPackages = () => get("packages");
export const getPackage = (packageName) => get(`packages/${enc(packageName)}`);
export const renamePackageFile = (packageName, newName) => post(`packages/${enc(packageName)}`, { newName });
export const removePackageFile = (packageName) => del(`packages/${enc(packageName)}`);
export const downloadPackage = (url) => post("download-package", { url });
export const searchNuGetFeeds = (q) => get("packages/nuget/search", { q: q || "" });
export const getNuGetPackageVersions = (feed, id) => get(`packages/nuget/${enc(feed)}/${enc(id)}/versions`);
export const installNuGetPackage = (feed, id, version) => post("packages/nuget/install", { Feed: feed, Id: id, Version: version });
export const checkNuGetUpdates = () => get("packages/nuget/check-updates");
// multipart/form-data, not JSON - the request() helper always JSON-encodes, so this bypasses it.
export async function uploadPackage(file) {
    const formData = new FormData();
    formData.append("file", file);
    const response = await fetch(buildUrl("upload-package"), { method: "POST", headers: defaultHeaders, body: formData });
    if (!response.ok) {
        if ((response.status === 401 || response.status === 403) && unauthorizedHandler) {
            unauthorizedHandler();
        }
        throw new ManagementApiError(response.status, await response.text().catch(() => ""));
    }
}
export const getPackageIconUri = (packageName) => buildUrl(`packages/${enc(packageName)}/icon`, { ...defaultHeaders });
export const getSettingXsdSchema = (packageName, settingName) => get(`packages/${enc(packageName)}/settings/${enc(settingName)}/xsd`);

// Self-update
export const getUpdateStatus = () => get("update/status");
export const checkForUpdate = () => post("update/check");
export const applyUpdate = () => post("update/apply");
export const getStaticSiteUpdates = () => get("update/sites");
export const checkStaticSiteUpdates = () => post("update/sites/check");
export const applyStaticSiteUpdate = (slug) => post(`update/sites/${enc(slug)}/apply`);
