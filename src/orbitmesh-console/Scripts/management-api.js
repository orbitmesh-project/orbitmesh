// getLicense/setLicense are dropped on purpose - ManagementController's license endpoints are a
// confirmed dead stub, never wired up to anything real.

let urlBase = null;
let defaultHeaders = {};

export function initializeClient(serverUri, accessKey, friendlyName) {
    urlBase = serverUri.replace(/\/+$/, "") + "/rest/management/";
    defaultHeaders = { EdgeName: "Management", PackageName: friendlyName, AccessKey: accessKey };
}

// Fired whenever a management API call comes back 401/403 - the AccessKey cookie has expired or been
// revoked server-side. The console shell (app.js) uses this to force a logout and bounce to /login
// instead of leaving the user stranded on a page that silently can't load or save anything.
let unauthorizedHandler = null;
export function setUnauthorizedHandler(handler) {
    unauthorizedHandler = handler;
}

// For when the signed-in admin resets their OWN credential's password (see Credentials.js) - every
// REST call here resends this AccessKey, so it has to be refreshed in place or every request after the
// reset 403s until the page is reloaded.
export function updateAccessKey(accessKey) {
    defaultHeaders = { ...defaultHeaders, AccessKey: accessKey };
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
export const removeEdge = (edgeName) => del("edges/" + edgeName);

// Edges - pending (connected but not yet authorized - see IPendingEdgeRegistry)
export const getPendingEdges = () => get("edges/pending");
export const approvePendingEdge = (instanceId, name) => post(`edges/pending/${instanceId}/approve`, { Name: name });
export const dismissPendingEdge = (instanceId) => del(`edges/pending/${instanceId}`);

// Security - IPs currently locked out by the brute-force limiter (see LoginAttemptLimiter)
export const getLockedOutIps = () => get("security/lockouts");
export const unlockIp = (ip) => del(`security/lockouts/${ip}`);

// Packages
export const getPackageInstances = (edgeName) => get(`edges/${edgeName}/packages`);
export const getPackageInstance = (edgeName, packageName) => get(`edges/${edgeName}/packages/${packageName}`);
export const upsertPackage = (edgeName, packageName, credential, packageFile, enable, autoStart) =>
    post(`edges/${edgeName}/packages`, { Name: packageName, Credential: credential, PackageFile: packageFile, Enable: enable, AutoStart: autoStart });
export const removePackage = (edgeName, packageName) => del(`edges/${edgeName}/packages/${packageName}`);
export const getPackageRecoveryOptions = (edgeName, packageName) => get(`edges/${edgeName}/packages/${packageName}/recoveryoptions`);
export const setPackageRecoveryOptions = (edgeName, packageName, recoveryOptions) =>
    post(`edges/${edgeName}/packages/${packageName}/recoveryoptions`, recoveryOptions);
export const getPackageGroups = (edgeName, packageName) => get(`edges/${edgeName}/packages/${packageName}/groups`);
export const setPackageGroups = (edgeName, packageName, groups) => post(`edges/${edgeName}/packages/${packageName}/groups`, groups);
export const getPackageSettings = (edgeName, packageName) => get(`edges/${edgeName}/packages/${packageName}/settings`);
export const setPackageSettings = (edgeName, packageName, settings, refreshPackage) =>
    post(`edges/${edgeName}/packages/${packageName}/settings`, { settings, refreshPackage: !!refreshPackage });

// Variables - a secret's Value is never included here; revealVariable() decrypts it on demand.
export const getVariables = () => get("variables");
export const revealVariable = (name) => get(`variables/${name}/reveal`);
export const upsertVariable = (name, value, isSecret) => post("variables", { Name: name, Value: value, IsSecret: !!isSecret });
export const removeVariable = (name) => del("variables/" + name);

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
export async function createAdminAccount(serverUri, name, accessKey) {
    const response = await fetch(serverUri.replace(/\/+$/, "") + "/rest/management/setup/create-admin", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ Name: name, AccessKey: accessKey })
    });
    if (!response.ok) {
        throw new ManagementApiError(response.status, await response.text().catch(() => ""));
    }
}

// Credentials
export const getCredentials = (includeAccessKeys) => get("credentials", { includeAccessKeys: !!includeAccessKeys });
// accessKey: "" means "leave the stored key alone" (role/enable-only edit) - the server preserves the
// existing key in that case, see ManagementController.InsertOrUpdateCredential. scopes is an array of
// OrbitMeshScope strings (see Pages/Credentials.js's scope matrix).
export const upsertCredential = (name, accessKey, kind, enable, scopes) =>
    post("credentials", { Name: name, AccessKey: accessKey || "", Kind: kind, Enable: enable, Scopes: scopes || [] });
export const removeCredential = (name) => del("credentials/" + name);
export const getCredentialAuthorizations = (name) => get(`credentials/${name}/authorizations`);
export const setCredentialAuthorizations = (name, authorizations) => post(`credentials/${name}/authorizations`, authorizations);

// Package Repository - local upload/download, plus browsing/installing from configured NuGet feeds.
export const getPackages = () => get("packages");
export const getPackage = (packageName) => get("packages/" + packageName);
export const renamePackageFile = (packageName, newName) => post("packages/" + packageName, { newName });
export const removePackageFile = (packageName) => del("packages/" + packageName);
export const downloadPackage = (url) => post("download-package", { url });
export const searchNuGetFeeds = (q) => get("packages/nuget/search", { q: q || "" });
export const getNuGetPackageVersions = (feed, id) => get(`packages/nuget/${feed}/${id}/versions`);
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
export const getPackageIconUri = (packageName) => buildUrl(`packages/${packageName}/icon`, { ...defaultHeaders });
export const getSettingXsdSchema = (packageName, settingName) => get(`packages/${packageName}/settings/${settingName}/xsd`);

// Self-update
export const getUpdateStatus = () => get("update/status");
export const checkForUpdate = () => post("update/check");
export const applyUpdate = () => post("update/apply");
export const getStaticSiteUpdates = () => get("update/sites");
export const checkStaticSiteUpdates = () => post("update/sites/check");
export const applyStaticSiteUpdate = (slug) => post(`update/sites/${slug}/apply`);
