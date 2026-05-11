using System.Globalization;
using System.Text;

namespace SimplestEngine.Resources;

/// <summary>
/// Pandemonium / Godot .tscn / .tres parser. Subset that handles real scenes:
///   [gd_scene load_steps=N format=2]
///   [ext_resource path="res://..." type="Texture" id=1]
///   [sub_resource type="ClassName" id=1]
///       prop = value
///   [node name="X" type="ClassName" parent="."]
///       prop = value
///   [connection signal="x" from="." to="." method="_on_x"]
/// </summary>
public static class TscnParser
{
    public static TscnDocument Parse(string text)
    {
        var doc = new TscnDocument();
        Section? current = null;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trim = line.TrimEnd();
            if (string.IsNullOrWhiteSpace(trim)) { current = null; continue; }
            if (trim.StartsWith(';')) continue;

            if (trim.StartsWith('['))
            {
                // section header line(s) can span by being well-formed
                var end = trim.LastIndexOf(']');
                if (end < 0) throw new FormatException($"Unterminated section header at line {i + 1}");
                var header = trim.Substring(1, end - 1);
                current = ParseSectionHeader(header);
                doc.Sections.Add(current);
                continue;
            }

            // prop = value line, possibly multi-line for arrays/dicts/PoolXxx
            if (current is null) continue;
            var eq = trim.IndexOf('=');
            if (eq < 0) continue;
            var key = trim[..eq].Trim();
            var valueStart = eq + 1;
            // Read whole value, supporting multi-line balancing of (), [], {}
            var valueBuilder = new StringBuilder();
            valueBuilder.Append(trim[valueStart..]);
            int depth = NetParens(trim[valueStart..]);
            while (depth > 0 && i + 1 < lines.Length)
            {
                i++;
                valueBuilder.Append('\n');
                valueBuilder.Append(lines[i]);
                depth += NetParens(lines[i]);
            }
            var rawVal = valueBuilder.ToString().Trim();
            current.Properties[key] = TscnValue.Parse(rawVal);
        }
        return doc;
    }

    private static int NetParens(string s)
    {
        int d = 0; bool inStr = false; bool escape = false;
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

    private static Section ParseSectionHeader(string header)
    {
        // "node name=\"X\" type=\"Y\" parent=\".\""
        var idx = header.IndexOf(' ');
        string name = idx < 0 ? header : header[..idx];
        var rest = idx < 0 ? string.Empty : header[(idx + 1)..];
        var s = new Section { Kind = name };

        // parse k=v pairs (values can be quoted)
        int p = 0;
        while (p < rest.Length)
        {
            while (p < rest.Length && char.IsWhiteSpace(rest[p])) p++;
            int kStart = p;
            while (p < rest.Length && rest[p] != '=' && !char.IsWhiteSpace(rest[p])) p++;
            if (p >= rest.Length || rest[p] != '=') break;
            string k = rest[kStart..p];
            p++; // skip '='
            string v;
            if (p < rest.Length && rest[p] == '"')
            {
                int vStart = ++p;
                while (p < rest.Length && rest[p] != '"') { if (rest[p] == '\\') p++; p++; }
                v = rest[vStart..p];
                if (p < rest.Length) p++;
            }
            else
            {
                int vStart = p;
                while (p < rest.Length && !char.IsWhiteSpace(rest[p])) p++;
                v = rest[vStart..p];
            }
            s.Attributes[k] = v;
        }
        return s;
    }
}

public sealed class TscnDocument
{
    public List<Section> Sections { get; } = new();

    public Section? Header => Sections.FirstOrDefault(s => s.Kind == "gd_scene" || s.Kind == "gd_resource");
    public IEnumerable<Section> ExtResources => Sections.Where(s => s.Kind == "ext_resource");
    public IEnumerable<Section> SubResources => Sections.Where(s => s.Kind == "sub_resource");
    public IEnumerable<Section> Nodes => Sections.Where(s => s.Kind == "node");
    public IEnumerable<Section> Connections => Sections.Where(s => s.Kind == "connection");
}

public sealed class Section
{
    public string Kind { get; set; } = string.Empty;
    public Dictionary<string, string> Attributes { get; } = new();
    public Dictionary<string, TscnValue> Properties { get; } = new();
}

