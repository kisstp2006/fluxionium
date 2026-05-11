namespace SimplestEngine;

/// <summary>
/// Godot's <c>Label</c> control. v1 uses our built-in <see cref="BitmapFont"/>
/// so projects can render text without importing a font resource. The public
/// property surface (text, modulate via FontColor, pixel scale) is intentionally
/// a strict subset of Godot 3.x's Label so projects keep loading once a real
/// Theme / DynamicFont system lands.
///
/// Like Godot's <c>Label</c>, this class would normally descend from <c>Control</c>;
/// we approximate it as a <see cref="Node2D"/> for v1 and revisit when the Control
/// hierarchy arrives.
/// </summary>
[GDClass("Label", "Node2D")]
public class Label : Node2D
{
    private string _text = string.Empty;
    private float _pixelScale = 2f;
    private Color _fontColor = Color.White;

    public string Text
    {
        get => _text;
        set { _text = value ?? string.Empty; QueueRedraw(); }
    }

    /// <summary>Tint applied to the rasterised glyph pixels.</summary>
    public Color FontColor
    {
        get => _fontColor;
        set { _fontColor = value; QueueRedraw(); }
    }

    /// <summary>
    /// Approximate font height in pixels. Internally drives the per-pixel scale of
    /// the 5×7 bitmap font (1 unit = <see cref="BitmapFont.GlyphHeight"/> dots tall).
    /// Keeps the Godot inspector key "FontSize" while we lack a real Font resource.
    /// </summary>
    public float FontSize
    {
        get => _pixelScale * BitmapFont.GlyphHeight;
        set { _pixelScale = MathF.Max(1f, value / BitmapFont.GlyphHeight); QueueRedraw(); }
    }

    public override void _Draw()
    {
        if (string.IsNullOrEmpty(_text) || RenderingServer is null) return;
        BitmapFont.Emit(
            RenderingServer,
            CanvasItemRid,
            _text,
            Vector2.Zero,
            _fontColor * Modulate,
            _pixelScale);
    }
}
