namespace SimplestEngine;

/// <summary>
/// Godot's <c>ColorRect</c>. In Godot proper it descends from <c>Control</c>; until
/// we have a real Control hierarchy we model it as a <see cref="Node2D"/> so the
/// (position, size, color) tuple still works in v1 without dragging the full
/// Control layout machinery in. Backward compatible: when Control lands we will
/// re-parent without changing the public TSCN property surface (color, size).
/// </summary>
[GDClass("ColorRect", "Node2D")]
public class ColorRect : Node2D
{
    private Vector2 _size = new(64f, 64f);
    private Color _color = Color.White;

    /// <summary>Width/height in pixels of the rectangle.</summary>
    public Vector2 Size
    {
        get => _size;
        set { _size = value; QueueRedraw(); }
    }

    /// <summary>Fill colour. Multiplied by <see cref="CanvasItem.Modulate"/>.</summary>
    public Color Color
    {
        get => _color;
        set { _color = value; QueueRedraw(); }
    }

    public override void _Draw()
    {
        if (RenderingServer is null) return;
        RenderingServer.CanvasItemAddRect(CanvasItemRid,
            new Rect2(Vector2.Zero, _size),
            _color * Modulate);
    }
}
