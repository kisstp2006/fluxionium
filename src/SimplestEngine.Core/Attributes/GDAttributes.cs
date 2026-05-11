namespace SimplestEngine;

/// <summary>
/// Marks a class for ClassDB registration (Godot parity).
/// The source generator picks these up and emits build-time ClassInfo tables.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GDClassAttribute : Attribute
{
    public string? Name { get; }
    public string? Inherits { get; }
    public GDClassAttribute(string? name = null, string? inherits = null)
    { Name = name; Inherits = inherits; }
}

/// <summary>Exposes a method to ClassDB / scripting bindings / TSCN.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class GDMethodAttribute : Attribute
{
    public string? Name { get; }
    public GDMethodAttribute(string? name = null) { Name = name; }
}

/// <summary>Exposes a property to ClassDB / scripting / inspector / TSCN.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class GDPropertyAttribute : Attribute
{
    public string? Name { get; }
    public PropertyHint Hint { get; set; } = PropertyHint.None;
    public string? HintString { get; set; }
    public GDPropertyAttribute(string? name = null) { Name = name; }
}

/// <summary>Declares a signal on the class.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class GDSignalAttribute : Attribute
{
    public string Name { get; }
    public string[] ArgNames { get; }
    public GDSignalAttribute(string name, params string[] argNames)
    { Name = name; ArgNames = argNames ?? Array.Empty<string>(); }
}

/// <summary>Hint metadata for inspector / TSCN property handling.</summary>
public enum PropertyHint
{
    None = 0,
    Range,
    Enum,
    File,
    Dir,
    NodePath,
    Resource,
    ColorNoAlpha,
    MultilineText,
}
