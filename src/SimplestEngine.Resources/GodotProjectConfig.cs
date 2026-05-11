using System.Globalization;
using System.Text;
using SimplestEngine.Abi;

namespace SimplestEngine.Resources;

/// <summary>
/// Strongly-typed view of a Godot 3.x / 4.x <c>project.godot</c> file.
///
/// Covers the surface needed to boot a Godot project verbatim - without ever
/// rewriting the user's file:
///   - [application] config/name, run/main_scene
///   - [display]     window/size/{width,height,viewport_width,viewport_height}
///   - [autoload]    NodeName=("*")? "res://..."
///   - [input]       action_name = { "events": [Object(InputEventKey, "scancode":N, "shift":bool, ...), ...] }
///   - [rendering]   environment/default_clear_color = Color(r, g, b, a)
///
/// Everything else is loaded into a raw section/key/value bag so callers
/// (editor, plugins) can inspect or round-trip without us modelling it.
/// </summary>
public sealed class GodotProjectConfig
{
    public string Title { get; set; } = "Game";
    public string? MainScene { get; set; }

    /// <summary>Godot 3 historical default window size (matches editor's project wizard).</summary>
    public const int Godot3DefaultWidth = 1024;
    public const int Godot3DefaultHeight = 600;

    public int WindowWidth { get; set; } = Godot3DefaultWidth;
    public int WindowHeight { get; set; } = Godot3DefaultHeight;

    /// <summary>True only when the project.godot actually has a [display] section.
    /// Bootstrap code uses this to decide between cfg-driven and engine-default sizes.</summary>
    public bool HasDisplaySection { get; set; }

    /// <summary>Set from [rendering] environment/default_clear_color, if present.</summary>
    public Color? ClearColor { get; set; }

    public List<AutoloadEntry> Autoloads { get; } = new();
    public Dictionary<string, InputActionEntry> InputActions { get; } = new(StringComparer.Ordinal);

    /// <summary>Raw section -> key -> raw value text. Preserved for inspection / round-trip.</summary>
    public Dictionary<string, Dictionary<string, string>> Raw { get; } =
        new(StringComparer.Ordinal);

    public static GodotProjectConfig Load(string path)
    {
        var cfg = new GodotProjectConfig();
        if (!File.Exists(path)) return cfg;

        var raw = GodotIniParser.Parse(File.ReadAllText(path));
        foreach (var (section, props) in raw)
            cfg.Raw[section] = props;

        if (raw.TryGetValue("application", out var app))
        {
            cfg.Title = app.TryGetValue("config/name", out var nm)
                ? Unquote(nm) : cfg.Title;
            cfg.MainScene = app.TryGetValue("run/main_scene", out var ms)
                ? Unquote(ms) : null;
        }

        if (raw.TryGetValue("display", out var disp))
        {
            cfg.HasDisplaySection = true;
            cfg.WindowWidth = ReadInt(disp,
                "window/size/viewport_width",
                "window/size/width") ?? cfg.WindowWidth;
            cfg.WindowHeight = ReadInt(disp,
                "window/size/viewport_height",
                "window/size/height") ?? cfg.WindowHeight;
        }

        if (raw.TryGetValue("autoload", out var auto))
        {
            foreach (var (k, v) in auto)
            {
                var raw_v = Unquote(v);
                var singleton = raw_v.StartsWith("*", StringComparison.Ordinal);
                if (singleton) raw_v = raw_v[1..];
                cfg.Autoloads.Add(new AutoloadEntry
                {
                    Name = k,
                    Path = raw_v,
                    Singleton = singleton,
                });
            }
        }

        if (raw.TryGetValue("input", out var input))
        {
            foreach (var (action, body) in input)
            {
                var entry = new InputActionEntry { Name = action };
                ParseInputActionBody(body, entry);
                cfg.InputActions[action] = entry;
            }
        }

        if (raw.TryGetValue("rendering", out var render))
        {
            if (render.TryGetValue("environment/default_clear_color", out var cc))
                cfg.ClearColor = ParseColorCall(cc);
        }

        return cfg;
    }

