using System.Reflection;

namespace OrbitMesh.Server.Services;

/// <summary>The Server's own version, as set via &lt;Version&gt; in the csproj - reported to the
/// console and sent to the update server as the "from" version. Assembly.GetName().Version isn't
/// used here because MSBuild pads a 3-part &lt;Version&gt; like "1.0.5" out to a 4-part AssemblyVersion
/// ("1.0.5.0"), which then travels to the update server as an ugly, not-quite-semver "from" value -
/// AssemblyInformationalVersion preserves the exact string from the csproj instead.</summary>
public static class ServerVersion
{
    public static string Current { get; } =
        typeof(ServerVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(ServerVersion).Assembly.GetName().Version?.ToString()
        ?? "1.0.0";
}
