using System.Reflection;

namespace SimplestEngine.Resources;

/// <summary>
/// One-stop helper for engine-supplied fallback resources. Currently the
/// default font (Noto Sans Regular, OFL, embedded in this assembly). Mirrors the
/// role Pandemonium's <c>default_theme/font_lodpi.inc</c> plays: every Control
/// has *something* to render with even when no project font is assigned.
/// </summary>
public static class EngineDefaults
{
    private const string EmbeddedFontResource = "SimplestEngine.Resources.NotoSans-Regular.ttf";
    // Pandemonium's _lodpi_font_height is 14 (see scene/resources/default_theme/font_lodpi.inc).
    public const int DefaultFontSize = 14;

    private static DynamicFontData? _cachedData;
    private static readonly Dictionary<int, DynamicFont> _fontPool = new();

    /// <summary>Get the embedded Noto Sans default at the requested pixel size.
    /// One <see cref="DynamicFont"/> instance per size, shared across the
    /// project (so e.g. all 14 px labels share the same atlas).</summary>
    public static DynamicFont GetDefaultFont(int size = DefaultFontSize)
    {
        if (_fontPool.TryGetValue(size, out var existing)) return existing;
        var data = LoadDefaultFontData();
        var font = new DynamicFont(data, size);
        _fontPool[size] = font;
        return font;
    }

    /// <summary>Lazy-load the embedded TTF once, then reuse it across all
    /// <c>GetDefaultFont</c> sizes (each one is a different
    /// <see cref="DynamicFontAtSize"/> on the same data).</summary>
    public static DynamicFontData LoadDefaultFontData()
    {
        if (_cachedData is not null) return _cachedData;
        var asm = typeof(EngineDefaults).Assembly;
        using var stream = asm.GetManifestResourceStream(EmbeddedFontResource)
            ?? throw new InvalidOperationException(
                $"Embedded font resource '{EmbeddedFontResource}' not found. " +
                "Check SimplestEngine.Resources.csproj EmbeddedResource entry.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        _cachedData = new DynamicFontData(ms.ToArray());
        return _cachedData;
    }

    /// <summary>Diagnostic helper: list every embedded resource the assembly
    /// ships with. Handy when the runtime can't find the font and you want a
    /// sanity check on the build pipeline.</summary>
    public static IReadOnlyList<string> ListEmbeddedResources() =>
        typeof(EngineDefaults).Assembly.GetManifestResourceNames();
}