    /// <summary>
    /// Pulls the deadzone and every <c>Object(InputEventKey, ...)</c> entry out of
    /// the action body (which Godot saves as a dict literal). Other event kinds
    /// (mouse, joypad) are silently ignored for now.
    /// </summary>
    private static void ParseInputActionBody(string body, InputActionEntry entry)
    {
        // Deadzone: "deadzone": <number>
        int dz = body.IndexOf("\"deadzone\"", StringComparison.Ordinal);
        if (dz >= 0)
        {
            int colon = body.IndexOf(':', dz);
            int comma = body.IndexOf(',', colon);
            if (colon > 0)
            {
                var slice = comma > 0 ? body[(colon + 1)..comma] : body[(colon + 1)..];
                if (float.TryParse(slice.Trim(), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var v))
                    entry.Deadzone = v;
            }
        }

        // Walk every "Object(...)" call at top level. Modifier flags, scancode,
        // physical_scancode and unicode are extracted from each call's argument list.
        foreach (var obj in InputObjectParser.EnumerateObjects(body))
        {
            if (!string.Equals(obj.TypeName, "InputEventKey", StringComparison.Ordinal))
                continue;
            entry.Bindings.Add(InputObjectParser.ToBinding(obj));
        }
    }

    private static Color? ParseColorCall(string s)
    {
        s = s.Trim();
        if (!s.StartsWith("Color", StringComparison.Ordinal)) return null;
        int op = s.IndexOf('(');
        int cp = s.LastIndexOf(')');
        if (op < 0 || cp <= op) return null;
        var inside = s[(op + 1)..cp];
        var parts = inside.Split(',');
        if (parts.Length < 3) return null;
        float r = 0, g = 0, b = 0, a = 1f;
        if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out r)) return null;
        if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out g)) return null;
        if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out b)) return null;
        if (parts.Length >= 4)
            float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a);
        return new Color(r, g, b, a);
    }

    private static int? ReadInt(Dictionary<string, string> bag, params string[] keys)
    {
        foreach (var k in keys)
            if (bag.TryGetValue(k, out var s) && int.TryParse(s, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var n))
                return n;
        return null;
    }

    private static string Unquote(string s)
    {
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') return s[1..^1];
        return s;
    }
}

public sealed class AutoloadEntry
{
    public string Name { get; set; } = "";
    /// <summary>Always in <c>res://...</c> form.</summary>
    public string Path { get; set; } = "";
    public bool Singleton { get; set; }
}

/// <summary>
/// One <c>InputEventKey</c> Godot saves into a project's InputMap. A binding
/// only fires while every flagged modifier is held and the key produces the
/// scancode (or physical scancode, as a fallback).
/// </summary>
public sealed class InputEventKeyBinding
{
    /// <summary>Godot 3 <c>scancode</c> / Godot 4 <c>keycode</c> (logical key).</summary>
    public int Scancode { get; set; }
    /// <summary>Godot 3 <c>physical_scancode</c> (layout-independent).</summary>
    public int PhysicalScancode { get; set; }
    public int Unicode { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Control { get; set; }
    public bool Meta { get; set; }
    /// <summary>Godot 3 keys also have a "command" alias that mirrors Ctrl on
    /// non-mac builds and Meta on mac. We OR it into the binding's mod set.</summary>
    public bool Command { get; set; }

    /// <summary>True when there's an actual key associated (scancode or physical).</summary>
    public bool IsBound => Scancode > 0 || PhysicalScancode > 0;

    public KeyModifiers ToModifiers()
    {
        var m = KeyModifiers.None;
        if (Alt)     m |= KeyModifiers.Alt;
        if (Shift)   m |= KeyModifiers.Shift;
        if (Control) m |= KeyModifiers.Ctrl;
        if (Meta)    m |= KeyModifiers.Meta;
        if (Command) m |= KeyModifiers.Ctrl; // Godot 3 maps command -> ctrl on non-mac
        return m;
    }
}

public sealed class InputActionEntry
{
    public string Name { get; set; } = "";
    public float Deadzone { get; set; } = 0.5f;
    public List<InputEventKeyBinding> Bindings { get; } = new();
}

/// <summary>
/// Pulls structured Godot <c>Object(TypeName, "key":value, "key":value, ...)</c>
/// calls out of a (potentially multi-line) project.godot value. Strictly typed,
/// unlike the regex sniff the previous implementation used.
/// </summary>
internal static class InputObjectParser
{
    internal sealed class ObjectCall
    {
        public string TypeName = "";
        public Dictionary<string, string> Fields = new(StringComparer.Ordinal);
    }

