namespace SimplestEngine;

/// <summary>
/// Pandemonium / Godot 3.x parity: 2D textured sprite.
/// (Godot 4 renamed this to Sprite2D - keeping Pandemonium name for .tscn parity.)
/// </summary>
[GDClass("Sprite", "Node2D")]
public class Sprite : Node2D
{
    private Texture2D? _texture;
    private Texture2D? _normalMap;
    private bool _centered = true;
    private Vector2 _offset = Vector2.Zero;
    private bool _flipH;
    private bool _flipV;
    private bool _region;
    private Rect2 _regionRect;
    private bool _regionFilterClip;
    private int _hframes = 1;
    private int _vframes = 1;
    private int _frame;

    public Texture2D? Texture
    {
        get => _texture;
        set { _texture = value; QueueRedraw(); }
    }
    public Texture2D? NormalMap
    {
        get => _normalMap;
        set { _normalMap = value; QueueRedraw(); }
    }
    public bool Centered { get => _centered; set { _centered = value; QueueRedraw(); } }
    public Vector2 Offset { get => _offset; set { _offset = value; QueueRedraw(); } }
    public bool FlipH { get => _flipH; set { _flipH = value; QueueRedraw(); } }
    public bool FlipV { get => _flipV; set { _flipV = value; QueueRedraw(); } }
    public bool Region { get => _region; set { _region = value; QueueRedraw(); } }
    public Rect2 RegionRect { get => _regionRect; set { _regionRect = value; QueueRedraw(); } }
    public bool RegionFilterClip { get => _regionFilterClip; set { _regionFilterClip = value; QueueRedraw(); } }
    public int HFrames { get => _hframes; set { _hframes = Math.Max(1, value); ClampFrame(); QueueRedraw(); } }
    public int VFrames { get => _vframes; set { _vframes = Math.Max(1, value); ClampFrame(); QueueRedraw(); } }
    public int Frame { get => _frame; set { _frame = Math.Clamp(value, 0, MaxFrame); QueueRedraw(); } }
    public Vector2 FrameCoords
    {
        get => new(_frame % _hframes, _frame / _hframes);
        set
        {
            int x = Math.Clamp((int)value.X, 0, _hframes - 1);
            int y = Math.Clamp((int)value.Y, 0, _vframes - 1);
            Frame = y * _hframes + x;
        }
    }

    private int MaxFrame => Math.Max(0, _hframes * _vframes - 1);

    public bool IsPixelOpaque(Vector2 point) => GetRect().HasPoint(point);
    public Rect2 GetRect()
    {
        var size = GetFrameSize();
        var pos = _offset;
        if (_centered) pos -= size * 0.5f;
        return new Rect2(pos, size);
    }

    private void ClampFrame() => _frame = Math.Clamp(_frame, 0, MaxFrame);

    public override void _Draw()
    {
        if (_texture is null || RenderingServer is null) return;

        var dst = GetRect();
        var src = GetSourceRect();

        if (src.Position == Vector2.Zero &&
            src.Size == new Vector2(_texture.Width, _texture.Height) &&
            !_flipH && !_flipV)
            RenderingServer.CanvasItemAddTextureRect(CanvasItemRid, dst, _texture.Rid, Modulate);
        else
            RenderingServer.CanvasItemAddTextureRectRegion(CanvasItemRid, dst, _texture.Rid, src, Modulate);
    }

    private Vector2 GetFrameSize()
    {
        if (_texture is null) return Vector2.Zero;
        var baseSize = _region ? _regionRect.Size : new Vector2(_texture.Width, _texture.Height);
        return new Vector2(baseSize.X / _hframes, baseSize.Y / _vframes);
    }

    private Rect2 GetSourceRect()
    {
        if (_texture is null) return default;

        var basePosition = _region ? _regionRect.Position : Vector2.Zero;
        var frameSize = GetFrameSize();
        int fx = _frame % _hframes;
        int fy = _frame / _hframes;
        var pos = basePosition + new Vector2(fx * frameSize.X, fy * frameSize.Y);
        var size = frameSize;

        if (_flipH)
        {
            pos = new Vector2(pos.X + size.X, pos.Y);
            size = new Vector2(-size.X, size.Y);
        }
        if (_flipV)
        {
            pos = new Vector2(pos.X, pos.Y + size.Y);
            size = new Vector2(size.X, -size.Y);
        }

        return new Rect2(pos, size);
    }
}
