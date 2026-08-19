using NuGet.Common;
using NuGetLogLevel = NuGet.Common.LogLevel;

namespace OrbitMesh.Server.Services;

/// <summary>Bridges NuGet.Protocol's own <see cref="NuGet.Common.ILogger"/> (a distinct interface
/// from ASP.NET Core's) into the app's normal logging pipeline, so feed activity shows up in NLog
/// like everything else instead of going nowhere.</summary>
internal sealed class NuGetLoggerAdapter(Microsoft.Extensions.Logging.ILogger logger) : NuGet.Common.ILogger
{
    public void LogDebug(string data) => Log(NuGetLogLevel.Debug, data);

    public void LogVerbose(string data) => Log(NuGetLogLevel.Verbose, data);

    public void LogInformation(string data) => Log(NuGetLogLevel.Information, data);

    public void LogMinimal(string data) => Log(NuGetLogLevel.Minimal, data);

    public void LogWarning(string data) => Log(NuGetLogLevel.Warning, data);

    public void LogError(string data) => Log(NuGetLogLevel.Error, data);

    public void LogInformationSummary(string data) => Log(NuGetLogLevel.Information, data);

    public void Log(NuGetLogLevel level, string data) => logger.Log(ToMicrosoftLevel(level), "{Message}", data);

    public Task LogAsync(NuGetLogLevel level, string data)
    {
        Log(level, data);
        return Task.CompletedTask;
    }

    public void Log(ILogMessage message) => Log(message.Level, message.Message);

    public Task LogAsync(ILogMessage message)
    {
        Log(message);
        return Task.CompletedTask;
    }

    private static Microsoft.Extensions.Logging.LogLevel ToMicrosoftLevel(NuGetLogLevel level) => level switch
    {
        NuGetLogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
        NuGetLogLevel.Verbose => Microsoft.Extensions.Logging.LogLevel.Trace,
        NuGetLogLevel.Information => Microsoft.Extensions.Logging.LogLevel.Information,
        NuGetLogLevel.Minimal => Microsoft.Extensions.Logging.LogLevel.Information,
        NuGetLogLevel.Warning => Microsoft.Extensions.Logging.LogLevel.Warning,
        NuGetLogLevel.Error => Microsoft.Extensions.Logging.LogLevel.Error,
        _ => Microsoft.Extensions.Logging.LogLevel.Information
    };
}
