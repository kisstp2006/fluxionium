using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace SimplestEngine;

public enum VariantType : byte
{
    Nil = 0,
    Bool,
    Int,
    Float,
    String,
    Vector2,
    Vector2i,
    Rect2,
    Color,
    Transform2D,
    StringName,
    NodePath,
    Rid,
    Object,
    Callable,
    Signal,
    Array,
    Dictionary,
}

/// <summary>
/// Dynamically typed value. Hot path of the engine - keep this layout tight.
/// Value-type primitives stored inline (16-byte payload union),
/// reference types (string, object, array, dict, callable, NodePath) via _ref.
/// </summary>
public readonly struct Variant : IEquatable<Variant>
{
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal readonly struct Payload
    {
        [FieldOffset(0)] public readonly long I64;
        [FieldOffset(0)] public readonly double F64;
        [FieldOffset(0)] public readonly Vector2 V2;
        [FieldOffset(0)] public readonly Vector2i V2i;
        [FieldOffset(0)] public readonly Color C;
        [FieldOffset(0)] public readonly Rect2 R2;
        [FieldOffset(0)] public readonly ulong RidValue;

        public Payload(long v) : this() { I64 = v; }
        public Payload(double v) : this() { F64 = v; }
        public Payload(Vector2 v) : this() { V2 = v; }
        public Payload(Vector2i v) : this() { V2i = v; }
        public Payload(Color v) : this() { C = v; }
        public Payload(Rect2 v) : this() { R2 = v; }
        public Payload(ulong rid) : this() { RidValue = rid; }
    }

    internal readonly Payload _payload;
    internal readonly object? _ref;
    public readonly VariantType Type;

    public static readonly Variant Nil = default;

    public bool IsNil => Type == VariantType.Nil;

    private Variant(VariantType t, Payload p, object? r = null)
    {
        Type = t; _payload = p; _ref = r;
    }

    public bool AsBool() => Type switch
    {
        VariantType.Bool => _payload.I64 != 0,
        VariantType.Int => _payload.I64 != 0,
        VariantType.Float => _payload.F64 != 0,
        _ => false,
    };

    public long AsInt() => Type switch
    {
        VariantType.Bool or VariantType.Int => _payload.I64,
        VariantType.Float => (long)_payload.F64,
        _ => 0,
    };

    public double AsFloat() => Type switch
    {
        VariantType.Bool or VariantType.Int => _payload.I64,
        VariantType.Float => _payload.F64,
        _ => 0.0,
    };

    public Vector2 AsVector2() => Type == VariantType.Vector2 ? _payload.V2 : default;
    public Vector2i AsVector2i() => Type == VariantType.Vector2i ? _payload.V2i : default;
    public Color AsColor() => Type == VariantType.Color ? _payload.C : default;
    public Rect2 AsRect2() => Type == VariantType.Rect2 ? _payload.R2 : default;
    public Rid AsRid() => Type == VariantType.Rid ? new Rid(_payload.RidValue) : default;
    public StringName AsStringName() => Type == VariantType.StringName ? new StringName((uint)_payload.I64) : StringName.Empty;
    public string AsString() => Type == VariantType.String ? ((string?)_ref ?? string.Empty) : (_ref?.ToString() ?? string.Empty);
    public object? AsObject() => _ref;
    public T? AsRef<T>() where T : class => _ref as T;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variant From(bool v) => new(VariantType.Bool, new Payload(v ? 1L : 0L));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variant From(int v) => new(VariantType.Int, new Payload((long)v));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variant From(long v) => new(VariantType.Int, new Payload(v));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variant From(float v) => new(VariantType.Float, new Payload((double)v));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variant From(double v) => new(VariantType.Float, new Payload(v));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variant From(Vector2 v) => new(VariantType.Vector2, new Payload(v));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variant From(Vector2i v) => new(VariantType.Vector2i, new Payload(v));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variant From(Color v) => new(VariantType.Color, new Payload(v));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variant From(Rect2 v) => new(VariantType.Rect2, new Payload(v));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variant From(Rid v) => new(VariantType.Rid, new Payload(v.Value));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variant From(StringName v) => new(VariantType.StringName, new Payload((long)v.Id));
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Variant From(string? v) => v is null ? Nil : new(VariantType.String, default, v);

    public static Variant FromObject(object? v) =>
        v is null ? Nil : new Variant(VariantType.Object, default, v);

    public static Variant FromArray(object array) => new(VariantType.Array, default, array);
    public static Variant FromDictionary(object dict) => new(VariantType.Dictionary, default, dict);
    public static Variant FromCallable(object callable) => new(VariantType.Callable, default, callable);
    public static Variant FromNodePath(object nodePath) => new(VariantType.NodePath, default, nodePath);

    public bool Equals(Variant other)
    {
        if (Type != other.Type) return false;
        return Type switch
        {
            VariantType.Nil => true,
            VariantType.Bool or VariantType.Int or VariantType.StringName => _payload.I64 == other._payload.I64,
            VariantType.Float => _payload.F64 == other._payload.F64,
            VariantType.Rid => _payload.RidValue == other._payload.RidValue,
            VariantType.Vector2 => _payload.V2 == other._payload.V2,
            VariantType.Vector2i => _payload.V2i == other._payload.V2i,
            VariantType.Color => _payload.C == other._payload.C,
            VariantType.Rect2 => _payload.R2 == other._payload.R2,
            _ => ReferenceEquals(_ref, other._ref) || (_ref?.Equals(other._ref) ?? false),
        };
    }

    public override bool Equals(object? obj) => obj is Variant v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Type, _payload.I64, _ref);

    public override string ToString() => Type switch
    {
        VariantType.Nil => "<nil>",
        VariantType.Bool => (_payload.I64 != 0).ToString(),
        VariantType.Int => _payload.I64.ToString(),
        VariantType.Float => _payload.F64.ToString(System.Globalization.CultureInfo.InvariantCulture),
        VariantType.String => (string?)_ref ?? string.Empty,
        VariantType.StringName => StringNameInterner.GetString((uint)_payload.I64),
        VariantType.Vector2 => _payload.V2.ToString(),
        VariantType.Vector2i => _payload.V2i.ToString(),
        VariantType.Color => _payload.C.ToString(),
        VariantType.Rect2 => _payload.R2.ToString(),
        VariantType.Rid => new Rid(_payload.RidValue).ToString(),
        VariantType.Object => _ref?.ToString() ?? "<null>",
        _ => $"Variant<{Type}>",
    };

    public static bool operator ==(Variant a, Variant b) => a.Equals(b);
    public static bool operator !=(Variant a, Variant b) => !a.Equals(b);
}