    public static IEnumerable<ObjectCall> EnumerateObjects(string body)
    {
        int i = 0;
        while (true)
        {
            int idx = body.IndexOf("Object(", i, StringComparison.Ordinal);
            if (idx < 0) yield break;
            int open = idx + "Object".Length;
            int close = FindMatchingClose(body, open);
            if (close < 0) yield break;
            var inside = body[(open + 1)..close];
            var call = new ObjectCall();
            ParseObjectBody(inside, call);
            yield return call;
            i = close + 1;
        }
    }

    /// <summary>Maps a parsed Godot <c>InputEventKey</c> Object into our binding type.</summary>
    public static InputEventKeyBinding ToBinding(ObjectCall obj)
    {
        var b = new InputEventKeyBinding();
        foreach (var (k, v) in obj.Fields)
        {
            switch (k)
            {
                // Godot 3 = scancode, Godot 4 = keycode. Treat them as the same field.
                case "scancode":
                case "keycode":
                    b.Scancode = ParseInt(v);
                    break;
                case "physical_scancode":
                case "physical_keycode":
                    b.PhysicalScancode = ParseInt(v);
                    break;
                case "unicode": b.Unicode = ParseInt(v); break;
                case "alt": b.Alt = ParseBool(v); break;
                case "shift": b.Shift = ParseBool(v); break;
                case "control": b.Control = ParseBool(v); break;
                case "meta": b.Meta = ParseBool(v); break;
                case "command": b.Command = ParseBool(v); break;
            }
        }
        return b;
    }

    private static void ParseObjectBody(string inside, ObjectCall call)
    {
        // First top-level token = type name; the rest are "key":value pairs.
        int p = 0;
        Skip(ref p, inside);
        int typeStart = p;
        while (p < inside.Length && inside[p] != ',' && !char.IsWhiteSpace(inside[p])) p++;
        call.TypeName = inside[typeStart..p].Trim();

        while (p < inside.Length)
        {
            Skip(ref p, inside);
            if (p >= inside.Length) break;
            if (inside[p] == ',') { p++; continue; }
            // expect a quoted key
            if (inside[p] != '"') { p++; continue; }
            int keyStart = ++p;
            while (p < inside.Length && inside[p] != '"') p++;
            var key = inside[keyStart..p];
            if (p < inside.Length) p++; // consume closing quote
            Skip(ref p, inside);
            if (p >= inside.Length || inside[p] != ':') continue;
            p++; // consume ':'
            Skip(ref p, inside);
            // value runs until the next top-level comma (or end of object body).
            int valStart = p;
            int depth = 0;
            bool inStr = false;
            while (p < inside.Length)
            {
                var c = inside[p];
                if (inStr)
                {
                    if (c == '\\' && p + 1 < inside.Length) { p += 2; continue; }
                    if (c == '"') inStr = false;
                    p++; continue;
                }
                if (c == '"') { inStr = true; p++; continue; }
                if (c == '(' || c == '[' || c == '{') { depth++; p++; continue; }
                if (c == ')' || c == ']' || c == '}') { depth--; p++; continue; }
                if (c == ',' && depth == 0) break;
                p++;
            }
            var val = inside[valStart..p].Trim();
            // strip wrapping quotes (the values we care about are scalars or bools).
            if (val.Length >= 2 && val[0] == '"' && val[^1] == '"') val = val[1..^1];
            call.Fields[key] = val;
        }
    }

    private static int FindMatchingClose(string s, int openIdx)
    {
        int depth = 0;
        bool inStr = false;
        for (int i = openIdx; i < s.Length; i++)
        {
            var c = s[i];
            if (inStr)
            {
                if (c == '\\' && i + 1 < s.Length) { i++; continue; }
                if (c == '"') inStr = false;
                continue;
            }
            if (c == '"') { inStr = true; continue; }
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}')
            {
                depth--;
                if (depth == 0 && c == ')') return i;
            }
        }
        return -1;
    }

    private static void Skip(ref int p, string s)
    {
        while (p < s.Length && (char.IsWhiteSpace(s[p]) || s[p] == '\n' || s[p] == '\r')) p++;
    }

