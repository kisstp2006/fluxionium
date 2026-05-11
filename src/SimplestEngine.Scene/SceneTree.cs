using SimplestEngine.Abi;

namespace SimplestEngine;

/// <summary>
/// The engine main loop and root of all scene-tree iteration. THE spine.
/// Pandemonium parity (subset): node lifecycle, groups, deferred mutation,
/// process / physics_process scheduling.
/// </summary>
public sealed class SceneTree
{
    public Viewport Root { get; }
    public MessageQueue Messages { get; } = new();
    public SceneMutationQueue Mutations { get; } = new();
    public DeferredFreeQueue Frees { get; } = new();
    public FrameAllocator FrameAllocator { get; } = new();

    public IRenderingServer? RenderingServer { get; set; }
    public IPhysicsBackend? PhysicsBackend { get; set; }
    public IInputServer? InputServer { get; set; }

    public Camera2D? CurrentCamera2D { get; private set; }

    public float TimeScale { get; set; } = 1f;
    public bool Paused { get; set; }

    private readonly Dictionary<StringName, HashSet<Node>> _groups = new();

    private float _physicsAccumulator;
    private const float PhysicsStep = 1f / 60f;

    public SceneTree()
    {
        Root = new Viewport { Name = StringName.Get("root") };
        // Hook so that deferred signals coming from anywhere land in OUR queue.
        MessageQueueGlobals.EnqueueCallHook = (callable, args) => Messages.Enqueue(callable, args);
        Root._SetTree(this);
    }

    public void SetCurrentCamera(Camera2D cam) => CurrentCamera2D = cam;

    public void RegisterInGroup(Node node, StringName group)
    {
        if (!_groups.TryGetValue(group, out var set))
        { set = new HashSet<Node>(); _groups[group] = set; }
        set.Add(node);
    }

    public void UnregisterFromGroup(Node node, StringName group)
    {
        if (_groups.TryGetValue(group, out var set)) set.Remove(node);
    }

    public IEnumerable<Node> GetNodesInGroup(StringName group) =>
        _groups.TryGetValue(group, out var s) ? s : Enumerable.Empty<Node>();

    public void CallGroup(StringName group, StringName method, params Variant[] args)
    {
        foreach (var n in GetNodesInGroup(group))
            n.Call(method, args);
    }

    public void QueueFree(Node node) =>
        Mutations.Enqueue(new SceneMutationQueue.Mutation
        { Type = SceneMutationQueue.MutationType.Free, Target = node });

    /// <summary>The single per-frame tick. Order matches Godot/Pandemonium.</summary>
    public void Process(float realDelta)
    {
        if (Paused) return;
        FrameAllocator.Reset();

        float delta = realDelta * TimeScale;

        // 1) Physics: fixed-step accumulator
        _physicsAccumulator += delta;
        while (_physicsAccumulator >= PhysicsStep)
        {
            _physicsAccumulator -= PhysicsStep;
            PhysicsBackend?.Step(PhysicsStep);
            // collision events translate into deferred signals - drained in Messages.Flush
            DrainCollisions();
            Root._PropagatePhysicsProcess(PhysicsStep);
        }

        // 2) Idle process
        Root._PropagateProcess(delta);

        // 3) Recording canvas draw commands for dirty items
        Root._PropagateRedraw();

        // 4) Submit frame
        RenderingServer?.Frame();

        // 5) Flush deferred queues - this can enqueue tree mutations, which next:
        Messages.Flush();

        // 6) Apply deferred mutations to the tree
        ApplyMutations();

        // 7) Finally free objects that were queue_free'd
        Frees.Drain();
    }

    private void DrainCollisions()
    {
        if (PhysicsBackend is null) return;
        var enters = PhysicsBackend.ConsumeCollisionEnters();
        var exits = PhysicsBackend.ConsumeCollisionExits();
        // physics nodes will hook user-data->node mapping via deferred signal emit.
        foreach (var (a, b, info) in enters)
        {
            var na = PhysicsBackend.BodyGetUserData(a) as Node;
            var nb = PhysicsBackend.BodyGetUserData(b) as Node;
            na?.EmitSignal(StringName.Get("body_entered"), Variant.FromObject(nb));
            nb?.EmitSignal(StringName.Get("body_entered"), Variant.FromObject(na));
        }
        foreach (var (a, b) in exits)
        {
            var na = PhysicsBackend.BodyGetUserData(a) as Node;
            var nb = PhysicsBackend.BodyGetUserData(b) as Node;
            na?.EmitSignal(StringName.Get("body_exited"), Variant.FromObject(nb));
            nb?.EmitSignal(StringName.Get("body_exited"), Variant.FromObject(na));
        }
    }

    private void ApplyMutations()
    {
        var list = Mutations.Drain();
        foreach (var m in list)
        {
            switch (m.Type)
            {
                case SceneMutationQueue.MutationType.Free:
                    if (m.Target is Node n)
                    {
                        n.Parent?.RemoveChild(n);
                        Frees.Enqueue(n);
                    }
                    break;
                case SceneMutationQueue.MutationType.AddChild:
                    (m.Target as Node)?.AddChild((Node)m.Other!);
                    break;
                case SceneMutationQueue.MutationType.RemoveChild:
                    (m.Target as Node)?.RemoveChild((Node)m.Other!);
                    break;
            }
        }
    }

    public void ChangeSceneTo(Node newScene)
    {
        foreach (var c in Root.Children.ToArray()) Root.RemoveChild(c);
        Root.AddChild(newScene);
    }
}

/// <summary>Root of the scene tree; carries the rendering canvas RID.</summary>
[GDClass("Viewport", "Node")]
public class Viewport : Node
{
    public Rid CanvasRid { get; internal set; }

    internal void _SetTree(SceneTree tree)
    {
        // Force-set the tree on the root so children inherit it on add.
        var f = typeof(Node).GetField("_tree", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        f?.SetValue(this, tree);
        var ins = typeof(Node).GetField("_insideTree", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        ins?.SetValue(this, true);
    }
}

/// <summary>Walks the canvas-item tree to record draws this frame.</summary>
internal static class RedrawPropagation
{
    public static void _PropagateRedraw(this Node node)
    {
        if (node is CanvasItem ci) ci.RecordDrawForFrame();
        foreach (var c in node.Children) c._PropagateRedraw();
    }
}

internal static class CanvasItemFrameExt
{
    public static void RecordDrawForFrame(this CanvasItem ci) => ci.GetType()
        .GetMethod("RecordDraw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
        ?.Invoke(ci, null);
}
