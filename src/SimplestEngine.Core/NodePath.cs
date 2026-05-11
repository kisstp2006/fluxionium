namespace SimplestEngine;

/// <summary>
/// Godot-style node path: "../Player/Sprite2D:position:x".
/// Tokens stored as StringName for fast comparison.
/// Pre-parsed into name tokens + property subnames.
/// </summary>
public sealed class NodePath : IEquatable<NodePath>
{
    public bool IsAbsolute { get; }
    public StringName[] Names { get; }
    public StringName[] SubNames { get; }
    public string OriginalString { get; }

    public static readonly NodePath Empty = new(string.Empty);

    public NodePath(string path)
    {
        OriginalString = path ?? string.Empty;
        if (string.IsNullOrEmpty(path))
        {
            Names = Array.Empty<StringName>();
            SubNames = Array.Empty<StringName>();
            return;
        }

        int colon = path.IndexOf(':');
        string names = colon < 0 ? path : path[..colon];
        string subs = colon < 0 ? string.Empty : path[(colon + 1)..];

        IsAbsolute = names.StartsWith('/');
        if (IsAbsolute) names = names[1..];

        Names = string.IsNullOrEmpty(names)
            ? Array.Empty<StringName>()
            : Array.ConvertAll(names.Split('/', StringSplitOptions.RemoveEmptyEntries), StringName.Get);
        SubNames = string.IsNullOrEmpty(subs)
            ? Array.Empty<StringName>()
            : Array.ConvertAll(subs.Split(':', StringSplitOptions.RemoveEmptyEntries), StringName.Get);
    }

    public bool IsEmpty => Names.Length == 0 && SubNames.Length == 0;
    public int NameCount => Names.Length;
    public int SubNameCount => SubNames.Length;

    public StringName GetName(int index) => Names[index];
    public StringName GetSubName(int index) => SubNames[index];

    public bool Equals(NodePath? other) =>
        other is not null
        && IsAbsolute == other.IsAbsolute
        && Names.AsSpan().SequenceEqual(other.Names)
        && SubNames.AsSpan().SequenceEqual(other.SubNames);

    public override bool Equals(object? obj) => obj is NodePath np && Equals(np);
    public override int GetHashCode()
    {
        var hc = new HashCode();
        hc.Add(IsAbsolute);
        foreach (var n in Names) hc.Add(n);
        foreach (var s in SubNames) hc.Add(s);
        return hc.ToHashCode();
    }

    public override string ToString() => OriginalString;
}
