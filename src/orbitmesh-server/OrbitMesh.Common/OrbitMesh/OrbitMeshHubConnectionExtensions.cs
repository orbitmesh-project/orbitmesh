using System.Text.Json.Serialization;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace OrbitMesh;

/// <summary>
/// Reconnect backoff and JSON wire-format settings shared by every OrbitMesh client that connects
/// to a hub (the PackageHost SDK and OrbitMesh.Edge) - kept in one place so both stay in sync
/// with the server's own JSON configuration (see OrbitMesh.Server/Program.cs).
/// </summary>
public static class OrbitMeshHubConnectionExtensions
{
    /// <summary>Applies the standard reconnect backoff and JSON wire-format settings every OrbitMesh
    /// hub connection uses.</summary>
    public static IHubConnectionBuilder WithOrbitMeshDefaults(this IHubConnectionBuilder builder) =>
        builder
            .WithAutomaticReconnect(new IndefiniteRetryPolicy())
            .AddJsonProtocol(o =>
            {
                o.PayloadSerializerOptions.PropertyNamingPolicy = null;
                o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter());
            });
}

/// <summary>Same 0/3/3/5/10s backoff, then retries every 10s forever instead of giving up (~21s is
/// shorter than a typical Server reboot).</summary>
internal sealed class IndefiniteRetryPolicy : IRetryPolicy
{
    private static readonly TimeSpan[] InitialDelays =
        [TimeSpan.Zero, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
        retryContext.PreviousRetryCount < InitialDelays.Length
            ? InitialDelays[retryContext.PreviousRetryCount]
            : TimeSpan.FromSeconds(10);
}
