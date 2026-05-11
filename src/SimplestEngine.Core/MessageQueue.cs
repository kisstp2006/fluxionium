namespace SimplestEngine;

/// <summary>
/// Deferred call queue. Godot's "secret gravity generator":
///   - call_deferred
///   - set_deferred
///   - deferred signal emit (CONNECT_DEFERRED)
///   - deferred notifications
/// Flushed at end-of-frame. Newly enqueued items run on the NEXT frame
/// (so we don't infinite-loop).
/// </summary>
public sealed class MessageQueue
{
    private struct Message
    {
        public Callable Target;
        public Variant[] Args;
    }

    private List<Message> _frontBuffer = new();
    private List<Message> _backBuffer = new();
    private readonly object _lock = new();

    public void Enqueue(Callable target, Variant[] args)
    {
        lock (_lock)
        {
            _frontBuffer.Add(new Message { Target = target, Args = args });
        }
    }

    /// <summary>Swap buffers and run all messages from the previous frame. Newly enqueued items wait for next flush.</summary>
    public void Flush()
    {
        lock (_lock)
        {
            (_frontBuffer, _backBuffer) = (_backBuffer, _frontBuffer);
        }

        foreach (var m in _backBuffer)
        {
            try { m.Target.Invoke(m.Args); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MessageQueue] deferred call threw: {ex}");
            }
        }
        _backBuffer.Clear();
    }

    public int PendingCount { get { lock (_lock) return _frontBuffer.Count; } }
}

/// <summary>
/// Iteration-safe tree mutation queue. queue_free / add_child / remove_child / reparent
/// land here, never modify the tree during traversal.
/// </summary>
public sealed class SceneMutationQueue
{
    public enum MutationType { Free, AddChild, RemoveChild, Reparent }

    public struct Mutation
    {
        public MutationType Type;
        public GodotObject Target;
        public GodotObject? Other;
        public int Index;
    }

    private readonly List<Mutation> _pending = new();
    private readonly object _lock = new();

    public void Enqueue(Mutation m) { lock (_lock) _pending.Add(m); }

    public IReadOnlyList<Mutation> Drain()
    {
        lock (_lock)
        {
            var copy = _pending.ToArray();
            _pending.Clear();
            return copy;
        }
    }

    public int PendingCount { get { lock (_lock) return _pending.Count; } }
}

/// <summary>
/// Holds objects scheduled for deletion. Drained AFTER MessageQueue.Flush
/// so any tree_exiting signals can still safely reach the object.
/// </summary>
public sealed class DeferredFreeQueue
{
    private readonly List<GodotObject> _pending = new();
    private readonly object _lock = new();

    public void Enqueue(GodotObject obj) { lock (_lock) _pending.Add(obj); }

    public void Drain()
    {
        GodotObject[] copy;
        lock (_lock)
        {
            copy = _pending.ToArray();
            _pending.Clear();
        }
        foreach (var o in copy)
        {
            try { ObjectDB.Unregister(o.InstanceId); }
            catch (Exception ex) { Console.Error.WriteLine($"[DeferredFreeQueue] {ex}"); }
        }
    }

    public int PendingCount { get { lock (_lock) return _pending.Count; } }
}
