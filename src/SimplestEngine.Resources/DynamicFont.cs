using SimplestEngine.Abi;
using StbTrueTypeSharp;

namespace SimplestEngine.Resources;

/// <summary>Hinting modes mirrored from Pandemonium's <c>DynamicFontData.Hinting</c>.</summary>
public enum DynamicFontHinting { None, Light, Normal }

/// <summary>
/// Raw TTF/OTF byte holder. Equivalent to Pandemonium's <c>DynamicFontData</c>:
/// stores the font file in memory and lazily creates one
/// <see cref="DynamicFontAtSize"/> per requested (size, outline, filter) combo,
/// so 10 Labels at the same size share a single rasterized atlas.
/// </summary>
[GDClass("DynamicFontData", "Resource")]
public sealed class DynamicFontData : SimplestEngine.Resource
{
    /// <summary>Stable cache key — Pandemonium calls this <c>CacheID</c>.</summary>
    public readonly record struct CacheId(short Size, byte OutlineSize, bool Mipmaps, bool Filter);

    internal readonly byte[] _fontData;
    public DynamicFontHinting Hinting { get; set; } = DynamicFontHinting.Normal;
    public bool Antialiased { get; set; } = true;

    private readonly Dictionary<CacheId, DynamicFontAtSize> _sizeCache = new();

    public DynamicFontData(byte[] fontData)
    {
        _fontData = fontData ?? throw new ArgumentNullException(nameof(fontData));
        ResourceClass = StringName.Get("DynamicFontData");
    }

    /// <summary>Resolve (or build, on first hit) the rasterized cache for a
    /// specific size + outline + filter combination.</summary>
    public DynamicFontAtSize GetAtSize(CacheId id)
    {
        if (_sizeCache.TryGetValue(id, out var existing)) return existing;
        var atSize = new DynamicFontAtSize(this, id);
        _sizeCache[id] = atSize;
        return atSize;
    }
}

/// <summary>
/// One concrete rasterized configuration of a TTF — keyed by size + outline +
/// filter. Holds the stb font info, the glyph cache, and one or more atlas
/// pages. Mirrors Pandemonium's <c>DynamicFontAtSize</c>.
/// </summary>
public sealed class DynamicFontAtSize
{
    /// <summary>Per-codepoint cached entry. Layout mirrors Pandemonium's
    /// <c>Character</c> (see <c>dynamic_font.h:220</c>) so the draw path stays a
    /// straight rect blit after the first rasterization.</summary>
    public struct CachedChar
    {
        public bool Found;
        public bool Empty;            // whitespace / no bitmap (e.g. space)
        public int TextureIdx;        // index into _atlases
        public Rect2 Rect;            // dst rect relative to baseline pen
        public Rect2 RectUv;          // src rect in atlas pixels (post-pack)
        public int HAlign, VAlign;    // stb xoff/yoff, integer
        public float Advance;         // pen-X advance in pixels (kerning-free)
    }

    private const int AtlasSize = 1024;
    // 1-pixel transparent border per glyph keeps NEAREST sampling from bleeding
    // neighbor glyph pixels onto a glyph's right/bottom edge.
    private const int GlyphPadding = 1;

    public readonly DynamicFontData Owner;
    public readonly DynamicFontData.CacheId Id;

    private readonly StbTrueType.stbtt_fontinfo _info;
    private readonly float _scale;
    public int Ascent { get; }
    public int Descent { get; }
    public int LineGap { get; }
    public int Height => Ascent + Descent + LineGap;

    private readonly Dictionary<int, CachedChar> _charMap = new();
    private readonly List<ShelfPackAtlas> _atlases = new();

    public DynamicFontAtSize(DynamicFontData owner, DynamicFontData.CacheId id)
    {
        Owner = owner;
        Id = id;
        _info = StbTrueType.CreateFont(owner._fontData, 0)
            ?? throw new InvalidOperationException("stbtt CreateFont failed (bad TTF data)");
        _scale = StbTrueType.stbtt_ScaleForPixelHeight(_info, id.Size);
        int a, d, lg;
        unsafe { StbTrueType.stbtt_GetFontVMetrics(_info, &a, &d, &lg); }
        // Snap metrics to integer pixels — pixel-perfect rule. Descent is negative
        // in stb (below baseline).
        Ascent = (int)MathF.Round(a * _scale);
        Descent = (int)MathF.Round(-d * _scale);
        LineGap = (int)MathF.Round(lg * _scale);
    }

    /// <summary>Kerning advance between <paramref name="cp"/> and
    /// <paramref name="next"/>, in pixels (already scaled).</summary>
    public float GetKerning(int cp, int next)
    {
        if (next == 0) return 0f;
        int k = StbTrueType.stbtt_GetCodepointKernAdvance(_info, cp, next);
        return k * _scale;
    }

    /// <summary>Cache lookup — rasterizes on first hit. Returns by ref into the
    /// internal dictionary; callers must not retain the ref past another
    /// <c>Get</c>.</summary>
    public CachedChar GetChar(int cp)
    {
        if (_charMap.TryGetValue(cp, out var existing)) return existing;
        var made = UpdateChar(cp);
        _charMap[cp] = made;
        return made;
    }

