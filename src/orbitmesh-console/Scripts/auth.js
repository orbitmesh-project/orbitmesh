// Cookie/AccessKey session handling. The one server call (checkControllerAccess) uses fetch.

const AUTH_COOKIE_NAMES = { username: "username", accessKey: "accessKey", lifetime: "lifetime" };

function getCookie(name) {
    const match = document.cookie.split("; ").find((row) => row.startsWith(name + "="));
    return match ? decodeURIComponent(match.split("=").slice(1).join("=")) : "";
}

function setCookie(name, value, expirationSeconds) {
    let expires = "Thu, 01 Jan 1970 00:00:00 UTC";
    if (expirationSeconds > 0) {
        const date = new Date();
        date.setTime(date.getTime() + expirationSeconds * 1000);
        expires = date.toUTCString();
    }
    document.cookie = `${name}=${encodeURIComponent(value)}; expires=${expires}; path=/`;
}

function getParameterByName(name) {
    return new URLSearchParams(window.location.search).get(name);
}

export function getUsername() {
    return getCookie(AUTH_COOKIE_NAMES.username);
}

export function getAccessKey() {
    return getCookie(AUTH_COOKIE_NAMES.accessKey);
}

export function isLoggedIn() {
    return getUsername() !== "" && getAccessKey() !== "";
}

// Renews the cookie's own expiration on every "still connected" tick, so a session in active use
// never silently expires mid-session.
export function renewCookie() {
    const username = getUsername();
    if (username === "") {
        return;
    }
    const lifetime = parseInt(getCookie(AUTH_COOKIE_NAMES.lifetime), 10) || 0;
    setCookie(AUTH_COOKIE_NAMES.username, username, lifetime);
    setCookie(AUTH_COOKIE_NAMES.accessKey, getAccessKey(), lifetime);
    setCookie(AUTH_COOKIE_NAMES.lifetime, String(lifetime), lifetime);
}

export function logout() {
    setCookie(AUTH_COOKIE_NAMES.username, "", 0);
    setCookie(AUTH_COOKIE_NAMES.accessKey, "", 0);
    setCookie(AUTH_COOKIE_NAMES.lifetime, "", 0);
}

// SHA-1 of username+password, matching the server's credential.AccessKey convention - native
// SubtleCrypto. Exported so the Credentials page can offer a "Login/Password" option (computing
// the AccessKey automatically) alongside entering a raw AccessKey directly.
export async function computeAccessKey(username, password) {
    const data = new TextEncoder().encode(username + password);
    const digest = await crypto.subtle.digest("SHA-1", data);
    return Array.from(new Uint8Array(digest)).map((b) => b.toString(16).padStart(2, "0")).join("");
}

export async function login(username, password, expirationMinutes) {
    const accessKey = await computeAccessKey(username, password);
    loginWithAccessKey(username, accessKey, expirationMinutes);
}

export function loginWithAccessKey(username, accessKey, expirationMinutes) {
    const seconds = expirationMinutes * 60;
    setCookie(AUTH_COOKIE_NAMES.username, username, seconds);
    setCookie(AUTH_COOKIE_NAMES.accessKey, accessKey, seconds);
    setCookie(AUTH_COOKIE_NAMES.lifetime, String(seconds), seconds);
}

// Refreshes just the accessKey cookie (keeping the existing username/lifetime) - for when a signed-in
// admin resets their OWN credential's password (see Credentials.js): the server now only accepts the
// new hash, so the old cookie would otherwise 403 on this admin's very next request.
export function updateAccessKeyCookie(accessKey) {
    const lifetime = parseInt(getCookie(AUTH_COOKIE_NAMES.lifetime), 10) || 0;
    setCookie(AUTH_COOKIE_NAMES.accessKey, accessKey, lifetime);
}

export async function checkControllerAccess(serverUri, username, accessKey) {
    const response = await fetch(`${serverUri}/rest/controller/CheckAccess`, {
        headers: { EdgeName: "Controller", PackageName: "ControlCenter:" + username, AccessKey: accessKey }
    });
    return response.ok;
}

export function getParentUri() {
    return window.location.origin;
}
