using SimplestEngine.Abi;

namespace SimplestEngine;

/// <summary>
/// Engine scene-graph node. Pandemonium parity: extends Object, owns children,
/// participates in SceneTree lifecycle, holds groups, supports NodePath addressing.
/// </summary>
[GDClass("Node", "Object")]
public class Node : GodotObject, IGodotObjectAPI
{
    // --- tree state -----------------------------------------------------
    private Node? _parent;
    private readonly List<Node> _children = new();
    private SceneTree? _tree;
    private StringName _name = StringName.Empty;
    private string _displayName = "Node";
    private bool _insideTree;
    private bool _readyNotified;
    private readonly HashSet<StringName> _groups = new();

    // --- process flags --------------------------------------------------
    private bool _processEnabled = true;
    private bool _physicsProcessEnabled = true;
    private bool _inputEnabled;
    private int _processPriority;
    private int _blocked;  // re-entrancy guard - matches Pandemonium parity

    // --- script ---------------------------------------------------------
    private IScriptInstance? _script;

    public Node? Parent => _parent;
    public IReadOnlyList<Node> Children => _children;
    public SceneTree? Tree => _tree;
    public bool IsInsideTree => _insideTree;
    public int ProcessPriority { get => _processPriority; set => _processPriority = value; }

    public Node()
    {
        _displayName = GetType().Name;
        _name = StringName.Get(_displayName);
    }

    public StringName Name
    {
        get => _name;
        set { _name = value; _displayName = value.ToString(); }
    }

    public string DisplayName => _displayName;

    public override StringName ClassName => StringName.Get(GetType().Name);

    // --- children -------------------------------------------------------

    public virtual void AddChild(Node child, bool legibleUniqueName = false)
    {
        if (child is null) throw new ArgumentNullException(nameof(child));
        if (child._parent is not null)
            throw new InvalidOperationException($"Node '{child._displayName}' already has a parent.");
        if (_blocked > 0)
        {
            // tree is being traversed - defer
            _tree?.Mutations.Enqueue(new SceneMutationQueue.Mutation
            { Type = SceneMutationQueue.MutationType.AddChild, Target = this, Other = child });
            return;
        }

        child._parent = this;
        _children.Add(child);
        child.Notification(Notifications.NotificationParented);

        if (_insideTree && _tree is not null)
            child._PropagateEnterTree(_tree);
    }

    public virtual void RemoveChild(Node child)
    {
        if (child is null || child._parent != this) return;
        if (_blocked > 0)
        {
            _tree?.Mutations.Enqueue(new SceneMutationQueue.Mutation
            { Type = SceneMutationQueue.MutationType.RemoveChild, Target = this, Other = child });
            return;
        }
        if (child._insideTree) child._PropagateExitTree();
        _children.Remove(child);
        child._parent = null;
        child.Notification(Notifications.NotificationUnparented);
    }

    public Node? GetNodeOrNull(NodePath path)
    {
        Node? current = path.IsAbsolute ? (_tree?.Root) : this;
        if (current is null) return null;
        for (int i = 0; i < path.NameCount; i++)
        {
            var token = path.GetName(i);
            if (token == StringName.Get(".")) continue;
            if (token == StringName.Get(".."))
            {
                current = current._parent;
                if (current is null) return null;
                continue;
            }
            current = current.FindChildByName(token);
            if (current is null) return null;
        }
        return current;
    }

    public Node GetNode(NodePath path) =>
        GetNodeOrNull(path) ?? throw new KeyNotFoundException($"Node not found: {path}");

    public Node? FindChildByName(StringName name)
    {
        foreach (var c in _children) if (c._name == name) return c;
        return null;
    }

    // --- groups ---------------------------------------------------------

    public void AddToGroup(StringName group)
    {
        if (_groups.Add(group)) _tree?.RegisterInGroup(this, group);
    }
    public void RemoveFromGroup(StringName group)
    {
        if (_groups.Remove(group)) _tree?.UnregisterFromGroup(this, group);
    }
    public bool IsInGroup(StringName group) => _groups.Contains(group);
    public IEnumerable<StringName> Groups => _groups;

