using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace OrbitMesh.Edge.Utils;

/// <summary>
/// CLI control channel (<c>edge start MyPackage</c>). <see cref="NamedPipeServerStream"/> works
/// cross-platform in modern .NET (backed by Unix domain sockets on Linux/macOS), so unlike the original
/// this is no longer Windows-only.
/// </summary>
public static class NamedPipeHelper
{
    private static readonly ConcurrentDictionary<string, byte> ActiveServers = new(StringComparer.OrdinalIgnoreCase);

    public static void StartServer(string pipeName, Action<string> onMessage, Action<Exception>? onError, CancellationToken cancellationToken)
    {
        if (!ActiveServers.TryAdd(pipeName, 0))
        {
            return;
        }

        _ = Task.Factory.StartNew(async () =>
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        await using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                        await server.WaitForConnectionAsync(cancellationToken);
                        using var reader = new StreamReader(server, Encoding.UTF8);
                        string? line;
                        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
                        {
                            onMessage(line);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (IOException ex) when (IsPipeAlreadyInUse(ex))
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        onError?.Invoke(ex);
                    }
                }
            }
            finally
            {
                ActiveServers.TryRemove(pipeName, out _);
            }
        }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public static bool TrySendMessage(string pipeName, string message, Action<Exception>? onError = null, int timeoutMs = 1000)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(timeoutMs);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(message);
            return true;
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            return false;
        }
    }

    private static bool IsPipeAlreadyInUse(IOException ex) =>
        ex.Message.Contains("All pipe instances are busy", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase);

    public static string GetCurrentProcessPipeName()
    {
        var path = Process.GetCurrentProcess().MainModule?.FileName?.ToLowerInvariant() ?? "orbitmesh.edge";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(path));
        return Convert.ToHexStringLower(hash);
    }
}
