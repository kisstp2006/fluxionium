namespace SimplestEngine;

/// <summary>
/// Godot's <c>Label</c> control. Renders <see cref="Text"/> at <see cref="FontSize"/>
/// pixels using the assigned <see cref="Font"/> resource, or the engine default
/// (Noto Sans Regular via <see cref="SimplestEngine.Font.DefaultFontFactory"/>)
/// when no font is set. Property surface tracks Godot 3.x Label.
///
/// Like Godot's <c>Label</c>, this class would normally descend from <c>Control</c>;
/// we approximate it as a <see cref="Node2D"/> until the Control hierarchy lands.
/// </summary>
[GDClass("Label", "Node2D")]
public class Label : Node2D
{
    private string _text = string.Empty;
    private int _fontSize = 14;
    private Color _fontColor = Color.White;
    private Font? _font;

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

    /// <summary>Outline color when <see cref="OutlineSize"/> > 0. Godot parity.</summary>
    public Color OutlineColor { get; set; } = new Color(0, 0, 0, 1);
    /// <summary>Outline thickness in pixels (0 disables). Wired through to
    /// DynamicFont when the assigned font supports outlines.</summary>
    public int OutlineSize { get; set; } = 0;

    /// <summary>Font resource used to render the label. <c>null</c> means
    /// "use the engine default at <see cref="FontSize"/>".</summary>
    public Font? Font
    {
        get => _font;
        set { _font = value; QueueRedraw(); }
    }

    /// <summary>Font pixel size. When <see cref="Font"/> is null the label asks
    /// <see cref="SimplestEngine.Font.GetDefault"/> for a Noto Sans instance at
    /// this size; when the font is a DynamicFont, set the size on the font
    /// itself (this property is only the *requested* size for the default
    /// fallback path).</summary>
    public int FontSize
    {
        get => _fontSize;
        set { _fontSize = Math.Max(1, value); QueueRedraw(); }
    }

    public override void _Draw()
    {
        if (string.IsNullOrEmpty(_text) || RenderingServer is null) return;
        var font = _font ?? SimplestEngine.Font.GetDefault(_fontSize);
        font.Draw(RenderingServer, CanvasItemRid, Vector2.Zero, _text, _fontColor * Modulate);
    }
}
