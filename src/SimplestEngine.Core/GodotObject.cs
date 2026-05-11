namespace SimplestEngine;

/// <summary>
/// Engine object base class (Godot's Object).
/// All Node, Resource, RefCounted descend from here.
/// Identity tracked via ObjectDB / InstanceId.
/// Signals stored per-instance keyed by StringName.
/// </summary>
public class GodotObject
{
    public ulong InstanceId { get; }

    private readonly Dictionary<StringName, List<SignalConnection>> _connections = new();

    public GodotObject()
    {
        InstanceId = ObjectDB.Register(this);
    }

    ~GodotObject()
    {
        ObjectDB.Unregister(InstanceId);
    }

    public virtual StringName ClassName => StringName.Get(GetType().Name);

    public ClassInfo? Class => ClassDB.GetClass(GetType());

    // --- property access -------------------------------------------------

    public virtual Variant Get(StringName property)
    {
        var info = Class?.FindProperty(property);
        return info?.Getter?.Invoke(this) ?? Variant.Nil;
    }

    public virtual bool Set(StringName property, Variant value)
    {
        var info = Class?.FindProperty(property);
        if (info?.Setter is null) return false;
        info.Setter(this, value);
        return true;
    }

    public bool HasProperty(StringName property) => Class?.FindProperty(property) is not null;

    // --- method invocation ----------------------------------------------

    public virtual Variant Call(StringName method, ReadOnlySpan<Variant> args)
    {
        var info = Class?.FindMethod(method);
        return info is null ? Variant.Nil : info.Invoker(this, args);
    }

    public bool HasMethod(StringName method) => Class?.FindMethod(method) is not null;

    // --- signal API ------------------------------------------------------

    public bool HasSignal(StringName signal) => Class?.FindSignal(signal) is not null;

    public SignalHandle Connect(StringName signal, Callable target, ConnectFlags flags = 0)
    {
        if (!_connections.TryGetValue(signal, out var list))
        {
            list = new List<SignalConnection>();
            _connections[signal] = list;
        }
        var conn = new SignalConnection(target, flags);
        list.Add(conn);
        return new SignalHandle(this, signal, conn);
    }

    public void Disconnect(StringName signal, Callable target)
    {
        if (!_connections.TryGetValue(signal, out var list)) return;
        list.RemoveAll(c => c.Target.Equals(target));
    }

    public bool IsConnected(StringName signal, Callable target)
    {
        if (!_connections.TryGetValue(signal, out var list)) return false;
        foreach (var c in list) if (c.Target.Equals(target)) return true;
        return false;
    }

    public void EmitSignal(StringName signal, params Variant[] args) =>
        EmitSignal(signal, (ReadOnlySpan<Variant>)args);

    public void EmitSignal(StringName signal, ReadOnlySpan<Variant> args)
    {
        if (!_connections.TryGetValue(signal, out var list)) return;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var conn = list[i];
            if ((conn.Flags & ConnectFlags.Deferred) != 0)
            {
                MessageQueueGlobals.EnqueueCall(conn.Target, args.ToArray());
            }
            else
            {
                conn.Target.Invoke(args);
            }
            if ((conn.Flags & ConnectFlags.OneShot) != 0)
            {
                list.RemoveAt(i);
            }
        }
    }

    internal void RemoveConnection(StringName signal, SignalConnection conn)
    {
        if (_connections.TryGetValue(signal, out var list)) list.Remove(conn);
    }
}

[Flags]
public enum ConnectFlags
{
    None = 0,
    Deferred = 1,
    OneShot = 2,
    ReferenceCounted = 4,
}

internal sealed class SignalConnection
{
    public Callable Target;
    public ConnectFlags Flags;
    public SignalConnection(Callable t, ConnectFlags f) { Target = t; Flags = f; }
}

public readonly struct SignalHandle
{
    public readonly GodotObject Owner;
    public readonly StringName SignalName;
    private readonly SignalConnection _conn;

    internal SignalHandle(GodotObject owner, StringName name, SignalConnection conn)
    { Owner = owner; SignalName = name; _conn = conn; }

    public void Disconnect() => Owner.RemoveConnection(SignalName, _conn);
}

/// <summary>
/// Function reference. Can wrap:
///  - a managed delegate
///  - a (target object, method name) pair (Godot-style)
///  - a script-language closure (resolved by ScriptServer)
/// </summary>
public readonly struct Callable : IEquatable<Callable>
{
    private readonly object? _target;
    private readonly StringName _method;
    private readonly Func<Variant[], Variant>? _delegate;

    public bool IsNull => _target is null && _delegate is null;

    public Callable(GodotObject target, StringName method)
    { _target = target; _method = method; _delegate = null; }

    public Callable(Func<Variant[], Variant> del)
    { _target = null; _method = StringName.Empty; _delegate = del; }

    public Callable(object scriptCallable)
    { _target = scriptCallable; _method = StringName.Empty; _delegate = null; }

    public Variant Invoke(ReadOnlySpan<Variant> args)
    {
        if (_delegate is not null) return _delegate(args.ToArray());
        if (_target is GodotObject obj && !_method.IsEmpty) return obj.Call(_method, args);
        if (_target is IScriptInvokable inv) return inv.Invoke(args);
        return Variant.Nil;
    }

    public bool Equals(Callable other) =>
        ReferenceEquals(_target, other._target) && _method == other._method && _delegate == other._delegate;
    public override bool Equals(object? obj) => obj is Callable c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(_target, _method, _delegate);
}

/// <summary>Implemented by script-language Callable wrappers (Lua closure, etc.).</summary>
public interface IScriptInvokable
{
    Variant Invoke(ReadOnlySpan<Variant> args);
}

/// <summary>
/// Global hooks for the deferred message queue (call_deferred, deferred signals, tree mutations).
/// The Scene layer owns the actual queue; Core just provides the entry point so signals can
/// enqueue without depending on Scene.
/// </summary>
public static class MessageQueueGlobals
{
    public static Action<Callable, Variant[]>? EnqueueCallHook;

    public static void EnqueueCall(Callable target, Variant[] args)
    {
        if (EnqueueCallHook is not null) EnqueueCallHook(target, args);
        else target.Invoke(args);
    }
}