    /// <summary>Flush every atlas page's pending uploads. Called once per draw
    /// pass before rect-batching the glyphs.</summary>
    public void FlushAtlases(IRenderingServer server)
    {
        foreach (var a in _atlases) a.Flush(server);
    }

    /// <summary>Get the atlas RID for a given texture index (0-based).</summary>
    public Rid GetAtlasRid(int idx) =>
        idx >= 0 && idx < _atlases.Count ? _atlases[idx].Texture : default;

    /// <summary>Get the atlas pixel size (1024 by default).</summary>
    public int GetAtlasSize() => AtlasSize;

    public int AtlasCount => _atlases.Count;
    public float GetAtlasFill(int idx) =>
        idx >= 0 && idx < _atlases.Count ? _atlases[idx].FillRatio() : 0f;

    private CachedChar UpdateChar(int cp)
    {
        // Glyph horizontal metrics (advance + LSB) — advance is needed even for
        // whitespace glyphs that have no bitmap.
        int advanceWidth, lsb;
        unsafe { StbTrueType.stbtt_GetCodepointHMetrics(_info, cp, &advanceWidth, &lsb); }
        float advancePx = advanceWidth * _scale;

        int w = 0, h = 0, xoff = 0, yoff = 0;
        byte[]? gray = null;
        unsafe
        {
            byte* bmp = StbTrueType.stbtt_GetCodepointBitmap(
                _info, _scale, _scale, cp, &w, &h, &xoff, &yoff);
            if (bmp != null && w > 0 && h > 0)
            {
                gray = new byte[w * h];
                fixed (byte* dst = gray)
                {
                    Buffer.MemoryCopy(bmp, dst, gray.Length, gray.Length);
                }
                StbTrueType.stbtt_FreeBitmap(bmp, null);
            }
            else if (bmp != null)
            {
                StbTrueType.stbtt_FreeBitmap(bmp, null);
            }
        }

        if (gray is null || w <= 0 || h <= 0)
        {
            // Whitespace / undefined glyph: still cache the advance so the pen
            // moves forward.
            return new CachedChar
            {
                Found = true,
                Empty = true,
                TextureIdx = 0,
                Advance = advancePx,
                HAlign = xoff,
                VAlign = yoff,
            };
        }

        int paddedW = w + GlyphPadding * 2;
        int paddedH = h + GlyphPadding * 2;
        if (!FindTexturePos(paddedW, paddedH, out int atlasIdx, out int px, out int py))
        {
            // Atlas exhausted (very rare for 14 px; would need ~thousands of
            // glyphs). Fall back to "found but empty" so the missing-glyph case
            // is still drawable as whitespace.
            return new CachedChar { Found = true, Empty = true, Advance = advancePx };
        }

        // Dilate single-channel src into RGBA8 (R=G=B=255, A=src). One alloc per
        // glyph; tiny — for 14 px glyphs ~120 bytes.
        var atlas = _atlases[atlasIdx];
        var rgba = new byte[paddedW * paddedH * 4];
        for (int yy = 0; yy < h; yy++)
        {
            for (int xx = 0; xx < w; xx++)
            {
                int dst = ((yy + GlyphPadding) * paddedW + (xx + GlyphPadding)) * 4;
                byte a = gray[yy * w + xx];
                rgba[dst + 0] = 255;
                rgba[dst + 1] = 255;
                rgba[dst + 2] = 255;
                rgba[dst + 3] = a;
            }
        }

        // Mirror into the CPU atlas buffer and widen the atlas dirty rect so the
        // next Flush() partial-blits this glyph region (including the padding)
        // to the GPU.
        int stride = AtlasSize * 4;
        for (int yy = 0; yy < paddedH; yy++)
        {
            int rowSrc = yy * paddedW * 4;
            int rowDst = (py + yy) * stride + px * 4;
            Buffer.BlockCopy(rgba, rowSrc, atlas.Pixels, rowDst, paddedW * 4);
        }
        atlas.MarkDirty(px, py, paddedW, paddedH);

        var c = new CachedChar
        {
            Found = true,
            Empty = false,
            TextureIdx = atlasIdx,
            // Visible glyph region in atlas (skip the transparent padding).
            RectUv = new Rect2(
                new Vector2(px + GlyphPadding, py + GlyphPadding),
                new Vector2(w, h)),
            // Rect is relative to the baseline pen. stb's yoff is already
            // negative for the typical case (glyph mostly above baseline).
            Rect = new Rect2(new Vector2(xoff, yoff), new Vector2(w, h)),
            HAlign = xoff,
            VAlign = yoff,
            Advance = advancePx,
        };
        return c;
    }

