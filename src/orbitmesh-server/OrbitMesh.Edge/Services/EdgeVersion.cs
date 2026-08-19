using System.Reflection;

namespace OrbitMesh.Edge.Services;

/// <summary>The Edge's own version, as set via &lt;Version&gt; in the csproj - reported to the update
/// server as the "from" version. Mirrors OrbitMesh.Server.Services.ServerVersion; see its own doc
/// comment for why AssemblyInformationalVersionAttribute is used instead of Assembly.GetName().Version.</summary>
public static class EdgeVersion
{
    public static string Current { get; } =
        typeof(EdgeVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(EdgeVersion).Assembly.GetName().Version?.ToString()
        ?? "1.0.0";
}
