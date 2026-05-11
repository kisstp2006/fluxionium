namespace SimplestEngine.Abi;

/// <summary>Backend-agnostic 2D rendering server. Pandemonium parity (subset).</summary>
public interface IRenderingServer
{
    // canvas items (RID-handled draw nodes)
    Rid CanvasItemCreate();
    void CanvasItemFree(Rid rid);
    void CanvasItemSetParent(Rid item, Rid parent);
    void CanvasItemSetTransform(Rid item, Transform2D xform);
    void CanvasItemSetVisible(Rid item, bool visible);
    void CanvasItemSetZIndex(Rid item, int z);
    void CanvasItemClear(Rid item);

    // command stream (recorded into the item; flushed at frame end)
    void CanvasItemAddTextureRect(Rid item, Rect2 rect, Rid texture, Color modulate);
    void CanvasItemAddTextureRectRegion(Rid item, Rect2 rect, Rid texture, Rect2 srcRect, Color modulate);
    void CanvasItemAddRect(Rid item, Rect2 rect, Color color);
    void CanvasItemAddLine(Rid item, Vector2 from, Vector2 to, Color color, float width);
    void CanvasItemAddCircle(Rid item, Vector2 pos, float radius, Color color);

    // canvases (the screen / a render target)
    Rid CanvasCreate();
    void CanvasFree(Rid rid);

    // textures
    Rid TextureCreate(int width, int height, ReadOnlySpan<byte> rgbaData);
    void TextureFree(Rid rid);
    Vector2i TextureGetSize(Rid texture);

    // frame
    void Frame();
    void SetClearColor(Color color);
    Vector2i GetViewportSize();
}

/// <summary>Concrete backend (Veldrid, bgfx, WebGPU, ...). Hidden behind RenderingServer.</summary>
public interface IRenderingBackend
{
    void BeginFrame(Color clearColor);
    void EndFrame();
    void Resize(int width, int height);
    Vector2i GetSize();

    // bridges the engine command stream to GPU calls
    void Submit(IRenderingServer src);

    // texture upload (backing for RenderingServer.TextureCreate)
    Rid CreateTexture(int width, int height, ReadOnlySpan<byte> rgba);
    void DestroyTexture(Rid rid);
    Vector2i GetTextureSize(Rid rid);
}
