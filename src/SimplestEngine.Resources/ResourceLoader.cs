using SimplestEngine.Abi;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SimplestEngine.Resources;

/// <summary>
/// Project-rooted resource loader. Handles <c>res://</c> paths and <c>uid://</c>
/// references (via <see cref="UidDatabase"/>). Format dispatch is extension-based:
///   .tscn / .scn -> PackedScene
///   .tres / .res -> typed Resource where we recognize the inner class
///   .gd / .cs    -> ScriptSourceResource (kept as source; no runtime if language inactive)
///   .png / .jpg  -> Texture2D via ImageSharp
/// Anything else falls through as a GenericResource so a project load never crashes
/// on a single unsupported asset.
/// </summary>
public sealed class ResourceLoader : IResourceProvider
{
    public string ProjectRoot { get; }
    public IRenderingServer? Rendering { get; set; }
    public UidDatabase Uids { get; } = new();

    private readonly Dictionary<string, IResource> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ResourceLoader(string projectRoot)
    {
        ProjectRoot = projectRoot;
        var uidPath = Path.Combine(projectRoot, ".engine", "uid_cache.bin");
        if (File.Exists(uidPath))
            Uids.LoadFrom(uidPath);

        // Godot 4 emits one <file>.uid sidecar per resource. We pre-scan them so
        // a freshly cloned project (no .engine cache) still resolves uid:// refs.
        if (Directory.Exists(projectRoot))
            ScanUidSidecars(projectRoot);
    }

    public IResource? Load(string pathOrUid)
    {
        var resolved = ResolvePath(pathOrUid);
        if (resolved is null) return null;
        if (_cache.TryGetValue(resolved, out var cached)) return cached;

        var ext = Path.GetExtension(resolved).ToLowerInvariant();
        IResource? result = ext switch
        {
            ".tscn" or ".scn" => LoadScene(resolved),
            ".tres" or ".res" => LoadTextResource(resolved),
            ".png" or ".jpg" or ".jpeg" or ".bmp" => LoadTexture(resolved),
            ".gd" or ".cs" or ".lua" => LoadScriptSource(resolved, ext),
            ".ttf" or ".otf" => LoadDynamicFont(resolved),
            ".fnt" => LoadAngelcodeFont(resolved),
            _ => LoadOpaque(resolved),
        };
        if (result is not null) _cache[resolved] = result;
        return result;
    }

    public void Save(IResource resource, string path)
    {
        // v1: scenes not yet saveable from the loader path.
        throw new NotImplementedException();
    }

    public bool Exists(string pathOrUid) => ResolvePath(pathOrUid) is not null;
    public string? UidToPath(string uid) => Uids.UidToPath(uid);
    public string? PathToUid(string path) => Uids.PathToUid(path);

    private string? ResolvePath(string pathOrUid)
    {
        if (pathOrUid.StartsWith("uid://", StringComparison.Ordinal))
        {
            var path = Uids.UidToPath(pathOrUid);
            return path is null ? null : ResolvePath(path);
        }
        var local = pathOrUid;
        if (local.StartsWith("res://", StringComparison.Ordinal)) local = local["res://".Length..];
        var full = Path.IsPathRooted(local) ? local : Path.Combine(ProjectRoot, local);
        return File.Exists(full) ? full : null;
    }

    private PackedScene LoadScene(string absPath)
    {
        var text = File.ReadAllText(absPath);
        var doc = TscnParser.Parse(text);
        return new PackedScene(doc, absPath);
    }

    private Resource LoadTextResource(string absPath)
    {
        var text = File.ReadAllText(absPath);
        var doc = TscnParser.Parse(text);
        // Promote known [gd_resource type="..."] payloads to typed engine resources.
        var header = doc.Header;
        var type = header?.Attributes.GetValueOrDefault("type");
        if (!string.IsNullOrEmpty(type))
        {
            var typed = MaterializeResourceByType(type!, doc);
            if (typed is not null)
            {
                typed.ResourcePath = absPath;
                return typed;
            }
        }
        return new GenericResource(doc) { ResourcePath = absPath };
    }

    private static Resource? MaterializeResourceByType(string type, TscnDocument doc)
    {
        // .tres files always carry one top-level sub-resource-like body in the
        // top section's properties (the [gd_resource] header itself).
        // Apply the property bag to a fresh instance via ClassDB.
        var inst = ClassDB.Instantiate(StringName.Get(type)) as Resource;
        if (inst is null) return null;
        // Take the first non-header section's properties; for simple .tres the
        // properties live directly on the [gd_resource] section itself though, so
        // try both.
        var carrier = doc.Sections.FirstOrDefault(s => s.Kind != "ext_resource" && s.Kind != "sub_resource");
        if (carrier is not null)
        {
            foreach (var (k, v) in carrier.Properties)
            {
                var variant = ConvertSimple(v);
                inst.Set(StringName.Get(k), variant);
            }
        }
        return inst;
    }

