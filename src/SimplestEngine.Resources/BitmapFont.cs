using SimplestEngine.Abi;

namespace SimplestEngine.Resources;

/// <summary>
/// Angelcode BMFont-compatible bitmap font. Mirrors Pandemonium's
/// <c>BitmapFont</c> (see <c>scene/resources/font/font.h:124-224</c>): a fixed
/// set of pages (textures), per-codepoint <c>Character</c> records pointing into
/// a page, and a kerning table. No runtime rasterization — everything is
/// pre-baked in the <c>.fnt</c>.
/// </summary>
[GDClass("BitmapFont", "Font")]
public sealed class BitmapFont : SimplestEngine.Font
{
    public struct Character
    {
        public int PageIndex;   // index into Textures
        public Rect2 Rect;      // src pixel rect inside the page
        public Vector2 Offset;  // pen offset (xoffset, yoffset)
        public float Advance;   // xadvance
    }

    public int LineHeight { get; set; } = 0;
    public int BaseHeight { get; set; } = 0;
    public List<Texture2D> Textures { get; } = new();
    public Dictionary<int, Character> Chars { get; } = new();
    public Dictionary<(int, int), float> KerningPairs { get; } = new();

    public BitmapFont() { ResourceClass = StringName.Get("BitmapFont"); }

    public void AddTexture(Texture2D tex) => Textures.Add(tex);
    public void AddChar(int codepoint, int pageIdx, Rect2 rect, Vector2 offset, float advance)
        => Chars[codepoint] = new Character { PageIndex = pageIdx, Rect = rect, Offset = offset, Advance = advance };
    public void AddKerningPair(int a, int b, float amount)
        => KerningPairs[(a, b)] = amount;

    public override float GetHeight() => LineHeight;
    public override float GetAscent() => BaseHeight;
    public override float GetDescent() => Math.Max(0, LineHeight - BaseHeight);

    public override Vector2 GetCharSize(int codepoint, int next = 0)
    {
        if (!Chars.TryGetValue(codepoint, out var c)) return new Vector2(0, LineHeight);
        float adv = c.Advance;
        if (next != 0 && KerningPairs.TryGetValue((codepoint, next), out var k)) adv += k;
        return new Vector2(adv, LineHeight);
    }

    public override float DrawChar(IRenderingServer server, Rid canvasItem,
                                   Vector2 pos, int codepoint, int next, Color modulate)
    {
        if (!Chars.TryGetValue(codepoint, out var c)) return 0f;
        float adv = c.Advance;
        if (next != 0 && KerningPairs.TryGetValue((codepoint, next), out var k)) adv += k;
        if (c.PageIndex < 0 || c.PageIndex >= Textures.Count) return adv;
        var tex = Textures[c.PageIndex];
        // BMFont positions are baseline-anchored: pen.Y - Ascent + offset.Y =
        // top-left of the glyph rect. Match Pandemonium's draw_char order.
        float dx = MathF.Floor(pos.X + c.Offset.X);
        float dy = MathF.Floor(pos.Y - GetAscent() + c.Offset.Y);
        var dst = new Rect2(new Vector2(dx, dy), c.Rect.Size);
        server.CanvasItemAddTextureRectRegion(canvasItem, dst, tex.Rid, c.Rect, modulate);
        return adv;
    }
}

/// <summary>
/// Parses Angelcode BMFont text-format <c>.fnt</c> files into a
/// <see cref="BitmapFont"/>. Implementation parity goal: match Pandemonium's
/// <c>BitmapFont::create_from_fnt</c>.
/// </summary>
public static class BitmapFontLoader
{
    /// <summary>Parse the .fnt file at <paramref name="absPath"/>. Page textures
    /// listed in the file are resolved through <paramref name="loader"/> (so
    /// relative paths are read off-disk via the engine's resource pipeline).</summary>
    public static BitmapFont Load(string absPath, ResourceLoader loader)
    {
        var lines = File.ReadAllLines(absPath);
        var font = new BitmapFont { ResourcePath = absPath };
        var dir = Path.GetDirectoryName(absPath) ?? string.Empty;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;
            var parts = TokenizeBmFontLine(line);
            if (parts.Count == 0) continue;
            string kind = parts[0];
            var kv = ParseKv(parts);

            switch (kind)
            {
                case "common":
                    if (kv.TryGetValue("lineHeight", out var lh)) font.LineHeight = ParseInt(lh);
                    if (kv.TryGetValue("base", out var bs)) font.BaseHeight = ParseInt(bs);
                    break;

                case "page":
                {
                    // BMFont page lines: page id=0 file="atlas_0.png"
                    if (!kv.TryGetValue("file", out var file)) break;
                    var clean = file.Trim('"');
                    var rel = Path.IsPathRooted(clean) ? clean : Path.Combine(dir, clean);
                    var tex = loader.Load("res://" + Path.GetRelativePath(loader.ProjectRoot, rel).Replace('\\', '/'))
                              as Texture2D;
                    if (tex is not null) font.AddTexture(tex);
                    break;
                }

                case "char":
                {
                    int id = kv.TryGetValue("id", out var sid) ? ParseInt(sid) : -1;
                    int x = kv.TryGetValue("x", out var sx) ? ParseInt(sx) : 0;
                    int y = kv.TryGetValue("y", out var sy) ? ParseInt(sy) : 0;
                    int w = kv.TryGetValue("width", out var sw) ? ParseInt(sw) : 0;
                    int h = kv.TryGetValue("height", out var sh) ? ParseInt(sh) : 0;
                    int ox = kv.TryGetValue("xoffset", out var sox) ? ParseInt(sox) : 0;
                    int oy = kv.TryGetValue("yoffset", out var soy) ? ParseInt(soy) : 0;
                    int xa = kv.TryGetValue("xadvance", out var sxa) ? ParseInt(sxa) : 0;
                    int page = kv.TryGetValue("page", out var sp) ? ParseInt(sp) : 0;
                    if (id < 0) break;
                    font.AddChar(id, page, new Rect2(new Vector2(x, y), new Vector2(w, h)),
                                 new Vector2(ox, oy), xa);
                    break;
                }

                case "kerning":
                {
                    int first = kv.TryGetValue("first", out var sf) ? ParseInt(sf) : 0;
                    int second = kv.TryGetValue("second", out var ss) ? ParseInt(ss) : 0;
                    int amount = kv.TryGetValue("amount", out var sa) ? ParseInt(sa) : 0;
                    font.AddKerningPair(first, second, amount);
                    break;
                }
            }
        }
        return font;
    }

    private static List<string> TokenizeBmFontLine(string line)
    {
        // BMFont tokens are whitespace-separated, but quoted strings may contain spaces.
        var list = new List<string>();
        int i = 0;
        while (i < line.Length)
        {
            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            if (i >= line.Length) break;
            int start = i;
            if (line[i] == '"')
            {
                int end = line.IndexOf('"', i + 1);
                if (end < 0) end = line.Length;
                list.Add(line.Substring(i, end - i + 1));
                i = end + 1;
            }
            else
            {
                while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
                list.Add(line.Substring(start, i - start));
            }
        }
        return list;
    }

    private static Dictionary<string, string> ParseKv(List<string> parts)
    {
        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < parts.Count; i++)
        {
            int eq = parts[i].IndexOf('=');
            if (eq <= 0) continue;
            kv[parts[i][..eq]] = parts[i][(eq + 1)..];
        }
        return kv;
    }

    private static int ParseInt(string s)
        => int.TryParse(s.Trim('"'), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
}
