// Sign-in is the server's business: it hands the browser an HttpOnly session cookie that this script can
// neither read nor forge, so there is nothing to store, renew or compute here - this module only calls
// /rest/auth. (It used to keep a username/AccessKey cookie, with the AccessKey derived client-side from the
// password, and re-send that key with every request.)

function authUrl(serverUri, path) {
    return serverUri.replace(/\/+$/, "") + "/rest/auth/" + path;
}

async function failure(response, fallback) {
    const text = await response.text().catch(() => "");
    return new Error(text && !text.startsWith("{") ? text : fallback);
}

// What the sign-in page should offer: the password form, and/or single sign-on through the identity
// provider the server is configured with (see OrbitMesh:Auth in appsettings.json).
export async function getAuthConfig(serverUri) {
    const response = await fetch(authUrl(serverUri, "config"));
    if (!response.ok) {
        throw await failure(response, `Couldn't read the sign-in options (HTTP ${response.status})`);
    }
    return response.json();
}

export async function login(serverUri, username, password) {
    const response = await fetch(authUrl(serverUri, "login"), {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ Name: username, Password: password })
    });
    if (!response.ok) {
        throw await failure(response, response.status === 429 ? "Too many failed attempts - try again later." : "Invalid credentials.");
    }
    return response.json();
}

export async function logout(serverUri) {
    try {
        await fetch(authUrl(serverUri, "logout"), { method: "POST", headers: { EdgeName: "Console" } });
    } catch {
        // The cookie is dropped server-side on its own once it idles out; nothing more to do from here.
    }
}

// The signed-in session ({ Name, Scopes, ExpiresUtc }), or null if there isn't a valid one. Calling it also
// counts as activity, which is what keeps an open Console from idling out.
export async function fetchSession(serverUri) {
    try {
        const response = await fetch(authUrl(serverUri, "me"));
        return response.ok ? await response.json() : null;
    } catch {
        // Server unreachable: not the same as "signed out" - the SignalR reconnect logic deals with that.
        return undefined;
    }
}

// Where "Sign in with ..." sends the browser: the server redirects to the identity provider and, after
// the user authenticated there, back to returnUrl (or to errorUrl with ?login_error=... if refused).
export function oidcLoginUrl(serverUri, returnUrl, errorUrl) {
    const params = new URLSearchParams({ returnUrl, errorUrl });
    return authUrl(serverUri, "oidc/login") + "?" + params.toString();
}

export function getParentUri() {
    return window.location.origin;
}
