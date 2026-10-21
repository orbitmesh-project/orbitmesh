using OrbitMesh.Server.Services;
using Microsoft.AspNetCore.SignalR;

namespace OrbitMesh.Server.Hubs;

/// <summary>Applies <see cref="LoginAttemptLimiter"/> to SignalR hub connections. The REST middleware
/// enforces it for HTTP calls, but hub negotiation is exempt from that middleware (each hub
/// authenticates in OnConnectedAsync with its own access type), so without this a hub endpoint could be
/// guessed at without limit - and every unauthenticated connect also costs one PBKDF2 per Human credential.</summary>
internal static class HubAttemptGuard
{
    public static bool IsLockedOut(this HubCallerContext context, LoginAttemptLimiter limiter) =>
        limiter.IsLockedOut(context.GetHttpContext()?.Connection.RemoteIpAddress);

    public static void RecordFailure(this HubCallerContext context, LoginAttemptLimiter limiter, ILogger logger)
    {
        var ip = context.GetHttpContext()?.Connection.RemoteIpAddress;
        if (limiter.RecordFailure(ip))
        {
            logger.LogWarning("Locking out {RemoteIp} for repeated failed AccessKey attempts.", ip);
        }
    }
}
