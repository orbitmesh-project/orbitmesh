using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using OrbitMesh.Server.Configuration;

namespace OrbitMesh.Server.Services;

public sealed class SessionInfo
{
    /// <summary>SHA-256 (hex) of the cookie token - the store never holds the token itself.</summary>
    public required string Id { get; init; }

    /// <summary>The Human credential's name, or "oidc:&lt;login&gt;" for a single-sign-on user.</summary>
    public required string Name { get; init; }

    public bool IsFederated { get; init; }

    /// <summary>The identity provider's groups at sign-in; scopes are derived from them live (see OidcRoleMapper).</summary>
    public List<string> Groups { get; init; } = [];

    public DateTime CreatedUtc { get; init; }

    public DateTime LastSeenUtc { get; set; }

    public DateTime AbsoluteExpiryUtc { get; init; }

    public string? RemoteIp { get; init; }
}

/// <summary>
/// Server-side sessions for people signing in to the Console (password or single sign-on). The browser
/// holds one random opaque token in an HttpOnly cookie; this store keeps only its SHA-256, so a leaked
/// sessions file can't be replayed. Unlike the AccessKey it replaces, a session expires on its own,
/// is revoked by sign-out or a password change, and never travels in a URL.
/// Sessions are persisted (keys/sessions.json) so a Server restart - e.g. a self-update - doesn't sign
/// everybody out.
/// </summary>
public sealed class SessionStore
{
    // LastSeenUtc is only persisted when something else changes or on the periodic flush; losing a few
    // minutes of sliding window across a crash is harmless.
    private static readonly TimeSpan TouchGranularity = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, SessionInfo> sessions = new();
    private readonly IOptionsMonitor<OrbitMeshOptions> options;
    private readonly ILogger<SessionStore> logger;
    private readonly string path;
    private readonly object fileLock = new();
    private volatile bool dirty;

    public SessionStore(IHostEnvironment environment, IOptionsMonitor<OrbitMeshOptions> options, ILogger<SessionStore> logger)
    {
        this.options = options;
        this.logger = logger;
        path = Path.Combine(environment.ContentRootPath, "keys", "sessions.json");
        Load();
    }

    private TimeSpan IdleTimeout => TimeSpan.FromMinutes(Math.Max(1, options.CurrentValue.Auth.SessionIdleMinutes));

    private TimeSpan MaxLifetime => TimeSpan.FromHours(Math.Max(1, options.CurrentValue.Auth.SessionMaxHours));

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Creates a session and returns the raw cookie token (shown to the browser once, never stored).</summary>
    public (string Token, SessionInfo Session) Create(string name, bool isFederated, IEnumerable<string>? groups, string? remoteIp)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var now = DateTime.UtcNow;
        var session = new SessionInfo
        {
            Id = HashToken(token),
            Name = name,
            IsFederated = isFederated,
            Groups = groups?.ToList() ?? [],
            CreatedUtc = now,
            LastSeenUtc = now,
            AbsoluteExpiryUtc = now + MaxLifetime,
            RemoteIp = remoteIp
        };
        sessions[session.Id] = session;
        dirty = true;
        Flush();
        return (token, session);
    }

    public SessionInfo? TryGetByToken(string? token, bool touch = true) =>
        string.IsNullOrEmpty(token) ? null : TryGetById(HashToken(token), touch);

    public SessionInfo? TryGetById(string? id, bool touch = true)
    {
        if (string.IsNullOrEmpty(id) || !sessions.TryGetValue(id, out var session))
        {
            return null;
        }
        var now = DateTime.UtcNow;
        if (IsExpired(session, now))
        {
            if (sessions.TryRemove(id, out _))
            {
                dirty = true;
            }
            return null;
        }
        if (touch && now - session.LastSeenUtc > TouchGranularity)
        {
            session.LastSeenUtc = now;
            dirty = true;
        }
        return session;
    }

    /// <summary>When this session stops being valid if left idle (never beyond its absolute expiry).</summary>
    public DateTime ExpiresAtUtc(SessionInfo session)
    {
        var idle = session.LastSeenUtc + IdleTimeout;
        return idle < session.AbsoluteExpiryUtc ? idle : session.AbsoluteExpiryUtc;
    }

    public void Revoke(string? token)
    {
        if (!string.IsNullOrEmpty(token) && sessions.TryRemove(HashToken(token), out _))
        {
            dirty = true;
            Flush();
        }
    }

    /// <summary>Ends every session of one account, optionally sparing the caller's own (a password change must
    /// sign out the other browsers, not the one doing the change).</summary>
    public int RevokeAllFor(string name, string? exceptId = null)
    {
        var count = 0;
        foreach (var session in sessions.Values.Where(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && s.Id != exceptId))
        {
            if (sessions.TryRemove(session.Id, out _))
            {
                count++;
            }
        }
        if (count > 0)
        {
            dirty = true;
            Flush();
        }
        return count;
    }

    /// <summary>Drops expired sessions and writes the file if anything changed - called periodically.</summary>
    public void Maintain()
    {
        var now = DateTime.UtcNow;
        foreach (var session in sessions.Values.Where(s => IsExpired(s, now)))
        {
            if (sessions.TryRemove(session.Id, out _))
            {
                dirty = true;
            }
        }
        Flush();
    }

    private bool IsExpired(SessionInfo session, DateTime now) =>
        now >= session.AbsoluteExpiryUtc || now - session.LastSeenUtc > IdleTimeout;

    private void Flush()
    {
        if (!dirty)
        {
            return;
        }
        lock (fileLock)
        {
            dirty = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(sessions.Values.ToList()));
                if (!OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                }
                File.Move(tmp, path, overwrite: true);
            }
            catch (Exception ex)
            {
                dirty = true;
                logger.LogWarning(ex, "Couldn't persist sessions to {Path} - they stay valid until the Server restarts.", path);
            }
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }
            var stored = JsonSerializer.Deserialize<List<SessionInfo>>(File.ReadAllText(path)) ?? [];
            var now = DateTime.UtcNow;
            foreach (var session in stored.Where(s => !IsExpired(s, now)))
            {
                sessions[session.Id] = session;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't read {Path} - starting with no sessions (everyone signs in again).", path);
        }
    }
}

public sealed class SessionMaintenanceService(SessionStore store) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                store.Maintain();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
