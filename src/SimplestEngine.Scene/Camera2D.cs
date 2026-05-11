namespace SimplestEngine;

/// <summary>Pandemonium parity: 2D camera. Active camera transform is applied per-Viewport.</summary>
[GDClass("Camera2D", "Node2D")]
public class Camera2D : Node2D
{
    private bool _current;
    public Vector2 Zoom { get; set; } = Vector2.One;
    public Vector2 Offset { get; set; } = Vector2.Zero;

    public bool Current
    {
        get => _current;
        set
        {
            _current = value;
            if (value && IsInsideTree) Tree!.SetCurrentCamera(this);
        }
    }

    public override void _EnterTree()
    {
        base._EnterTree();
        if (_current) Tree?.SetCurrentCamera(this);
    }

    public Transform2D GetCameraTransform()
    {
        var t = GlobalTransform;
        // Pandemonium: camera transform inverts the world: view = inverse(transform * zoom)
        var camPos = t.Origin + Offset;
        var view = new Transform2D(new Vector2(1f / Zoom.X, 0), new Vector2(0, 1f / Zoom.Y), -camPos);
        return view;
    }
}
