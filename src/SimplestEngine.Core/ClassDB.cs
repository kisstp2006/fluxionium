namespace SimplestEngine;

public delegate Variant MethodInvoker(object instance, ReadOnlySpan<Variant> args);
public delegate Variant PropertyGetter(object instance);
public delegate void PropertySetter(object instance, Variant value);

public sealed class MethodInfo
{
    public required StringName Name { get; init; }
    public required MethodInvoker Invoker { get; init; }
    public VariantType ReturnType { get; init; } = VariantType.Nil;
    public VariantType[] ArgTypes { get; init; } = Array.Empty<VariantType>();
    public string[] ArgNames { get; init; } = Array.Empty<string>();
}

public sealed class PropertyInfo
{
    public required StringName Name { get; init; }
    public required VariantType Type { get; init; }
    public PropertyGetter? Getter { get; init; }
    public PropertySetter? Setter { get; init; }
    public PropertyHint Hint { get; init; } = PropertyHint.None;
    public string? HintString { get; init; }
    public Variant DefaultValue { get; init; } = Variant.Nil;
}

public sealed class SignalInfo
{
    public required StringName Name { get; init; }
    public string[] ArgNames { get; init; } = Array.Empty<string>();
}

public sealed class ClassInfo
{
    public required StringName Name { get; init; }
    public StringName Inherits { get; init; }
    public Type? RuntimeType { get; init; }
    public Func<object>? Factory { get; init; }
    public Dictionary<StringName, MethodInfo> Methods { get; } = new();
    public Dictionary<StringName, PropertyInfo> Properties { get; } = new();
    public Dictionary<StringName, SignalInfo> Signals { get; } = new();

    public MethodInfo? FindMethod(StringName n) =>
        Methods.TryGetValue(n, out var m) ? m
        : (Inherits.IsEmpty ? null : ClassDB.GetClass(Inherits)?.FindMethod(n));

    public PropertyInfo? FindProperty(StringName n) =>
        Properties.TryGetValue(n, out var p) ? p
        : (Inherits.IsEmpty ? null : ClassDB.GetClass(Inherits)?.FindProperty(n));

    public SignalInfo? FindSignal(StringName n)
    {
        if (Signals.TryGetValue(n, out var s)) return s;
        return Inherits.IsEmpty ? null : ClassDB.GetClass(Inherits)?.FindSignal(n);
    }
}

/// <summary>
/// Global class registry, Godot parity. Build-time-populated by the source generator
/// (each [GDClass] emits a `__Register__()` static method called from a module initializer).
/// </summary>
public static class ClassDB
{
    private static readonly Dictionary<StringName, ClassInfo> _classes = new();
    private static readonly Dictionary<Type, ClassInfo> _byType = new();

    public static IReadOnlyDictionary<StringName, ClassInfo> Classes => _classes;

    public static void Register(ClassInfo info)
    {
        _classes[info.Name] = info;
        if (info.RuntimeType is not null) _byType[info.RuntimeType] = info;
    }

    public static ClassInfo? GetClass(StringName name) =>
        _classes.TryGetValue(name, out var c) ? c : null;

    public static ClassInfo? GetClass(Type t) =>
        _byType.TryGetValue(t, out var c) ? c : null;

    public static ClassInfo? GetClass(object instance) => GetClass(instance.GetType());

    public static object? Instantiate(StringName name)
    {
        var info = GetClass(name);
        return info?.Factory?.Invoke();
    }

    public static bool IsParent(StringName child, StringName possibleParent)
    {
        var c = GetClass(child);
        while (c is not null)
        {
            if (c.Name == possibleParent) return true;
            if (c.Inherits.IsEmpty) break;
            c = GetClass(c.Inherits);
        }
        return false;
    }
}
