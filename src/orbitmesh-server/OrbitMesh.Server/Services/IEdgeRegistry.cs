using OrbitMesh.Server.Models;

namespace OrbitMesh.Server.Services;

/// <summary>
/// Tracks connected Edges. Replaces the static dictionaries the original EdgeHub kept in-process;
/// ASP.NET Core SignalR hubs are instantiated per-call so shared state must live in a singleton service.
/// </summary>
public interface IEdgeRegistry
{
    IReadOnlyDictionary<string, EdgeInfo> Edges { get; }

    string? GetEdgeName(string connectionId);

    void Register(string connectionId, string edgeName, EdgeInfo info);

    void Disconnect(string connectionId);

    bool IsConnected(string edgeName);
}

public sealed class EdgeRegistry : IEdgeRegistry
{
    private readonly Dictionary<string, string> _edgeNameByConnection = new();
    private readonly Dictionary<string, EdgeInfo> _edges = new();
    private readonly object _lock = new();

    public IReadOnlyDictionary<string, EdgeInfo> Edges
    {
        get
        {
            lock (_lock)
            {
                return new Dictionary<string, EdgeInfo>(_edges);
            }
        }
    }

    public string? GetEdgeName(string connectionId)
    {
        lock (_lock)
        {
            return _edgeNameByConnection.GetValueOrDefault(connectionId);
        }
    }

    public void Register(string connectionId, string edgeName, EdgeInfo info)
    {
        lock (_lock)
        {
            _edgeNameByConnection[connectionId] = edgeName;
            _edges[edgeName] = info;
        }
    }

    public void Disconnect(string connectionId)
    {
        lock (_lock)
        {
            if (_edgeNameByConnection.Remove(connectionId, out var edgeName) && _edges.TryGetValue(edgeName, out var info))
            {
                info.IsConnected = false;
            }
        }
    }

    public bool IsConnected(string edgeName)
    {
        lock (_lock)
        {
            return _edges.TryGetValue(edgeName, out var info) && info.IsConnected;
        }
    }
}
