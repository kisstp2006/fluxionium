namespace SimplestEngine;

/// <summary>
/// Pandemonium / Godot 3.x parity: 2D textured sprite.
/// (Godot 4 renamed this to Sprite2D - keeping Pandemonium name for .tscn parity.)
/// </summary>
[GDClass("Sprite", "Node2D")]
public class Sprite : Node2D
{
    private Texture2D? _texture;
    private bool _centered = true;
    private Vector2 _offset = Vector2.Zero;
    private bool _flipH;
    private bool _flipV;
    private bool _region;
    private Rect2 _regionRect;
    private int _hframes = 1;
    private int _vframes = 1;
    private int _frame;

    public Texture2D? Texture
    {
        get => _texture;
        set { _texture = value; QueueRedraw(); }
    }
    public bool Centered { get => _centered; set { _centered = value; QueueRedraw(); } }
    public Vector2 Offset { get => _offset; set { _offset = value; QueueRedraw(); } }
    public bool FlipH { get => _flipH; set { _flipH = value; QueueRedraw(); } }
    public bool FlipV { get => _flipV; set { _flipV = value; QueueRedraw(); } }
    public bool Region { get => _region; set { _region = value; QueueRedraw(); } }
    public Rect2 RegionRect { get => _regionRect; set { _regionRect = value; QueueRedraw(); } }
    public int HFrames { get => _hframes; set { _hframes = Math.Max(1, value); QueueRedraw(); } }
    public int VFrames { get => _vframes; set { _vframes = Math.Max(1, value); QueueRedraw(); } }
    public int Frame { get => _frame; set { _frame = value; QueueRedraw(); } }

    public override void _Draw()
    {
        if (_texture is null || RenderingServer is null) return;

        var size = _region
            ? _regionRect.Size
            : new Vector2(_texture.Width / (float)_hframes, _texture.Height / (float)_vframes);
        var pos = _offset;
        if (_centered) pos -= size * 0.5f;
        if (_flipH) size = new Vector2(-size.X, size.Y);
        if (_flipV) size = new Vector2(size.X, -size.Y);

        Rect2 dst = new(pos, size);
        if (_region)
        {
            RenderingServer.CanvasItemAddTextureRectRegion(CanvasItemRid, dst, _texture.Rid, _regionRect, Modulate);
        }
        else if (_hframes * _vframes > 1)
        {
            int fx = _frame % _hframes;
            int fy = _frame / _hframes;
            float fw = _texture.Width / (float)_hframes;
            float fh = _texture.Height / (float)_vframes;
            Rect2 src = new(new Vector2(fx * fw, fy * fh), new Vector2(fw, fh));
            RenderingServer.CanvasItemAddTextureRectRegion(CanvasItemRid, dst, _texture.Rid, src, Modulate);
        }
        else
        {
            RenderingServer.CanvasItemAddTextureRect(CanvasItemRid, dst, _texture.Rid, Modulate);
        }
    }
}
