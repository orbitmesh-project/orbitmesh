namespace OrbitMesh.Server.Services;

/// <summary>
/// Works out which secret a request authenticates with. Machine callers present an AccessKey (header or
/// query string, as ever). A person signed in to the Console presents the session cookie instead, which is
/// turned into an internal pseudo key (<see cref="SessionKeyPrefix"/> + session id) that
/// <see cref="IOrbitMeshDirectory"/> resolves to a credential - so every existing authorization check keeps
/// taking "an access key" and works unchanged for both kinds of caller.
/// The pseudo key is only ever produced here from a validated cookie: a client-supplied AccessKey that
/// looks like one is discarded.
/// </summary>
public static class CallerKeys
{
    public const string SessionCookieName = "orbitmesh_session";

    // A control character can't appear in a header value, and no generated or typed AccessKey starts with one.
    public const string SessionKeyPrefix = "\u0001session:";

    private const string EffectiveKeyItem = "OrbitMesh.EffectiveAccessKey";

    public static bool IsSessionKey(string? key) => key != null && key.StartsWith(SessionKeyPrefix, StringComparison.Ordinal);

    public static string? SessionIdOf(string? key) => IsSessionKey(key) ? key![SessionKeyPrefix.Length..] : null;

    /// <summary>The key this request authenticates with: the presented AccessKey if there is one, otherwise the
    /// session cookie's pseudo key. <paramref name="presentedKey"/> tells whether an AccessKey was supplied at
    /// all - only those requests can be guesses worth counting against the lockout.</summary>
    public static string? FromRequest(HttpRequest request, SessionStore sessions, out bool presentedKey)
    {
        var presented = request.GetHeaderOrQuery(OrbitMeshHeaderNames.AccessKey);
        presentedKey = !string.IsNullOrEmpty(presented);
        if (presentedKey)
        {
            return IsSessionKey(presented) ? null : presented;
        }
        return FromSessionCookie(request, sessions);
    }

    public static string? FromSessionCookie(HttpRequest request, SessionStore sessions)
    {
        // The cookie is ambient: refuse it on a request a browser sends from another origin (cross-site
        // WebSocket hijacking, forged form posts). SameSite=Strict already stops most of those; this is the
        // second line, and the only one for same-site-but-different-origin pages.
        if (!IsSameOrigin(request) || !request.Cookies.TryGetValue(SessionCookieName, out var token))
        {
            return null;
        }
        var session = sessions.TryGetByToken(token);
        return session == null ? null : SessionKeyPrefix + session.Id;
    }

    /// <summary>True when the request carries no Origin header (not a browser cross-origin request) or the Origin
    /// is this very host. The scheme is ignored on purpose: behind a TLS-terminating proxy that isn't listed in
    /// TrustedProxies the Server sees "http" while the browser says "https".</summary>
    public static bool IsSameOrigin(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Origin", out var origin) || string.IsNullOrEmpty(origin))
        {
            return true;
        }
        return Uri.TryCreate(origin.ToString(), UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Stores the key a request authenticated with, for the controllers and filters further down the pipeline.</summary>
    public static void SetEffectiveAccessKey(this HttpContext context, string? key) => context.Items[EffectiveKeyItem] = key;

    public static string? GetEffectiveAccessKey(this HttpContext context) =>
        context.Items.TryGetValue(EffectiveKeyItem, out var key) ? key as string : null;

    public static void SetSessionCookie(this HttpResponse response, string token, TimeSpan lifetime) =>
        response.Cookies.Append(SessionCookieName, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = response.HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            Expires = DateTimeOffset.UtcNow + lifetime,
            IsEssential = true
        });

    public static void ClearSessionCookie(this HttpResponse response) =>
        response.Cookies.Delete(SessionCookieName, new CookieOptions
        {
            Secure = response.HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/"
        });
}
