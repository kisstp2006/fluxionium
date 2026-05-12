using SimplestEngine.Abi;

namespace SimplestEngine;

/// <summary>
/// Abstract font resource. Mirrors Pandemonium's
/// <c>scene/resources/font/font.h</c> <c>Font</c>: a thin polymorphic API every
/// font backend (bitmap, dynamic/TTF, .fnt) implements. Controls like
/// <see cref="Label"/> draw through <see cref="Draw"/> — they never know which
/// concrete font is on the other end.
/// </summary>
[GDClass("Font", "Resource")]
public abstract class Font : Resource
{
    /// <summary>
    /// Hook for the default font supplier. SimplestEngine.Resources sets this at
    /// boot to <c>EngineDefaults.GetDefaultFont</c>; Scene cannot reference
    /// Resources (the dependency is the other way around) so we wire it up via a
    /// static delegate. Returns null when no font system is available — Label
    /// then falls back to <see cref="EmbeddedBitmapFont"/>.
    /// </summary>
    public static Func<int, Font?>? DefaultFontFactory { get; set; }

    /// <summary>Resolve the engine default font at <paramref name="size"/>,
    /// using <see cref="DefaultFontFactory"/> if registered, otherwise the
    /// last-resort 5×7 <see cref="EmbeddedBitmapFont"/>.</summary>
    public static Font GetDefault(int size = 14)
        => DefaultFontFactory?.Invoke(size) ?? EmbeddedBitmapFont.Instance;

    protected Font() { ResourceClass = StringName.Get("Font"); }

    /// <summary>Total line height in pixels (ascent + descent + line gap).</summary>
    public abstract float GetHeight();
    /// <summary>Pixels from baseline to top of the tallest glyph.</summary>
    public abstract float GetAscent();
    /// <summary>Pixels from baseline to bottom of the lowest descender.</summary>
    public abstract float GetDescent();

    /// <summary>Advance + bbox of a single codepoint. <paramref name="next"/>
    /// activates pair kerning; pass 0 for none.</summary>
    public abstract Vector2 GetCharSize(int codepoint, int next = 0);

    /// <summary>Push the rasterized quad for <paramref name="codepoint"/> at
    /// <paramref name="pos"/> (pen baseline position) onto the canvas item.
    /// Returns the horizontal advance in pixels (kerning-aware).</summary>
    public abstract float DrawChar(
        IRenderingServer server, Rid canvasItem,
        Vector2 pos, int codepoint, int next, Color modulate);

    /// <summary>Total bounding size of a rendered string (max ascent, accumulated
    /// advance). Matches the Pandemonium semantics used by Label autosizing.</summary>
    public Vector2 GetStringSize(string text)
    {
        if (string.IsNullOrEmpty(text)) return Vector2.Zero;
        float w = 0f;
        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            int next = i + 1 < text.Length ? text[i + 1] : 0;
            w += GetCharSize(cp, next).X;
        }
        return new Vector2(w, GetHeight());
    }

    /// <summary>Render <paramref name="text"/> with the baseline aligned to
    /// <paramref name="pos"/> + ascent — i.e. the same anchor Pandemonium's
    /// <c>Font::draw</c> uses (top-left of the visible text box).</summary>
    public void Draw(IRenderingServer server, Rid canvasItem, Vector2 pos,
                     string text, Color modulate, int clipW = -1)
    {
        if (string.IsNullOrEmpty(text) || server is null) return;
        float ascent = GetAscent();
        // Baseline lives ascent pixels below the requested top-left.
        float penX = pos.X;
        float penY = pos.Y + ascent;
        float xStart = penX;
        for (int i = 0; i < text.Length; i++)
        {
            int cp = text[i];
            if (cp == '\n')
            {
                penX = xStart;
                penY += GetHeight();
                continue;
            }
            int next = i + 1 < text.Length ? text[i + 1] : 0;
            float adv = DrawChar(server, canvasItem, new Vector2(penX, penY), cp, next, modulate);
            penX += adv;
            if (clipW > 0 && penX - xStart > clipW) break;
        }
    }
}
