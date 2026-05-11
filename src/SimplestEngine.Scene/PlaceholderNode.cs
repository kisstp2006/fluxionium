using SimplestEngine.Abi;

namespace SimplestEngine;

/// <summary>
/// Used when a TSCN file references a class we do NOT (yet) implement
/// (AnimationPlayer, TileMap, AudioStreamPlayer2D, etc.).
///
/// We still want to load the scene without crashing, AND we want to
/// preserve the original class name + every property value so the
/// editor can surface "missing implementation" warnings AND the
/// project can later be round-tripped (save) without data loss.
///
/// The placeholder pretends to be a Node and silently swallows
/// any Set() and Call() the loader / scripts make.
/// </summary>
[GDClass("PlaceholderNode", "Node")]
public sealed class PlaceholderNode : Node
{
    /// <summary>The class name the .tscn file actually asked for (e.g. "AnimationPlayer").</summary>
    public StringName OriginalClass { get; set; } = StringName.Empty;

    /// <summary>All properties applied to this node during scene loading, preserved verbatim.</summary>
    public Dictionary<StringName, Variant> CapturedProperties { get; } = new();

    public override StringName ClassName => OriginalClass.IsEmpty
        ? StringName.Get(nameof(PlaceholderNode))
        : OriginalClass;

    public override Variant Get(StringName property)
    {
        if (CapturedProperties.TryGetValue(property, out var v)) return v;
        return base.Get(property);
    }

    public override bool Set(StringName property, Variant value)
    {
        // Try base first - 'name', 'script', etc. should still work.
        if (base.Set(property, value)) return true;
        CapturedProperties[property] = value;
        return true;
    }
}
