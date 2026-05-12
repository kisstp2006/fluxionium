using SimplestEngine.Abi;

namespace SimplestEngine;

/// <summary>
/// Tiny 5×7 ASCII bitmap font built directly into the engine assembly. Used as
/// the very last fallback when <see cref="Font.DefaultFontFactory"/> is unset
/// (e.g. a runtime that didn't link SimplestEngine.Resources or fails to load
/// the embedded TTF). Implements the abstract <see cref="Font"/> API by
/// emitting one filled <c>Rect</c> per lit dot — no atlas, no TTF dependency.
/// Mirrors Pandemonium's emergency bitmap fallback semantics.
/// </summary>
public sealed class EmbeddedBitmapFont : Font
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;
    public const int Advance = GlyphWidth + 1;
    public const int LineHeight = GlyphHeight + 2;

    /// <summary>Process-wide singleton — there is no per-instance state beyond
    /// the glyph data which is static and immutable.</summary>
    public static readonly EmbeddedBitmapFont Instance = new();

    private static readonly Dictionary<char, byte[]> _glyphs = BuildGlyphs();

    private EmbeddedBitmapFont() { ResourceClass = StringName.Get("EmbeddedBitmapFont"); }

    public override float GetHeight() => LineHeight;
    public override float GetAscent() => GlyphHeight;
    public override float GetDescent() => LineHeight - GlyphHeight;

    public override Vector2 GetCharSize(int codepoint, int next = 0) =>
        new(Advance, LineHeight);

    public override float DrawChar(IRenderingServer server, Rid canvasItem,
                                   Vector2 pos, int codepoint, int next, Color modulate)
    {
        char key = NormalizeKey((char)codepoint);
        if (!_glyphs.TryGetValue(key, out var rows)) return Advance;
        // Pen is at baseline; the 5x7 strip lives ascent pixels above it.
        var origin = new Vector2(pos.X, pos.Y - GlyphHeight);
        var pixelSize = new Vector2(1, 1);
        for (int y = 0; y < GlyphHeight && y < rows.Length; y++)
        {
            int bits = rows[y];
            if (bits == 0) continue;
            for (int x = 0; x < GlyphWidth; x++)
            {
                if ((bits & (1 << (GlyphWidth - 1 - x))) == 0) continue;
                server.CanvasItemAddRect(canvasItem,
                    new Rect2(new Vector2(origin.X + x, origin.Y + y), pixelSize),
                    modulate);
            }
        }
        return Advance;
    }

    private static char NormalizeKey(char c) =>
        c >= 'a' && c <= 'z' ? (char)(c - 32) : c;

    private static Dictionary<char, byte[]> BuildGlyphs()
    {
        // 7-row glyphs; literals read top-to-bottom, left-to-right. Matches the
        // legacy BitmapFont data exactly.
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
