using System.Globalization;
using SimplestEngine.Abi;

namespace SimplestEngine.Resources;

/// <summary>Pandemonium parity: packed scene resource. Instances a Node tree.</summary>
[GDClass("PackedScene", "Resource")]
public sealed class PackedScene : Resource
{
    private readonly TscnDocument _doc;
    // ExtResource / SubResource ids: Godot 3 uses ints, Godot 4 uses strings ("1_xyz12").
    // Treat them uniformly as strings.
    private readonly Dictionary<string, IResource?> _extResources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _extResourcePaths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string?> _extResourceTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Variant> _subResources = new(StringComparer.Ordinal);

    public IScriptServer? ScriptServer { get; set; }

    public PackedScene(TscnDocument doc, string path)
    {
        _doc = doc;
        ResourcePath = path;
        ResourceClass = StringName.Get("PackedScene");
    }

    /// <summary>Materialize the scene as a fresh Node tree.</summary>
    public Node? Instance(IRenderingServer? rs, IResourceProvider? rp)
    {
        ResolveExtResources(rp);
        ResolveSubResources();

        // Build node tree. Track every node by its TSCN path ("." for root,
        // "Child", "Child/SubChild") so a parent= attribute can find it.
        var sections = _doc.Nodes.ToList();
        var byPath = new Dictionary<string, Node>(StringComparer.Ordinal);
        Node? root = null;

        foreach (var ns in sections)
        {
            var name = ns.Attributes.GetValueOrDefault("name") ?? "Node";
            var type = ns.Attributes.GetValueOrDefault("type");
            var parent = ns.Attributes.GetValueOrDefault("parent");

            // Decide concrete node instance.
            Node node;
            var instance = ns.Properties.GetValueOrDefault("instance");
            if (instance is { Kind: TscnValue.ValueKind.Call, CallName: "ExtResource" }
                && instance.CallArgs?.Count > 0
                && _extResources.TryGetValue(ExtKey(instance.CallArgs[0]), out var packedRes)
                && packedRes is PackedScene innerPacked)
            {
                node = innerPacked.Instance(rs, rp) ?? new Node();
            }
            else if (!string.IsNullOrEmpty(type))
            {
                node = InstantiateByType(type);
            }
            else
            {
                // "no type" usually means an inherited-scene override of an instance.
                // Without instance resolution we cannot reach the original node,
                // so we fall back to a base Node so the rest of the tree still loads.
                node = new Node();
            }

            node.Name = StringName.Get(name);
            if (node is CanvasItem ci && rs is not null) ci.RenderingServer = rs;

            // Apply properties (script is special, groups too).
            foreach (var (k, v) in ns.Properties)
            {
                switch (k)
                {
                    case "instance":
                        continue;
                    case "script":
                        ApplyScript(node, v);
                        continue;
                    case "groups":
                        ApplyGroups(node, v);
                        continue;
                    case "unique_name_in_owner":
                    case "editor_description":
                    case "owner":
                    case "index":
                        // Tolerated but not yet applied; not load-blocking.
                        continue;
                }
                if (k.StartsWith("metadata/", StringComparison.Ordinal))
                {
                    // No general Metadata API yet; ignore quietly.
                    continue;
                }
                ApplyProperty(node, k, v);
            }

            // Godot 3.x Control-derived nodes persist layout as margin_* (not position+size).
            // Our v1 ColorRect/Label are Node2D stand-ins — map the same numbers Godot writes.
            ApplyGodot3ControlMargins(node, ns);

            if (string.IsNullOrEmpty(parent))
            {
                root = node;
                byPath["."] = node;
            }
            else
            {
                var parentNode = byPath.GetValueOrDefault(parent);
                if (parentNode is not null)
                {
                    parentNode.AddChild(node);
                    var path = parent == "." ? name : $"{parent}/{name}";
                    byPath[path] = node;
                }
                else
                {
                    // Orphan: parent not found. Attach to root so it isn't lost.
                    if (root is not null)
                    {
                        Console.WriteLine($"[scene] orphan node '{name}' (parent='{parent}' not found) - attaching to root.");
                        root.AddChild(node);
                    }
                }
            }
        }

        ApplyConnections(byPath);
        return root;
    }

    private void ResolveExtResources(IResourceProvider? rp)
    {
        foreach (var ext in _doc.ExtResources)
        {
            var id = ext.Attributes.GetValueOrDefault("id") ?? "";
            var path = ext.Attributes.GetValueOrDefault("path");
            var type = ext.Attributes.GetValueOrDefault("type");
            _extResourcePaths[id] = path;
            _extResourceTypes[id] = type;

            if (string.IsNullOrEmpty(path)) continue;

            if (type == "Script")
            {
                _extResources[id] = LoadScriptResource(path, rp);
            }
            else if (rp is not null)
            {
                try { _extResources[id] = rp.Load(path); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[scene] ext_resource load failed: {path} ({ex.Message})");
                    _extResources[id] = null;
                }
            }
        }
    }

    private IResource? LoadScriptResource(string path, IResourceProvider? rp)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        var abs = ResolveScriptPath(path);