    private bool FindTexturePos(int w, int h, out int atlasIdx, out int x, out int y)
    {
        for (int i = 0; i < _atlases.Count; i++)
        {
            if (_atlases[i].TryPackRect(w, h, out x, out y))
            {
                atlasIdx = i;
                return true;
            }
        }
        // No existing atlas fits — open a new one.
        var atlas = new ShelfPackAtlas(AtlasSize);
        if (atlas.TryPackRect(w, h, out x, out y))
        {
            _atlases.Add(atlas);
            atlasIdx = _atlases.Count - 1;
            Console.WriteLine(
                $"[font] DynamicFontAtSize(size={Id.Size}, outline={Id.OutlineSize}, " +
                $"filter={Id.Filter}) atlas {AtlasSize}x{AtlasSize} created (page {atlasIdx})");
            return true;
        }
        atlasIdx = -1; x = 0; y = 0;
        return false;
    }
}

/// <summary>
/// User-facing dynamic font resource. What <c>Theme</c> and <see cref="Label"/>
/// hold. Holds a <see cref="DynamicFontData"/> plus size / outline / filter /
/// spacing knobs, mirroring Pandemonium's <c>DynamicFont</c>.
/// </summary>
[GDClass("DynamicFont", "Font")]
public sealed class DynamicFont : SimplestEngine.Font
{
    private DynamicFontData _fontData;
    private DynamicFontAtSize _atSize = null!;
    private int _size = 16;
    private int _outlineSize = 0;
    private bool _useFilter = false;   // pixel-perfect default
    private bool _useMipmaps = false;

    public Color OutlineColor { get; set; } = new Color(0, 0, 0, 1);
    public int SpacingTop { get; set; }
    public int SpacingBottom { get; set; }
    public int SpacingChar { get; set; }
    public int SpacingSpace { get; set; }

    public DynamicFont(DynamicFontData data, int size = 16)
    {
        _fontData = data;
        _size = size;
        ResourceClass = StringName.Get("DynamicFont");
        ReloadAtSize();
    }

    public DynamicFontData FontData
    {
        get => _fontData;
        set { _fontData = value; ReloadAtSize(); }
    }

    public int Size
    {
        get => _size;
        set { if (value != _size) { _size = Math.Max(1, value); ReloadAtSize(); } }
    }

    public int OutlineSize
    {
        get => _outlineSize;
        set { if (value != _outlineSize) { _outlineSize = Math.Max(0, value); ReloadAtSize(); } }
    }

    public bool UseFilter
    {
        get => _useFilter;
        set { if (value != _useFilter) { _useFilter = value; ReloadAtSize(); } }
    }

    public bool UseMipmaps
    {
        get => _useMipmaps;
        set { if (value != _useMipmaps) { _useMipmaps = value; ReloadAtSize(); } }
    }

    private void ReloadAtSize()
    {
        var id = new DynamicFontData.CacheId(
            (short)_size, (byte)_outlineSize, _useMipmaps, _useFilter);
        _atSize = _fontData.GetAtSize(id);
    }

    public override float GetHeight() => _atSize.Height + SpacingTop + SpacingBottom;
    public override float GetAscent() => _atSize.Ascent + SpacingTop;
    public override float GetDescent() => _atSize.Descent + SpacingBottom;

    public override Vector2 GetCharSize(int codepoint, int next = 0)
    {
        var c = _atSize.GetChar(codepoint);
        float adv = c.Advance + SpacingChar;
        if (codepoint == ' ') adv += SpacingSpace;
        if (next != 0) adv += _atSize.GetKerning(codepoint, next);
        return new Vector2(adv, _atSize.Height);
    }

    public override float DrawChar(IRenderingServer server, Rid canvasItem,
                                   Vector2 pos, int codepoint, int next, Color modulate)
    {
        var c = _atSize.GetChar(codepoint);
        float adv = c.Advance + SpacingChar;
        if (codepoint == ' ') adv += SpacingSpace;
        if (next != 0) adv += _atSize.GetKerning(codepoint, next);
        if (c.Empty || !c.Found) return adv;

        // Lazy GPU sync — first hit creates the texture from the current CPU
        // buffer, subsequent hits partial-blit the dirty rect (the strips packed
        // during UpdateChar above and on any earlier draw this frame).
        _atSize.FlushAtlases(server);
        var atlasRid = _atSize.GetAtlasRid(c.TextureIdx);
        if (!atlasRid.IsValid) return adv;

        // Pixel-perfect rule: floor the destination pen so the dst rect lands on
        // integer pixels, matching the NEAREST sampler on the atlas.
        float dx = MathF.Floor(pos.X + c.HAlign);
        float dy = MathF.Floor(pos.Y + c.VAlign);
        var dst = new Rect2(new Vector2(dx, dy), c.Rect.Size);
        server.CanvasItemAddTextureRectRegion(canvasItem, dst, atlasRid, c.RectUv, modulate);
        return adv;
    }

    /// <summary>Force-rasterize a string's glyphs and upload pending atlas
    /// updates. Useful right after loading a scene so the first draw is
    /// jank-free.</summary>
    public void Preload(IRenderingServer server, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var ch in text) _atSize.GetChar(ch);
        _atSize.FlushAtlases(server);
    }
}
