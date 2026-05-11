namespace SimplestEngine.Abi;

/// <summary>
/// THE NEURAL SPINE of the engine. All script bindings, plugins, replication,
/// hot reload, sandboxing, and remote-editor RPC go through this interface.
/// Implementations MUST NOT leak backend specifics (Veldrid, Aether, MoonSharp).
/// </summary>
public interface IEngineAPI
{
    Variant GetProperty(IGodotObjectAPI obj, StringName name);
    void    SetProperty(IGodotObjectAPI obj, StringName name, Variant value);
    Variant CallMethod (IGodotObjectAPI obj, StringName method, ReadOnlySpan<Variant> args);

    SignalHandle Connect(IGodotObjectAPI obj, StringName signal, Callable target, ConnectFlags flags);
    void Disconnect(IGodotObjectAPI obj, StringName signal, Callable target);

    /// <summary>Late-resolve a node path against the engine's current tree.</summary>
    IGodotObjectAPI? FindNode(NodePath path);

    /// <summary>Schedule a deferred method call on the message queue.</summary>
    void CallDeferred(IGodotObjectAPI obj, StringName method, ReadOnlySpan<Variant> args);

    /// <summary>Schedule the object for deletion after the current frame.</summary>
    void QueueFree(IGodotObjectAPI obj);

    /// <summary>Construct a new instance of a registered class by name.</summary>
    IGodotObjectAPI? Instantiate(StringName className);

    /// <summary>
    /// Global input service (Godot's <c>Input</c> singleton). Stays optional so
    /// headless tools / tests can run without a window.
    /// </summary>
    IInputServer? Input { get; }
}

/// <summary>
/// Stable view of any engine-owned object. Script bindings see ONLY this,
/// never the concrete <see cref="GodotObject"/> / <see cref="Node"/> types.
/// </summary>
public interface IGodotObjectAPI
{
    ulong InstanceId { get; }
    StringName ClassName { get; }
    bool HasSignal(StringName signal);
    bool HasMethod(StringName method);
    bool HasProperty(StringName property);
}
