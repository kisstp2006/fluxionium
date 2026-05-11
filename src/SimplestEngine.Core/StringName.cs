using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace SimplestEngine;

/// <summary>
/// Interned string identifier. First-class optimization throughout the engine.
/// Property/signal/method/group names are always compared by ID, never by string.
/// </summary>
public readonly struct StringName : IEquatable<StringName>
{
    public readonly uint Id;

    public static readonly StringName Empty = default;

    internal StringName(uint id) { Id = id; }

    public bool IsEmpty { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => Id == 0; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StringName Get(string? value)
    {
        if (string.IsNullOrEmpty(value)) return Empty;
        return new StringName(StringNameInterner.Intern(value));
    }

    public override string ToString() => Id == 0 ? string.Empty : StringNameInterner.GetString(Id);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(StringName other) => Id == other.Id;

    public override bool Equals(object? obj) => obj is StringName s && s.Id == Id;
    public override int GetHashCode() => (int)Id;

    public static bool operator ==(StringName a, StringName b) => a.Id == b.Id;
    public static bool operator !=(StringName a, StringName b) => a.Id != b.Id;

    public static implicit operator StringName(string s) => Get(s);
}

/// <summary>Global intern pool backing <see cref="StringName"/>. Thread-safe.</summary>
public static class StringNameInterner
{
    private static readonly ConcurrentDictionary<string, uint> _toId =
        new(StringComparer.Ordinal);
    private static readonly List<string> _strings = new() { string.Empty };
    private static readonly object _addLock = new();

    public static uint Intern(string s)
    {
        if (_toId.TryGetValue(s, out var id)) return id;
        lock (_addLock)
        {
            if (_toId.TryGetValue(s, out id)) return id;
            id = (uint)_strings.Count;
            _strings.Add(s);
            _toId[s] = id;
            return id;
        }
    }

    public static string GetString(uint id)
    {
        lock (_addLock)
        {
            return id < _strings.Count ? _strings[(int)id] : string.Empty;
        }
    }

    public static int Count
    {
        get { lock (_addLock) return _strings.Count; }
    }
}