    private static int ParseInt(string s) =>
        int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    private static bool ParseBool(string s) =>
        string.Equals(s.Trim(), "true", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Minimal Godot-flavoured INI parser. Tolerates everything we don't model:
///   - Comments starting with ';' or '#'
///   - Multi-line values whose top-level parens / brackets / braces are unbalanced
///   - Section-less prologue (treated as the "header" section)
/// </summary>
public static class GodotIniParser
{
    public static Dictionary<string, Dictionary<string, string>> Parse(string text)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var section = "header";
        result[section] = new Dictionary<string, string>(StringComparer.Ordinal);

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (string.IsNullOrWhiteSpace(line)) continue;
            var ls = line.TrimStart();
            if (ls.StartsWith(';') || ls.StartsWith('#')) continue;

            if (ls.StartsWith('['))
            {
                var end = ls.LastIndexOf(']');
                if (end < 0) continue;
                section = ls.Substring(1, end - 1).Trim();
                if (!result.ContainsKey(section))
                    result[section] = new Dictionary<string, string>(StringComparer.Ordinal);
                continue;
            }

            var eq = ls.IndexOf('=');
            if (eq < 0) continue;
            var key = ls[..eq].Trim();
            var rest = ls[(eq + 1)..].TrimStart();

            // Multi-line values: keep reading until parens are balanced.
            var sb = new StringBuilder();
            sb.Append(rest);
            int depth = NetParens(rest);
            while (depth > 0 && i + 1 < lines.Length)
            {
                i++;
                sb.Append('\n');
                sb.Append(lines[i]);
                depth += NetParens(lines[i]);
            }
            result[section][key] = sb.ToString().Trim();
        }

        return result;
    }

    private static int NetParens(string s)
    {
        int d = 0;
        bool inStr = false;
        bool escape = false;
        foreach (var c in s)
        {
            if (escape) { escape = false; continue; }
            if (c == '\\') { escape = true; continue; }
            if (c == '"') { inStr = !inStr; continue; }
            if (inStr) continue;
            if (c == '(' || c == '[' || c == '{') d++;
            else if (c == ')' || c == ']' || c == '}') d--;
        }
        return d;
    }
}

/// <summary>
/// Translates Godot-encoded InputMap actions into an engine <see cref="IInputMap"/>.
///
/// Strict Godot parity: every action in <c>project.godot [input]</c> is created
/// on the map (replacing any engine default with the same name). An action is
/// created even when none of its events have a usable scancode - matching
/// Godot's "action defined, no key bound" state. Caller is responsible for
/// installing engine-side <c>ui_*</c> defaults BEFORE calling Apply, and only
/// for actions NOT defined in the project file.
/// </summary>
public static class InputMapImporter
{
    /// <summary>
    /// When Godot saves <c>InputEventKey</c> rows with <c>scancode:0</c> (before the user
    /// picks real keys in the Input Map editor), the action exists but binds nothing.
    /// Attaching our engine defaults keeps sample projects playable without hand-editing
    /// <c>project.godot</c>. Actions with real bindings from the file are untouched.
    /// </summary>
    private static readonly Dictionary<string, (KeyCode Key, KeyModifiers Mods)[]> EmptyBindingFallbacks =
        new(StringComparer.Ordinal)
        {
            ["ui_left"] = new[] { (KeyCode.Left, KeyModifiers.None) },
            ["ui_right"] = new[] { (KeyCode.Right, KeyModifiers.None) },
            ["ui_up"] = new[] { (KeyCode.Up, KeyModifiers.None) },
            ["ui_down"] = new[] { (KeyCode.Down, KeyModifiers.None) },
            ["ui_accept"] = new[] { (KeyCode.Space, KeyModifiers.None) },
            ["ui_cancel"] = new[] { (KeyCode.Escape, KeyModifiers.None) },
            ["move_left"] = new[] { (KeyCode.A, KeyModifiers.None) },
            ["move_right"] = new[] { (KeyCode.D, KeyModifiers.None) },
            ["jump"] = new[] { (KeyCode.Space, KeyModifiers.None) },
        };