/// <summary>A parsed TSCN value (preserves enough type info to round-trip on save).</summary>
public sealed class TscnValue
{
    public ValueKind Kind { get; set; }
    public string? Text { get; set; }
    public double Number { get; set; }
    public bool Bool { get; set; }
    public string? CallName { get; set; }   // e.g. "Vector2", "Color", "Rect2", "ExtResource", "SubResource", "NodePath"
    public List<TscnValue>? CallArgs { get; set; }
    public List<TscnValue>? Array { get; set; }
    public Dictionary<string, TscnValue>? Dict { get; set; }

    public enum ValueKind { String, Number, Bool, Null, Call, Array, Dict }

    public static TscnValue Parse(string raw)
    {
        raw = raw.Trim();
        if (raw.Length == 0) return new TscnValue { Kind = ValueKind.Null };

        if (raw == "true") return new TscnValue { Kind = ValueKind.Bool, Bool = true };
        if (raw == "false") return new TscnValue { Kind = ValueKind.Bool, Bool = false };
        if (raw == "null") return new TscnValue { Kind = ValueKind.Null };

        if (raw[0] == '"')
        {
            // string literal with possible escapes
            var sb = new StringBuilder();
            for (int i = 1; i < raw.Length; i++)
            {
                if (raw[i] == '\\' && i + 1 < raw.Length) { sb.Append(raw[++i]); continue; }
                if (raw[i] == '"') break;
                sb.Append(raw[i]);
            }
            return new TscnValue { Kind = ValueKind.String, Text = sb.ToString() };
        }

        if (raw[0] == '[')
        {
            var inside = raw[1..^1].Trim();
            var arr = new List<TscnValue>();
            foreach (var part in SplitTopLevel(inside, ','))
                if (!string.IsNullOrWhiteSpace(part)) arr.Add(Parse(part));
            return new TscnValue { Kind = ValueKind.Array, Array = arr };
        }

        if (raw[0] == '{')
        {
            var inside = raw[1..^1].Trim();
            var dict = new Dictionary<string, TscnValue>();
            foreach (var part in SplitTopLevel(inside, ','))
            {
                var p = part.Trim();
                if (p.Length == 0) continue;
                var colon = TopLevelColon(p);
                if (colon < 0) continue;
                var k = Parse(p[..colon].Trim()).Text ?? "";
                var v = Parse(p[(colon + 1)..].Trim());
                dict[k] = v;
            }
            return new TscnValue { Kind = ValueKind.Dict, Dict = dict };
        }

        // call form: Name(args)
        int op = raw.IndexOf('(');
        if (op > 0 && raw[^1] == ')')
        {
            var name = raw[..op].Trim();
            var inside = raw[(op + 1)..^1].Trim();
            var args = new List<TscnValue>();
            foreach (var part in SplitTopLevel(inside, ','))
                if (!string.IsNullOrWhiteSpace(part)) args.Add(Parse(part));
            return new TscnValue { Kind = ValueKind.Call, CallName = name, CallArgs = args };
        }

        // numeric
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return new TscnValue { Kind = ValueKind.Number, Number = d };

        // bare identifier - treat as string
        return new TscnValue { Kind = ValueKind.String, Text = raw };
    }

    private static IEnumerable<string> SplitTopLevel(string s, char sep)
    {
        int depth = 0; int start = 0; bool inStr = false; bool escape = false;
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (escape) { escape = false; continue; }
            if (c == '\\') { escape = true; continue; }
            if (c == '"') { inStr = !inStr; continue; }
            if (inStr) continue;
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}') depth--;
            else if (c == sep && depth == 0) { yield return s.Substring(start, i - start); start = i + 1; }
        }
        if (start < s.Length) yield return s.Substring(start);
    }

    private static int TopLevelColon(string s)
    {
        int depth = 0; bool inStr = false; bool escape = false;
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (escape) { escape = false; continue; }
            if (c == '\\') { escape = true; continue; }
            if (c == '"') { inStr = !inStr; continue; }
            if (inStr) continue;
            if (c == '(' || c == '[' || c == '{') depth++;
            else if (c == ')' || c == ']' || c == '}') depth--;
            else if (c == ':' && depth == 0) return i;
        }
        return -1;
    }

    public string AsString() => Kind == ValueKind.String ? (Text ?? "") : "";
    public bool AsBool() => Kind == ValueKind.Bool && Bool;
    public double AsNumber() => Kind == ValueKind.Number ? Number : 0.0;
}