        if (ext == ".lua" && ScriptServer is not null)
        {
            var script = ScriptServer.Load(abs);
            if (script is not null)
                return new ScriptResource(script, abs);
        }

        if (rp is not null && System.IO.File.Exists(abs))
        {
            var src = System.IO.File.ReadAllText(abs);
            var lang = ext switch
            {
                ".gd" => "gdscript",
                ".cs" => "csharp",
                ".lua" => "lua",
                _ => "unknown",
            };
            return new ScriptSourceResource(src, lang, abs);
        }
        return null;
    }

    private string ResolveScriptPath(string path)
    {
        var local = path.StartsWith("res://", StringComparison.Ordinal) ? path["res://".Length..] : path;
        if (System.IO.Path.IsPathRooted(local)) return local;
        var dir = System.IO.Path.GetDirectoryName(ResourcePath) ?? "";
        return System.IO.Path.Combine(dir, local);
    }

    private void ResolveSubResources()
    {
        foreach (var sub in _doc.SubResources)
        {
            var id = sub.Attributes.GetValueOrDefault("id") ?? "";
            // v1: store the raw section so a typed loader can later reify shapes / textures.
            _subResources[id] = Variant.FromObject(sub);
        }
    }

    private Node InstantiateByType(string type)
    {
        var sn = StringName.Get(type);
        var inst = ClassDB.Instantiate(sn) as Node;
        if (inst is not null) return inst;

        // Unknown class: keep the scene loadable by emitting a placeholder
        // that remembers the original class name and every property thrown at it.
        Console.WriteLine($"[scene] unsupported node class '{type}' - using PlaceholderNode.");
        return new PlaceholderNode { OriginalClass = sn };
    }

    private void ApplyScript(Node node, TscnValue v)
    {
        if (v.Kind != TscnValue.ValueKind.Call || v.CallName != "ExtResource" || v.CallArgs is null || v.CallArgs.Count == 0)
            return;
        var key = ExtKey(v.CallArgs[0]);
        if (!_extResources.TryGetValue(key, out var res)) return;

        switch (res)
        {
            case ScriptResource sres when sres.Script is not null:
            {
                var inst = sres.Script.Instantiate((Abi.IGodotObjectAPI)node);
                node.SetScript(inst);
                break;
            }
            case ScriptSourceResource src:
            {
                // No runtime for this language; emit a one-shot warning per node
                // so the user knows their script is dormant (rather than silently failing).
                Console.WriteLine($"[scene] node '{node.DisplayName}' has a {src.Language} script ({src.SourceFile}); language not active.");
                break;
            }
        }
    }

    private static void ApplyGroups(Node node, TscnValue v)
    {
        if (v.Kind != TscnValue.ValueKind.Array || v.Array is null) return;
        foreach (var item in v.Array)
        {
            var name = item.AsString();
            if (!string.IsNullOrEmpty(name))
                node.AddToGroup(StringName.Get(name));
        }
    }

    private void ApplyConnections(Dictionary<string, Node> byPath)
    {
        foreach (var conn in _doc.Connections)
        {
            var signal = conn.Attributes.GetValueOrDefault("signal");
            var from = conn.Attributes.GetValueOrDefault("from");
            var to = conn.Attributes.GetValueOrDefault("to");
            var method = conn.Attributes.GetValueOrDefault("method");
            if (string.IsNullOrEmpty(signal) || string.IsNullOrEmpty(from) ||
                string.IsNullOrEmpty(to) || string.IsNullOrEmpty(method))
                continue;
            if (!byPath.TryGetValue(from, out var fromNode)) continue;
            if (!byPath.TryGetValue(to, out var toNode)) continue;
            fromNode.Connect(StringName.Get(signal), new Callable(toNode, StringName.Get(method)));
        }
    }

    /// <summary>
    /// Godot 3.x saves <see cref="ColorRect"/> and <see cref="Label"/> layout as
    /// <c>margin_left</c>/<c>margin_top</c>/<c>margin_right</c>/<c>margin_bottom</c>
    /// (Control convention). We expose Node2D <c>position</c> + <c>size</c> instead,
    /// so translate after all scalar properties are applied.
    /// </summary>
    private static void ApplyGodot3ControlMargins(Node node, Section ns)
    {
        if (!TryReadMarginProperty(ns, "margin_left", out var ml)) return;
        if (!TryReadMarginProperty(ns, "margin_top", out var mt)) return;
        if (!TryReadMarginProperty(ns, "margin_right", out var mr)) return;
        if (!TryReadMarginProperty(ns, "margin_bottom", out var mb)) return;

        var w = MathF.Max(0f, mr - ml);
        var h = MathF.Max(0f, mb - mt);

        switch (node)
        {
            case ColorRect cr:
                cr.Position = new Vector2(ml, mt);
                cr.Size = new Vector2(w, h);
                break;
            case Label lb:
                lb.Position = new Vector2(ml, mt);
                break;
        }
    }

    private static bool TryReadMarginProperty(Section ns, string key, out float value)
    {
        value = 0f;
        if (!ns.Properties.TryGetValue(key, out var tv)) return false;
        if (tv.Kind != TscnValue.ValueKind.Number) return false;
        value = (float)tv.AsNumber();
        return true;
    }

    private void ApplyProperty(Node node, string key, TscnValue val)
    {
        var sn = StringName.Get(key);
        var variant = ConvertValue(val);
        if (node.Set(sn, variant)) return;

        // Reflection fallback for nodes that have a CLR property but no ClassDB
        // registration (or for misc properties not in the registered set).
        var prop = node.GetType().GetProperty(PascalCase(key));
        if (prop is null) return;
        try
        {
            object? converted = ConvertToClr(variant, prop.PropertyType);
            if (converted is not null || !prop.PropertyType.IsValueType)
                prop.SetValue(node, converted);
        }
        catch { /* property exists but conversion failed - non-fatal */ }
    }

    private Variant ConvertValue(TscnValue val)
    {
        switch (val.Kind)
        {
            case TscnValue.ValueKind.Number: return Variant.From(val.Number);
            case TscnValue.ValueKind.Bool: return Variant.From(val.Bool);
            case TscnValue.ValueKind.String: return Variant.From(val.Text ?? "");
            case TscnValue.ValueKind.Array: return Variant.Nil; // arrays only used for groups currently
            case TscnValue.ValueKind.Dict: return Variant.Nil;  // dicts pass-through ignored
            case TscnValue.ValueKind.Call:
                return val.CallName switch
                {
                    "Vector2" when val.CallArgs?.Count >= 2 =>
                        Variant.From(new Vector2((float)val.CallArgs[0].AsNumber(), (float)val.CallArgs[1].AsNumber())),
                    "Vector2i" when val.CallArgs?.Count >= 2 =>
                        Variant.From(new Vector2i((int)val.CallArgs[0].AsNumber(), (int)val.CallArgs[1].AsNumber())),
                    "Color" when val.CallArgs?.Count >= 3 =>
                        Variant.From(new Color(
                            (float)val.CallArgs[0].AsNumber(), (float)val.CallArgs[1].AsNumber(),
                            (float)val.CallArgs[2].AsNumber(),
                            val.CallArgs.Count > 3 ? (float)val.CallArgs[3].AsNumber() : 1f)),
                    "Rect2" when val.CallArgs?.Count >= 4 =>
                        Variant.From(new Rect2(
                            (float)val.CallArgs[0].AsNumber(), (float)val.CallArgs[1].AsNumber(),
                            (float)val.CallArgs[2].AsNumber(), (float)val.CallArgs[3].AsNumber())),
                    "NodePath" when val.CallArgs?.Count >= 1 =>
                        Variant.FromNodePath(new NodePath(val.CallArgs[0].AsString())),
                    "ExtResource" when val.CallArgs?.Count >= 1 =>
                        Variant.FromObject(_extResources.GetValueOrDefault(ExtKey(val.CallArgs[0]))),
                    "SubResource" when val.CallArgs?.Count >= 1 =>
                        _subResources.GetValueOrDefault(ExtKey(val.CallArgs[0])),
                    _ => Variant.Nil,
                };
            default: return Variant.Nil;
        }
    }

    /// <summary>Normalize an Ext/SubResource id call argument to a stable string key.</summary>
    private static string ExtKey(TscnValue v) => v.Kind switch
    {
        TscnValue.ValueKind.Number when v.Number == Math.Truncate(v.Number) =>
            ((long)v.Number).ToString(CultureInfo.InvariantCulture),
        TscnValue.ValueKind.Number =>
            v.Number.ToString("R", CultureInfo.InvariantCulture),
        TscnValue.ValueKind.String => v.Text ?? "",
        _ => "",
    };

    private static object? ConvertToClr(Variant v, Type t)
    {
        if (t == typeof(string)) return v.AsString();
        if (t == typeof(int)) return (int)v.AsInt();
        if (t == typeof(long)) return v.AsInt();
        if (t == typeof(float)) return (float)v.AsFloat();
        if (t == typeof(double)) return v.AsFloat();
        if (t == typeof(bool)) return v.AsBool();
        if (t == typeof(Vector2)) return v.AsVector2();
        if (t == typeof(Vector2i)) return v.AsVector2i();
        if (t == typeof(Color)) return v.AsColor();
        if (t == typeof(Rect2)) return v.AsRect2();
        if (t == typeof(StringName)) return v.AsStringName();
        if (t == typeof(NodePath)) return v.AsRef<NodePath>();
        if (typeof(IResource).IsAssignableFrom(t)) return v.AsObject();
        return null;
    }

    private static string PascalCase(string snake)
    {
        if (string.IsNullOrEmpty(snake)) return snake;
        var parts = snake.Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new System.Text.StringBuilder();
        foreach (var p in parts) sb.Append(char.ToUpperInvariant(p[0])).Append(p[1..]);
        return sb.ToString();
    }
}

/// <summary>Wraps a runtime-active Script returned by the ScriptServer.</summary>
public sealed class ScriptResource : Resource
{
    public IScript Script { get; }
    public ScriptResource(IScript s, string path)
    {
        Script = s;
        ResourcePath = path;
        ResourceClass = StringName.Get("Script");
    }
}
