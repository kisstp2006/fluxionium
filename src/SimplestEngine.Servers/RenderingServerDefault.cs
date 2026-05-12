using SimplestEngine.Abi;

namespace SimplestEngine.Servers;

/// <summary>
/// Engine-owned rendering server with a struct-based RenderCommand stream.
/// Nodes push commands here; a backend (Veldrid, bgfx, ...) reads them via
/// <see cref="EnumerateItems"/> and submits the frame.
/// </summary>
public sealed class RenderingServerDefault : IRenderingServer
{
    private readonly RidAllocator<CanvasItemData> _items = new();
    private readonly RidAllocator<CanvasData> _canvases = new();
    private readonly IRenderingBackend _backend;
    private Color _clearColor = new(0.1f, 0.1f, 0.12f, 1f);

    public RenderingServerDefault(IRenderingBackend backend) { _backend = backend; }

    public Vector2i GetViewportSize() => _backend.GetSize();
    public void SetClearColor(Color color) { _clearColor = color; }

    public Rid CanvasItemCreate()
    {
        var data = new CanvasItemData();
        return _items.Allocate(data);
    }

    public void CanvasItemFree(Rid rid) => _items.Free(rid);

    public void CanvasItemSetParent(Rid item, Rid parent)
    {
        if (_items.TryGet(item, out var d) && d is not null) d.Parent = parent;
    }
    public void CanvasItemSetTransform(Rid item, Transform2D xform)
    {
        if (_items.TryGet(item, out var d) && d is not null) d.Transform = xform;
    }
    public void CanvasItemSetVisible(Rid item, bool visible)
    {
        if (_items.TryGet(item, out var d) && d is not null) d.Visible = visible;
    }
    public void CanvasItemSetZIndex(Rid item, int z)
    {
        if (_items.TryGet(item, out var d) && d is not null) d.ZIndex = z;
    }
    public void CanvasItemClear(Rid item)
    {
        if (_items.TryGet(item, out var d) && d is not null) d.Commands.Clear();
    }

    public void CanvasItemAddTextureRect(Rid item, Rect2 rect, Rid texture, Color modulate)
    {
        if (!_items.TryGet(item, out var d) || d is null) return;
        d.Commands.Add(new RenderCommand
        { Type = RenderCommandType.TextureRect, Rect = rect, Color = modulate, Texture = texture, SrcRect = new Rect2(0, 0, 1, 1) });
    }
    public void CanvasItemAddTextureRectRegion(Rid item, Rect2 rect, Rid texture, Rect2 srcRect, Color modulate)
    {
        if (!_items.TryGet(item, out var d) || d is null) return;
        d.Commands.Add(new RenderCommand
        { Type = RenderCommandType.TextureRect, Rect = rect, Color = modulate, Texture = texture, SrcRect = srcRect });
    }
    public void CanvasItemAddRect(Rid item, Rect2 rect, Color color)
    {
        if (!_items.TryGet(item, out var d) || d is null) return;
        d.Commands.Add(new RenderCommand { Type = RenderCommandType.Rect, Rect = rect, Color = color });
    }
    public void CanvasItemAddLine(Rid item, Vector2 from, Vector2 to, Color color, float width)
    {
        if (!_items.TryGet(item, out var d) || d is null) return;
        d.Commands.Add(new RenderCommand
        { Type = RenderCommandType.Line, Rect = new Rect2(from, to - from), Color = color, Width = width });
    }
    public void CanvasItemAddCircle(Rid item, Vector2 pos, float radius, Color color)
    {
        if (!_items.TryGet(item, out var d) || d is null) return;
        d.Commands.Add(new RenderCommand
        { Type = RenderCommandType.Circle, Rect = new Rect2(pos, new Vector2(radius, radius)), Color = color });
    }

    public Rid CanvasCreate() => _canvases.Allocate(new CanvasData());
    public void CanvasFree(Rid rid) => _canvases.Free(rid);

    public Rid TextureCreate(int width, int height, ReadOnlySpan<byte> rgbaData)
        => _backend.CreateTexture(width, height, rgbaData);
    public void TextureFree(Rid rid) => _backend.DestroyTexture(rid);
    public Vector2i TextureGetSize(Rid texture) => _backend.GetTextureSize(texture);
    public void TextureUpdate(Rid texture, int x, int y, int width, int height, ReadOnlySpan<byte> rgba)
        => _backend.UpdateTexture(texture, x, y, width, height, rgba);
    public void TextureSetFilter(Rid texture, bool nearest)
        => _backend.SetTextureFilter(texture, nearest);

    public void Frame()
    {
        _backend.BeginFrame(_clearColor);
        _backend.Submit(this);
        _backend.EndFrame();
    }

    public IEnumerable<(Rid rid, CanvasItemData data)> EnumerateItems()
    {
        foreach (var (rid, p) in _items.Enumerate())
            if (p.Visible)
                yield return (rid, p);
    }
}

public enum RenderCommandType : byte
{
    Rect,
    TextureRect,
    Line,
    Circle,
}

public struct RenderCommand
{
    public RenderCommandType Type;
    public Rect2 Rect;
    public Rect2 SrcRect;
    public Color Color;
    public Rid Texture;
    public float Width;
}

public sealed class CanvasItemData
{
    public Rid Parent;
    public Transform2D Transform = Transform2D.Identity;
    public bool Visible = true;
    public int ZIndex;
    public List<RenderCommand> Commands { get; } = new();
}

public sealed class CanvasData { /* per-canvas state placeholder */ }
