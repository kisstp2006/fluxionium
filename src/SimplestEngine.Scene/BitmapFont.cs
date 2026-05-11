namespace SimplestEngine;

/// <summary>
/// Built-in 5×7 ASCII bitmap font. Used by <see cref="Label"/> as the v1 fallback
/// renderer so projects can show text without any imported font resource.
///
/// Each glyph is 7 rows × 5 bits; high bit (1 &lt;&lt; 4) is the leftmost column.
/// A real Theme / DynamicFont (FreeType) pipeline lands in M6.5. The constants
/// here are intentionally cheap so a label of ~50 characters costs ~750 rects
/// per frame, well below the renderer's 4096-quad batch.
/// </summary>
public static class BitmapFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;
    public const int Advance = GlyphWidth + 1;
    public const int LineHeight = GlyphHeight + 2;

    private static readonly Dictionary<char, byte[]> _glyphs = BuildGlyphs();

    /// <summary>True if the font has a glyph for the given character.</summary>
    public static bool HasGlyph(char c) => _glyphs.ContainsKey(NormalizeKey(c));

    /// <summary>Width in pixels of <paramref name="text"/> at 1× scale (glyph + spacing).</summary>
    public static int MeasureWidth(string text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Length * Advance - 1;

    /// <summary>
    /// Push the rectangles representing <paramref name="text"/> to the canvas item, scaled by
    /// <paramref name="pixelScale"/>. Each set pixel becomes one filled rect; the renderer
    /// batches them into a single draw call.
    /// </summary>
    public static void Emit(
        Abi.IRenderingServer server,
        Rid canvasItem,
        string text,
        Vector2 origin,
        Color color,
        float pixelScale = 2f)
    {
        if (string.IsNullOrEmpty(text)) return;
        var pen = origin;
        var step = new Vector2(Advance * pixelScale, 0f);

        foreach (var raw in text)
        {
            var key = NormalizeKey(raw);
            if (raw == '\n')
            {
                pen = new Vector2(origin.X, pen.Y + LineHeight * pixelScale);
                continue;
            }
            if (_glyphs.TryGetValue(key, out var rows))
                EmitGlyph(server, canvasItem, rows, pen, color, pixelScale);
            pen += step;
        }
    }

    private static void EmitGlyph(
        Abi.IRenderingServer server,
        Rid canvasItem,
        byte[] rows,
        Vector2 origin,
        Color color,
        float pixelScale)
    {
        var size = new Vector2(pixelScale, pixelScale);
        for (int y = 0; y < GlyphHeight && y < rows.Length; y++)
        {
            var bits = rows[y];
            if (bits == 0) continue;
            for (int x = 0; x < GlyphWidth; x++)
            {
                if ((bits & (1 << (GlyphWidth - 1 - x))) == 0) continue;
                var pos = new Vector2(origin.X + x * pixelScale, origin.Y + y * pixelScale);
                server.CanvasItemAddRect(canvasItem, new Rect2(pos, size), color);
            }
        }
    }

    private static char NormalizeKey(char c) =>
        c >= 'a' && c <= 'z' ? (char)(c - 32) : c;

    private static Dictionary<char, byte[]> BuildGlyphs()
    {
        // 7-row glyphs; binary literals read top-to-bottom, left-to-right.
        // Restricted to characters needed for typical demo strings - everything
        // else falls back to "blank" via the dictionary miss path.
        return new Dictionary<char, byte[]>
        {
            [' '] = G(0,0,0,0,0,0,0),
            ['!'] = G(0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00000, 0b00100),
            ['"'] = G(0b01010, 0b01010, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000),
            ['+'] = G(0b00000, 0b00100, 0b00100, 0b11111, 0b00100, 0b00100, 0b00000),
            [','] = G(0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00100, 0b01000),
            ['-'] = G(0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000),
            ['.'] = G(0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00100),
            ['/'] = G(0b00001, 0b00010, 0b00010, 0b00100, 0b01000, 0b01000, 0b10000),
            [':'] = G(0b00000, 0b00100, 0b00000, 0b00000, 0b00100, 0b00000, 0b00000),
            ['?'] = G(0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b00000, 0b00100),
            ['('] = G(0b00010, 0b00100, 0b01000, 0b01000, 0b01000, 0b00100, 0b00010),
            [')'] = G(0b01000, 0b00100, 0b00010, 0b00010, 0b00010, 0b00100, 0b01000),

            ['0'] = G(0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110),
            ['1'] = G(0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110),
            ['2'] = G(0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111),
            ['3'] = G(0b11110, 0b00001, 0b00001, 0b01110, 0b00001, 0b00001, 0b11110),
            ['4'] = G(0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010),
            ['5'] = G(0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110),
            ['6'] = G(0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110),
            ['7'] = G(0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000),
            ['8'] = G(0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110),
            ['9'] = G(0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100),

            ['A'] = G(0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001),
            ['B'] = G(0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110),
            ['C'] = G(0b01110, 0b10001, 0b10000, 0b10000, 0b10000, 0b10001, 0b01110),
            ['D'] = G(0b11110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b11110),
            ['E'] = G(0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111),
            ['F'] = G(0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000),
            ['G'] = G(0b01110, 0b10001, 0b10000, 0b10111, 0b10001, 0b10001, 0b01110),
            ['H'] = G(0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001),
            ['I'] = G(0b01110, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110),
            ['J'] = G(0b00111, 0b00010, 0b00010, 0b00010, 0b00010, 0b10010, 0b01100),
            ['K'] = G(0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001),
            ['L'] = G(0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111),
            ['M'] = G(0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001),
            ['N'] = G(0b10001, 0b10001, 0b11001, 0b10101, 0b10011, 0b10001, 0b10001),
            ['O'] = G(0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110),
            ['P'] = G(0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000),
            ['Q'] = G(0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101),
            ['R'] = G(0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001),
            ['S'] = G(0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110),
            ['T'] = G(0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100),
            ['U'] = G(0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110),
            ['V'] = G(0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100),
            ['W'] = G(0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b10101, 0b01010),
            ['X'] = G(0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001),
            ['Y'] = G(0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100),
            ['Z'] = G(0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111),
        };
    }

    private static byte[] G(int r0, int r1, int r2, int r3, int r4, int r5, int r6) =>
        new[] { (byte)r0, (byte)r1, (byte)r2, (byte)r3, (byte)r4, (byte)r5, (byte)r6 };
}
