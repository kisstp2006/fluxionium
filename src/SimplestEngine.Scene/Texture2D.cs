using SimplestEngine.Abi;

namespace SimplestEngine;

/// <summary>
/// Pandemonium parity: Texture2D resource. Loadable from PNG/JPG by ResourceLoader.
/// Holds a server-side RID; the pixel bytes live in the rendering backend.
/// </summary>
[GDClass("Texture2D", "Resource")]
public class Texture2D : Resource
{
    public Rid Rid { get; internal set; }
    public int Width { get; internal set; }
    public int Height { get; internal set; }
    public Vector2i Size => new(Width, Height);

    public Texture2D(Rid rid, int width, int height)
    {
        Rid = rid;
        Width = width;
        Height = height;
        ResourceClass = StringName.Get("Texture2D");
    }

    public Texture2D() : this(default, 0, 0) { }
}

/// <summary>Pandemonium parity: base Resource.</summary>
[GDClass("Resource", "RefCounted")]
public class Resource : GodotObject, IResource
{
    public string ResourcePath { get; set; } = string.Empty;
    public StringName ResourceClass { get; protected set; } = StringName.Get("Resource");
    public StringName ResourceName { get; set; } = StringName.Empty;
}
