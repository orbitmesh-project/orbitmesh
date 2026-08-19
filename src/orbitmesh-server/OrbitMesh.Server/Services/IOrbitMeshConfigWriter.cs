using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using OrbitMesh.Server.Configuration;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Persists changes back to the "OrbitMesh" section of appsettings.json. Because the file is loaded
/// with <c>reloadOnChange: true</c>, writing it triggers ASP.NET Core's own file watcher and
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> picks up the change automatically -
/// no manual FileSystemWatcher/ConfigurationManager.RefreshSection dance needed.
/// </summary>
public interface IOrbitMeshConfigWriter
{
    string ReadRawJson();

    void WriteRawJson(string json);

    void Update(Action<OrbitMeshOptions> mutate);

    /// <summary>Parses raw JSON for the "OrbitMesh" section the same way WriteRawJson will bind
    /// it, without writing anything - lets a caller (e.g. the Management API) reject a bad edit
    /// before it ever reaches disk. Throws if the JSON is malformed or doesn't fit the expected
    /// shape.</summary>
    OrbitMeshOptions Deserialize(string json);
}

public sealed class OrbitMeshConfigWriter(IHostEnvironment environment) : IOrbitMeshConfigWriter
{
    private readonly string _path = Path.Combine(environment.ContentRootPath, "appsettings.json");
    private readonly object _lock = new();
    // camelCase (JsonSerializerDefaults.Web) to match this project's appsettings.json convention -
    // deliberately different from the PascalCase used on the wire to the console (Program.cs), which
    // is a separate JSON dialect for a separate audience. Enums still need JsonStringEnumConverter
    // here too, though: without it, AuthorizationType/ScopeType round-trip as bare numbers (e.g.
    // "defaultAuthorization": 0) instead of the readable names used everywhere else in this file.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ReadRawJson()
    {
        lock (_lock)
        {
            var root = JsonNode.Parse(File.ReadAllText(_path))!;
            return (root[OrbitMeshOptions.SectionName] ?? new JsonObject()).ToJsonString(SerializerOptions);
        }
    }

    public void WriteRawJson(string json)
    {
        lock (_lock)
        {
            var section = JsonNode.Parse(json) ?? throw new ArgumentException("Invalid JSON", nameof(json));
            var root = JsonNode.Parse(File.ReadAllText(_path))!;
            root[OrbitMeshOptions.SectionName] = section;
            BackupBeforeWrite();
            File.WriteAllText(_path, root.ToJsonString(SerializerOptions));
        }
    }

    public OrbitMeshOptions Deserialize(string json)
    {
        var section = JsonNode.Parse(json) ?? throw new ArgumentException("Invalid JSON", nameof(json));
        return section.Deserialize<OrbitMeshOptions>(SerializerOptions)
            ?? throw new ArgumentException("Configuration deserialized to null", nameof(json));
    }

    public void Update(Action<OrbitMeshOptions> mutate)
    {
        lock (_lock)
        {
            var root = JsonNode.Parse(File.ReadAllText(_path))!;
            var sectionNode = root[OrbitMeshOptions.SectionName];
            var current = sectionNode != null
                ? sectionNode.Deserialize<OrbitMeshOptions>(SerializerOptions) ?? new OrbitMeshOptions()
                : new OrbitMeshOptions();

            mutate(current);

            root[OrbitMeshOptions.SectionName] = JsonSerializer.SerializeToNode(current, SerializerOptions);
            BackupBeforeWrite();
            File.WriteAllText(_path, root.ToJsonString(SerializerOptions));
        }
    }

    // A single rolling backup of the last-known-good file, taken right before each write. This is the
    // only copy of every edge/package/credential in the system - a crash mid-write or a bad raw-JSON
    // PUT via SetServerConfiguration would otherwise leave no way back to a working file.
    private void BackupBeforeWrite()
    {
        if (File.Exists(_path))
        {
            File.Copy(_path, _path + ".bak", overwrite: true);
        }
    }
}
