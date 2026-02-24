using System.Collections.Concurrent;

namespace Linteum.Api.Services;

public interface IConnectionTracker
{
    void AddConnection(string connectionId);
    void RemoveConnection(string connectionId);
    void AddToGroup(string connectionId, string groupName);
    void RemoveFromGroup(string connectionId, string groupName);
    int GetGroupCount(string groupName);
    int GetTotalConnectionCount();
}

public class ConnectionTracker : IConnectionTracker
{
    private readonly ConcurrentDictionary<string, HashSet<string>> _connectionGroups = new();
    
    private readonly ConcurrentDictionary<string, HashSet<string>> _groupConnections = new();
    
    private readonly object _lock = new();

    public void AddConnection(string connectionId)
    {
        _connectionGroups.TryAdd(connectionId, new HashSet<string>());
    }

    public void RemoveConnection(string connectionId)
    {
        if (_connectionGroups.TryRemove(connectionId, out var groups))
        {
            lock (_lock)
            {
                foreach (var group in groups)
                {
                    if (_groupConnections.TryGetValue(group, out var connections))
                    {
                        connections.Remove(connectionId);
                        if (connections.Count == 0)
                        {
                            _groupConnections.TryRemove(group, out _);
                        }
                    }
                }
            }
        }
    }

    public void AddToGroup(string connectionId, string groupName)
    {
        lock (_lock)
        {
            if (_connectionGroups.ContainsKey(connectionId))
            {
                _connectionGroups[connectionId].Add(groupName);
            }
            else
            {
                 _connectionGroups.TryAdd(connectionId, new HashSet<string> { groupName });
            }

            var connections = _groupConnections.GetOrAdd(groupName, _ => new HashSet<string>());
            connections.Add(connectionId);
        }
    }

    public void RemoveFromGroup(string connectionId, string groupName)
    {
        lock (_lock)
        {
            if (_connectionGroups.TryGetValue(connectionId, out var groups))
            {
                groups.Remove(groupName);
            }

            if (_groupConnections.TryGetValue(groupName, out var connections))
            {
                connections.Remove(connectionId);
                if (connections.Count == 0)
                {
                    _groupConnections.TryRemove(groupName, out _);
                }
            }
        }
    }

    public int GetGroupCount(string groupName)
    {
        lock (_lock)
        {
            return _groupConnections.TryGetValue(groupName, out var connections) ? connections.Count : 0;
        }
    }

    public int GetTotalConnectionCount()
    {
        return _connectionGroups.Count;
    }
}

