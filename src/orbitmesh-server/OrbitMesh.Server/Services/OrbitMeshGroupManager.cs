namespace OrbitMesh.Server.Services;

/// <summary>Tracks which SignalR connections belong to which broadcast groups (messages/telemetry subscriptions).</summary>
public class OrbitMeshGroupManager
{
    // Plain Dictionary, not ConcurrentDictionary: every access below already runs inside the exclusive
    // _lock, so a concurrent collection here would just be redundant internal locking on top of ours.
    private readonly Dictionary<string, HashSet<string>> _groupsByConnection = new();
    private readonly Dictionary<string, HashSet<string>> _connectionsByGroup = new();
    private readonly object _lock = new();

    public void Add(string connectionId, string group)
    {
        lock (_lock)
        {
            GetOrAddSet(_groupsByConnection, connectionId).Add(group);
            GetOrAddSet(_connectionsByGroup, group).Add(connectionId);
        }
    }

    public void Remove(string connectionId, string group)
    {
        lock (_lock)
        {
            if (_groupsByConnection.TryGetValue(connectionId, out var groups))
            {
                groups.Remove(group);
            }
            if (_connectionsByGroup.TryGetValue(group, out var connections))
            {
                connections.Remove(connectionId);
            }
        }
    }

    /// <summary>Removes a connection from every group it belongs to (called on disconnect).</summary>
    public void Remove(string connectionId)
    {
        lock (_lock)
        {
            if (_groupsByConnection.Remove(connectionId, out var groups))
            {
                foreach (var group in groups)
                {
                    if (_connectionsByGroup.TryGetValue(group, out var connections))
                    {
                        connections.Remove(connectionId);
                    }
                }
            }
        }
    }

    public List<string> GetDistinctConnectionIdsOnGroupList(IEnumerable<string> groups)
    {
        lock (_lock)
        {
            var result = new HashSet<string>();
            foreach (var group in groups)
            {
                if (_connectionsByGroup.TryGetValue(group, out var connections))
                {
                    result.UnionWith(connections);
                }
            }
            return result.ToList();
        }
    }

    private static HashSet<string> GetOrAddSet(Dictionary<string, HashSet<string>> dictionary, string key)
    {
        if (!dictionary.TryGetValue(key, out var set))
        {
            set = new HashSet<string>();
            dictionary[key] = set;
        }
        return set;
    }
}

/// <summary>Group manager backing <see cref="Hubs.OrbitMeshHub"/> and <see cref="Hubs.EdgeHub"/> subscriptions.</summary>
public sealed class PackageGroupManager : OrbitMeshGroupManager;

/// <summary>Group manager backing <see cref="Hubs.ConsumerHub"/> subscriptions.</summary>
public sealed class ConsumerGroupManager : OrbitMeshGroupManager;