    // --- lifecycle ------------------------------------------------------

    internal void _PropagateEnterTree(SceneTree tree)
    {
        _tree = tree;
        _insideTree = true;
        foreach (var g in _groups) tree.RegisterInGroup(this, g);
        Notification(Notifications.NotificationEnterTree);
        _EnterTree();
        _script?.Call(StringName.Get("_enter_tree"), ReadOnlySpan<Variant>.Empty);
        Notification(Notifications.NotificationPostEnterTree);

        _blocked++;
        foreach (var c in _children) c._PropagateEnterTree(tree);
        _blocked--;

        if (!_readyNotified)
        {
            _readyNotified = true;
            Notification(Notifications.NotificationReady);
            _Ready();
            _script?.Call(StringName.Get("_ready"), ReadOnlySpan<Variant>.Empty);
        }
    }

    internal void _PropagateExitTree()
    {
        _blocked++;
        foreach (var c in _children) c._PropagateExitTree();
        _blocked--;

        Notification(Notifications.NotificationExitTree);
        _ExitTree();
        _script?.Call(StringName.Get("_exit_tree"), ReadOnlySpan<Variant>.Empty);
        if (_tree is not null)
            foreach (var g in _groups) _tree.UnregisterFromGroup(this, g);
        _tree = null;
        _insideTree = false;
        _readyNotified = false;
    }

    internal void _PropagateProcess(float delta)
    {
        if (!_insideTree) return;
        if (_processEnabled)
        {
            Notification(Notifications.NotificationProcess);
            _Process(delta);
            _script?.Call(StringName.Get("_process"), new ReadOnlySpan<Variant>(new[] { Variant.From(delta) }));
        }
        _blocked++;
        foreach (var c in _children) c._PropagateProcess(delta);
        _blocked--;
    }

    internal void _PropagatePhysicsProcess(float delta)
    {
        if (!_insideTree) return;
        if (_physicsProcessEnabled)
        {
            Notification(Notifications.NotificationPhysicsProcess);
            _PhysicsProcess(delta);
            _script?.Call(StringName.Get("_physics_process"), new ReadOnlySpan<Variant>(new[] { Variant.From(delta) }));
        }
        _blocked++;
        foreach (var c in _children) c._PropagatePhysicsProcess(delta);
        _blocked--;
    }

    // --- notification dispatch ------------------------------------------

    public virtual void Notification(int what)
    {
        _Notification(what);
        _script?.Notification(what);
    }

    protected virtual void _Notification(int what) { }

    // --- virtual lifecycle methods (Godot _foo style) ------------------

    public virtual void _EnterTree() { }
    public virtual void _ExitTree() { }
    public virtual void _Ready() { }
    public virtual void _Process(float delta) { }
    public virtual void _PhysicsProcess(float delta) { }
    public virtual void _Input(InputEvent e) { }
    public virtual void _UnhandledInput(InputEvent e) { }
    public virtual void _Draw() { }

    public bool ProcessMode { get => _processEnabled; set => _processEnabled = value; }
    public bool PhysicsProcessMode { get => _physicsProcessEnabled; set => _physicsProcessEnabled = value; }

    // --- script -----------------------------------------------------

    public void SetScript(IScriptInstance? instance) => _script = instance;
    public IScriptInstance? GetScript() => _script;

    // --- queue_free ----------------------------------------------------

    public void QueueFree() { _tree?.QueueFree(this); }
}

/// <summary>Placeholder until real InputEvent hierarchy is in place.</summary>
public abstract class InputEvent
{
    public bool Handled { get; set; }
}

public sealed class InputEventKey : InputEvent
{
    public Abi.KeyCode Key;
    public bool Pressed;
    public bool Echo;
}

public sealed class InputEventMouseMotion : InputEvent
{
    public Vector2 Position;
    public Vector2 Relative;
}

public sealed class InputEventMouseButton : InputEvent
{
    public Abi.MouseButton Button;
    public Vector2 Position;
    public bool Pressed;
}
