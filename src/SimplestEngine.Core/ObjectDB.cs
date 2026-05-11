namespace SimplestEngine;

/// <summary>
/// Tracks all engine Objects by stable InstanceId. Godot parity.
/// Allows lookup-by-id from script bindings, replication, serialization.
/// </summary>
public static class ObjectDB
{
    private static ulong _nextId = 1;
    private static readonly Dictionary<ulong, WeakReference> _objects = new();
    private static readonly object _lock = new();

    public static ulong Register(object obj)
    {
        lock (_lock)
        {
            var id = _nextId++;
            _objects[id] = new WeakReference(obj);
            return id;
        }
    }

    public static void Unregister(ulong id)
    {
        lock (_lock) _objects.Remove(id);
    }

    public static object? GetInstance(ulong id)
    {
        lock (_lock)
        {
            return _objects.TryGetValue(id, out var w) && w.IsAlive ? w.Target : null;
        }
    }

    public static int LiveCount
    {
        get
        {
            lock (_lock)
            {
                int n = 0;
                foreach (var w in _objects.Values) if (w.IsAlive) n++;
                return n;
            }
        }
    }
}