    public static void Apply(GodotProjectConfig cfg, IInputMap map)
    {
        foreach (var (name, entry) in cfg.InputActions)
        {
            var action = StringName.Get(name);
            // Strict overwrite: drop any pre-existing engine default for this action,
            // then recreate it - so the project file is the single source of truth.
            map.RemoveAction(action);
            map.AddAction(action);

            int bound = 0;
            foreach (var binding in entry.Bindings)
            {
                if (!binding.IsBound) continue; // scancode=0 + physical=0 is "no key"
                var raw = binding.Scancode > 0 ? binding.Scancode : binding.PhysicalScancode;
                var key = TranslateGodotKey(raw);
                if (key == KeyCode.None)
                {
                    Console.WriteLine($"[input] action '{name}': godot keycode {raw} not yet mapped, skipped.");
                    continue;
                }
                map.AddKeyEvent(action, key, binding.ToModifiers());
                bound++;
            }

            if (bound == 0 && EmptyBindingFallbacks.TryGetValue(name, out var fallbacks))
            {
                foreach (var (key, mods) in fallbacks)
                    map.AddKeyEvent(action, key, mods);
                bound = fallbacks.Length;
                Console.WriteLine($"[input] action '{name}': project file has no key bindings (Godot scancode=0 stubs) — using engine defaults ({bound} key(s)).");
            }
            else
                Console.WriteLine($"[input] action '{name}' -> {bound} binding(s){(bound == 0 ? " (defined but empty)" : "")}");
        }
    }

    /// <summary>
    /// Godot 3 keycodes: ASCII for printable keys, 0x01000000+ range for system
    /// keys (the same layout Pandemonium inherits). We cover the surface our
    /// <see cref="KeyCode"/> enum understands - anything else is warned and skipped.
    /// </summary>
    private static KeyCode TranslateGodotKey(int godotKey) => godotKey switch
    {
        // ASCII printable range maps directly: Godot stores 'A'..'Z' as 65..90,
        // '0'..'9' as 48..57, space as 32, etc. Our KeyCode reserves those slots
        // for the same characters on purpose.
        32 => KeyCode.Space,
        39 => KeyCode.Apostrophe,
        44 => KeyCode.Comma,
        45 => KeyCode.Minus,
        46 => KeyCode.Period,
        47 => KeyCode.Slash,
        >= 48 and <= 57 => (KeyCode)godotKey,           // 0..9
        59 => KeyCode.Semicolon,
        61 => KeyCode.Equal,
        >= 65 and <= 90 => (KeyCode)godotKey,           // A..Z
        91 => KeyCode.LeftBracket,
        92 => KeyCode.Backslash,
        93 => KeyCode.RightBracket,
        96 => KeyCode.GraveAccent,

        // High-tier (Godot 3 KEY_* constants live at 0x01000000+ in Godot 3.
        // Pandemonium / Godot 3.x dump these directly into project.godot).
        16777217 => KeyCode.Escape,
        16777218 => KeyCode.Tab,
        16777220 => KeyCode.Enter,
        16777221 => KeyCode.Enter,        // KP_ENTER → reuse Enter
        16777222 => KeyCode.Insert,
        16777223 => KeyCode.Delete,
        16777224 => KeyCode.Pause,
        16777225 => KeyCode.PrintScreen,
        16777231 => KeyCode.Left,
        16777232 => KeyCode.Up,
        16777233 => KeyCode.Right,
        16777234 => KeyCode.Down,
        16777235 => KeyCode.PageUp,
        16777236 => KeyCode.PageDown,
        16777229 => KeyCode.Home,
        16777230 => KeyCode.End,
        16777237 => KeyCode.F1,
        16777238 => KeyCode.F2,
        16777239 => KeyCode.F3,
        16777240 => KeyCode.F4,
        16777241 => KeyCode.F5,
        16777242 => KeyCode.F6,
        16777243 => KeyCode.F7,
        16777244 => KeyCode.F8,
        16777245 => KeyCode.F9,
        16777246 => KeyCode.F10,
        16777247 => KeyCode.F11,
        16777248 => KeyCode.F12,
        16777251 => KeyCode.Shift,
        16777252 => KeyCode.Ctrl,
        16777253 => KeyCode.Meta,
        16777254 => KeyCode.Alt,
        16777256 => KeyCode.CapsLock,
        16777257 => KeyCode.NumLock,
        16777258 => KeyCode.ScrollLock,

        // Backspace shows up as 16777219 in Godot 3 (KEY_BACKSPACE).
        16777219 => KeyCode.Backspace,

        _ => KeyCode.None,
    };
}
