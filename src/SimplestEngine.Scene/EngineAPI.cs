using SimplestEngine.Abi;

namespace SimplestEngine;

/// <summary>
/// Default <see cref="IEngineAPI"/> implementation. THE neural spine.
/// Every script binding goes through this — never directly touching engine
/// internals. Bound to a SceneTree at runtime.
/// </summary>
public sealed class EngineAPI : IEngineAPI
{
    private readonly SceneTree _tree;
    public EngineAPI(SceneTree tree) { _tree = tree; }

    public Variant GetProperty(IGodotObjectAPI obj, StringName name)
        => obj is GodotObject go ? go.Get(name) : Variant.Nil;

    public void SetProperty(IGodotObjectAPI obj, StringName name, Variant value)
    { if (obj is GodotObject go) go.Set(name, value); }

    public Variant CallMethod(IGodotObjectAPI obj, StringName method, ReadOnlySpan<Variant> args)
        => obj is GodotObject go ? go.Call(method, args.ToArray()) : Variant.Nil;

    public SignalHandle Connect(IGodotObjectAPI obj, StringName signal, Callable target, ConnectFlags flags)
        => obj is GodotObject go ? go.Connect(signal, target, flags) : default;

    public void Disconnect(IGodotObjectAPI obj, StringName signal, Callable target)
    { if (obj is GodotObject go) go.Disconnect(signal, target); }

    public IGodotObjectAPI? FindNode(NodePath path)
        => _tree.Root.GetNodeOrNull(path);

    public void CallDeferred(IGodotObjectAPI obj, StringName method, ReadOnlySpan<Variant> args)
    {
        if (obj is GodotObject go)
            _tree.Messages.Enqueue(new Callable(go, method), args.ToArray());
    }

    public void QueueFree(IGodotObjectAPI obj)
    { if (obj is Node n) _tree.QueueFree(n); }

    public IGodotObjectAPI? Instantiate(StringName className)
        => ClassDB.Instantiate(className) as IGodotObjectAPI;

    public IInputServer? Input => _tree.InputServer;
}
