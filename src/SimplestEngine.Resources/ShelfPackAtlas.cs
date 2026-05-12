using SimplestEngine.Abi;

namespace SimplestEngine.Resources;

/// <summary>
/// One backing texture for a glyph cache. Direct port of Pandemonium's
/// <c>ShelfPackTexture</c> + <c>Shelf</c> (see
/// <c>scene/resources/font/dynamic_font.h</c>):
/// rows ("shelves") are allocated top-down; each new glyph picks the shelf with
/// the smallest vertical waste (<c>(shelf.h - glyph.h) * glyph.w</c>), or opens a
/// fresh shelf below the last one. The pixel buffer mirrors the GPU texture so we
/// can do partial blits via <see cref="IRenderingServer.TextureUpdate"/>.
/// </summary>
internal sealed class ShelfPackAtlas
{
    private struct Shelf
    {
        public int X;       // pen X inside the shelf
        public int Y;       // top of the shelf
        public int Width;   // remaining usable width
        public int Height;  // shelf height
        public Shelf(int x, int y, int w, int h) { X = x; Y = y; Width = w; Height = h; }
    }

    public readonly int Size;
    /// <summary>RGBA8 mirror; row stride = <see cref="Size"/> * 4.</summary>
    public readonly byte[] Pixels;
    /// <summary>Server-side RID. <c>default</c> until <see cref="Flush"/>
    /// uploads.</summary>
    public Rid Texture;

    private readonly List<Shelf> _shelves = new();
    // Dirty rect (inclusive bounds). Empty when MinX > MaxX.
    private int _dirtyMinX = int.MaxValue, _dirtyMinY = int.MaxValue;
    private int _dirtyMaxX = -1, _dirtyMaxY = -1;
    /// <summary>True iff at least one glyph has been packed since the last
    /// <see cref="Flush"/> (or since creation).</summary>
    public bool Dirty => _dirtyMaxX >= _dirtyMinX;

    public ShelfPackAtlas(int size = 1024)
    {
        Size = size;
        Pixels = new byte[size * size * 4];
    }

    /// <summary>Mark a rectangle as needing re-upload to the GPU. Called from
    /// the rasterizer after writing pixels into <see cref="Pixels"/>.</summary>
    public void MarkDirty(int x, int y, int w, int h)
    {
        if (w <= 0 || h <= 0) return;
        if (x < _dirtyMinX) _dirtyMinX = x;
        if (y < _dirtyMinY) _dirtyMinY = y;
        if (x + w - 1 > _dirtyMaxX) _dirtyMaxX = x + w - 1;
        if (y + h - 1 > _dirtyMaxY) _dirtyMaxY = y + h - 1;
    }

    /// <summary>Lazy GPU sync. On first call creates the texture; on subsequent
    /// calls partial-blits just the dirty rectangle. No-op when clean.</summary>
    public void Flush(IRenderingServer server)
    {
        if (!Texture.IsValid)
        {
            Texture = server.TextureCreate(Size, Size, Pixels);
            // Atlases never need bilinear — integer pen + integer source rect
            // must round-trip 1:1 with screen pixels.
            server.TextureSetFilter(Texture, nearest: true);
            ResetDirty();
            return;
        }
        if (!Dirty) return;
        int x = _dirtyMinX;
        int y = _dirtyMinY;
        int w = _dirtyMaxX - _dirtyMinX + 1;
        int h = _dirtyMaxY - _dirtyMinY + 1;
        // Copy the dirty sub-rect into a tightly packed buffer (UpdateTexture
        // wants contiguous rows).
        var sub = new byte[w * h * 4];
        int stride = Size * 4;
        for (int yy = 0; yy < h; yy++)
        {
            Buffer.BlockCopy(Pixels, (y + yy) * stride + x * 4,
                             sub, yy * w * 4, w * 4);
        }
        server.TextureUpdate(Texture, x, y, w, h, sub);
        ResetDirty();
    }

    private void ResetDirty()
    {
        _dirtyMinX = int.MaxValue; _dirtyMinY = int.MaxValue;
        _dirtyMaxX = -1; _dirtyMaxY = -1;
    }

    /// <summary>Try to place a (<paramref name="w"/> × <paramref name="h"/>)
    /// glyph. Returns true on success and writes the top-left pixel coords.
    /// Algorithm: best-fit by waste over existing shelves; fallback open a new
    /// shelf below the last one.</summary>
    public bool TryPackRect(int w, int h, out int x, out int y)
    {
        x = 0; y = 0;
        if (w <= 0 || h <= 0 || w > Size) return false;

        int shelfTopAcc = 0;
        int bestIdx = -1;
        int bestWaste = int.MaxValue;
        for (int i = 0; i < _shelves.Count; i++)
        {
            var s = _shelves[i];
            shelfTopAcc += s.Height;
            if (w > s.Width) continue;
            if (h == s.Height)
            {
                // Exact-height fit — Pandemonium short-circuits to this shelf.
                x = s.X; y = s.Y;
                s.X += w; s.Width -= w;
                _shelves[i] = s;
                return true;
            }
            if (h > s.Height) continue;
            int waste = (s.Height - h) * w;
            if (waste < bestWaste) { bestWaste = waste; bestIdx = i; }
        }
        if (bestIdx >= 0)
        {
            var s = _shelves[bestIdx];
            x = s.X; y = s.Y;
            s.X += w; s.Width -= w;
            _shelves[bestIdx] = s;
            return true;
        }
        // No existing shelf fits — open a new one.
        if (h <= Size - shelfTopAcc)
        {
            var shelf = new Shelf(0, shelfTopAcc, Size, h);
            x = shelf.X; y = shelf.Y;
            shelf.X += w; shelf.Width -= w;
            _shelves.Add(shelf);
            return true;
        }
        return false;
    }

    /// <summary>Approximate fill ratio for diagnostics (sum of shelf heights / Size).</summary>
    public float FillRatio()
    {
        int sum = 0;
        foreach (var s in _shelves) sum += s.Height;
        return (float)sum / Size;
    }
}
