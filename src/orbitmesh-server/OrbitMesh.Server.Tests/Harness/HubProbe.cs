using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace OrbitMesh.Server.Tests.Harness;

/// <summary>Opens a SignalR connection to a hub the way a client would. A hub decides in OnConnectedAsync and aborts a
/// rejected connection right after the handshake: "accepted" means the connection is still open a moment later,
/// "rejected" means the Server closed it.</summary>
public static class HubProbe
{
    private static readonly TimeSpan AcceptedWindow = TimeSpan.FromMilliseconds(900);

    public static async Task<bool> ConnectsAsync(ServerFixture server, string hub, Caller caller, string? ip = null)
    {
        ip ??= server.CreateApi().Ip;
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.Server.BaseAddress, $"signalr/hubs/{hub}"), options =>
            {
                options.HttpMessageHandlerFactory = _ => server.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.Headers["X-Test-Remote-Ip"] = ip;
                if (caller.EdgeName != null) { options.Headers["EdgeName"] = caller.EdgeName; }
                if (caller.PackageName != null) { options.Headers["PackageName"] = caller.PackageName; }
                if (caller.AccessKey != null) { options.Headers["AccessKey"] = caller.AccessKey; }
                if (caller.SessionToken != null) { options.Headers["Cookie"] = $"{Api.SessionCookie}={caller.SessionToken}"; }
            })
            .Build();
        connection.Closed += _ =>
        {
            closed.TrySetResult();
            return Task.CompletedTask;
        };

        try
        {
            await connection.StartAsync();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // Not a refusal: the hub route itself is wrong, and every "rejected" assertion would pass for nothing.
            throw new InvalidOperationException($"The hub route for {hub} does not exist.", ex);
        }
        catch (Exception)
        {
            return false;
        }

        return await Task.WhenAny(closed.Task, Task.Delay(AcceptedWindow)) != closed.Task;
    }
}