    private static Variant ConvertSimple(TscnValue v) => v.Kind switch
    {
        TscnValue.ValueKind.Number => Variant.From(v.Number),
        TscnValue.ValueKind.Bool => Variant.From(v.Bool),
        TscnValue.ValueKind.String => Variant.From(v.Text ?? ""),
        TscnValue.ValueKind.Call when v.CallName == "Vector2" && v.CallArgs?.Count >= 2 =>
            Variant.From(new Vector2((float)v.CallArgs[0].AsNumber(), (float)v.CallArgs[1].AsNumber())),
        TscnValue.ValueKind.Call when v.CallName == "Color" && v.CallArgs?.Count >= 3 =>
            Variant.From(new Color((float)v.CallArgs[0].AsNumber(), (float)v.CallArgs[1].AsNumber(),
                (float)v.CallArgs[2].AsNumber(),
                v.CallArgs.Count > 3 ? (float)v.CallArgs[3].AsNumber() : 1f)),
        _ => Variant.Nil,
    };

    private Texture2D? LoadTexture(string absPath)
    {
        if (Rendering is null) return null;
        using var img = Image.Load<Rgba32>(absPath);
        var bytes = new byte[img.Width * img.Height * 4];
        img.CopyPixelDataTo(bytes);
        var rid = Rendering.TextureCreate(img.Width, img.Height, bytes);
        return new Texture2D(rid, img.Width, img.Height) { ResourcePath = absPath };
    }

    private ScriptSourceResource LoadScriptSource(string absPath, string ext)
    {
        var src = File.ReadAllText(absPath);
        var lang = ext switch
        {
            ".gd" => "gdscript",
            ".cs" => "csharp",
            ".lua" => "lua",
            _ => "unknown",
        };
        return new ScriptSourceResource(src, lang, absPath);
    }

    private DynamicFontData LoadDynamicFont(string absPath)
    {
        // No texture creation here — atlases are lazy and live on the
        // DynamicFontAtSize. We only stage the raw TTF bytes.
        var bytes = File.ReadAllBytes(absPath);
        return new DynamicFontData(bytes) { ResourcePath = absPath };
    }

    private BitmapFont LoadAngelcodeFont(string absPath) =>
        BitmapFontLoader.Load(absPath, this);

    private IResource? LoadOpaque(string absPath)
    {
        // Final safety net: anything we don't model surfaces as an opaque resource
        // so the editor / project loader can still report it.
        return new OpaqueResource(absPath);
    }

    private void ScanUidSidecars(string projectRoot)
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(projectRoot, "*.uid", SearchOption.AllDirectories))
            {
                var uid = File.ReadAllText(f).Trim();
                if (!uid.StartsWith("uid://", StringComparison.Ordinal)) continue;
                // The actual file is the .uid path without ".uid".
                var target = f.EndsWith(".uid", StringComparison.OrdinalIgnoreCase)
                    ? f[..^".uid".Length] : f;
                var rel = Path.GetRelativePath(projectRoot, target).Replace('\\', '/');
                Uids.Set(uid, "res://" + rel);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[uid] sidecar scan failed: {ex.Message}");
        }
    }
}

internal sealed class GenericResource : Resource
{
    public TscnDocument Document { get; }
    public GenericResource(TscnDocument doc) { Document = doc; }
}

internal sealed class OpaqueResource : Resource
{
    public string AbsolutePath { get; }
    public OpaqueResource(string path)
    {
        AbsolutePath = path;
        ResourcePath = path;
        ResourceClass = StringName.Get("Resource");
    }
}

/// <summary>UID-to-path mapping (`uid://...`).</summary>
public sealed class UidDatabase
{
    private readonly Dictionary<string, string> _uidToPath = new();
    private readonly Dictionary<string, string> _pathToUid = new();

    public string? UidToPath(string uid) => _uidToPath.GetValueOrDefault(uid);
    public string? PathToUid(string path) => _pathToUid.GetValueOrDefault(path);

    public void Set(string uid, string path) { _uidToPath[uid] = path; _pathToUid[path] = uid; }

    public void LoadFrom(string path)
    {
        if (!File.Exists(path)) return;
        foreach (var line in File.ReadAllLines(path))
        {
            var p = line.Split('=', 2);
            if (p.Length == 2) Set(p[0], p[1]);
        }
    }

    public void SaveTo(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, _uidToPath.Select(kv => $"{kv.Key}={kv.Value}"));
    }
}
