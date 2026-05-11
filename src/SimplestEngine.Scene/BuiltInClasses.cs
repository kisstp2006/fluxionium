namespace SimplestEngine;

/// <summary>
/// Manual ClassDB bootstrap for v1. Until the Roslyn source generator
/// is in place, every [GDClass] is registered here at startup.
/// </summary>
public static class BuiltInClasses
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        // Object base
        ClassDB.Register(new ClassInfo
        {
            Name = StringName.Get("Object"), RuntimeType = typeof(GodotObject),
            Factory = () => new GodotObject(),
        });

        // Resource
        ClassDB.Register(new ClassInfo
        {
            Name = StringName.Get("Resource"), Inherits = StringName.Get("Object"),
            RuntimeType = typeof(Resource), Factory = () => new Resource(),
        });

        // Texture2D / Texture (Pandemonium name parity)
        ClassDB.Register(new ClassInfo
        {
            Name = StringName.Get("Texture2D"), Inherits = StringName.Get("Resource"),
            RuntimeType = typeof(Texture2D), Factory = () => new Texture2D(),
        });
        ClassDB.Register(new ClassInfo
        {
            Name = StringName.Get("Texture"), Inherits = StringName.Get("Resource"),
            RuntimeType = typeof(Texture2D), Factory = () => new Texture2D(),
        });

        // Node
        RegisterNode<Node>("Node", "Object");

        // Viewport
        RegisterNode<Viewport>("Viewport", "Node");

        // CanvasItem
        RegisterCanvasItem<CanvasItem>("CanvasItem");

        // Node2D
        RegisterNode2D<Node2D>("Node2D");

        // Sprite (Pandemonium 2D sprite, Godot 3.x name)
        RegisterNode2D<Sprite>("Sprite");
        // Also alias Sprite2D for Godot 4 compatibility
        RegisterNode2D<Sprite>("Sprite2D");

        RegisterNode2D<Camera2D>("Camera2D");
        RegisterNode2D<Label>("Label");
        RegisterNode2D<ColorRect>("ColorRect");

        // Godot 3 'Position2D' and Godot 4 'Marker2D' are both bare transform pins -
        // we map them onto Node2D so scenes that use them still position correctly.
        RegisterNode2D<Node2D>("Position2D");
        RegisterNode2D<Node2D>("Marker2D");

        // CanvasLayer: in Godot it owns its own transform stack, but for scene-load
        // compatibility a plain Node is enough - children still appear under it.
        RegisterNode<Node>("CanvasLayer", "Node");
        RegisterNode<Node>("ParallaxBackground", "Node");
        RegisterNode<Node>("ParallaxLayer", "Node");

        // PlaceholderNode is what the loader instantiates when nothing else fits;
        // expose it so projects can save / re-open without losing the marker.
        ClassDB.Register(new ClassInfo
        {
            Name = StringName.Get("PlaceholderNode"),
            Inherits = StringName.Get("Node"),
            RuntimeType = typeof(PlaceholderNode),
            Factory = () => new PlaceholderNode(),
        });

        RegisterNode<Timer>("Timer", "Node",
            signals: new[] { new SignalInfo { Name = StringName.Get("timeout") } });

        // --- Physics 2D (Pandemonium hierarchy) ----------------------------
        RegisterNode2D<StaticBody2D>("StaticBody2D");
        RegisterNode2D<RigidBody2D>("RigidBody2D");
        RegisterNode2D<KinematicBody2D>("KinematicBody2D");
        RegisterNode2D<CharacterBody2D>("CharacterBody2D");
        RegisterNode2D<Area2D>("Area2D");
        RegisterNode2D<CollisionShape2D>("CollisionShape2D");
        RegisterNode2D<CollisionPolygon2D>("CollisionPolygon2D");

        ClassDB.Register(new ClassInfo
        {
            Name = StringName.Get("Shape2D"), Inherits = StringName.Get("Resource"),
            RuntimeType = typeof(Shape2D),
        });
        ClassDB.Register(new ClassInfo
        {
            Name = StringName.Get("CircleShape2D"), Inherits = StringName.Get("Shape2D"),
            RuntimeType = typeof(CircleShape2D), Factory = () => new CircleShape2D(),
        });
        ClassDB.Register(new ClassInfo
        {
            Name = StringName.Get("RectangleShape2D"), Inherits = StringName.Get("Shape2D"),
            RuntimeType = typeof(RectangleShape2D), Factory = () => new RectangleShape2D(),
        });
    }

    private static void RegisterNode<T>(string name, string inherits, SignalInfo[]? signals = null)
        where T : Node, new()
    {
        var ci = new ClassInfo
        {
            Name = StringName.Get(name),
            Inherits = StringName.Get(inherits),
            RuntimeType = typeof(T),
            Factory = () => new T(),
        };
        AddNodeProperties(ci);
        if (signals is not null)
            foreach (var s in signals) ci.Signals[s.Name] = s;
        ClassDB.Register(ci);
    }

    private static void RegisterCanvasItem<T>(string name) where T : CanvasItem, new()
    {
        var ci = new ClassInfo
        {
            Name = StringName.Get(name),
            Inherits = StringName.Get("Node"),
            RuntimeType = typeof(T),
            Factory = () => new T(),
        };
        AddNodeProperties(ci);
        AddCanvasItemProperties(ci);
        ClassDB.Register(ci);
    }

    private static void RegisterNode2D<T>(string name) where T : Node2D, new()
    {
        var ci = new ClassInfo
        {
            Name = StringName.Get(name),
            Inherits = StringName.Get("Node2D"),
            RuntimeType = typeof(T),
            Factory = () => new T(),
        };
        AddNodeProperties(ci);
        AddCanvasItemProperties(ci);
        AddNode2DProperties(ci);
        AddSpecific(ci, name);
        if (name == "Node2D")
            ClassDB.Register(new ClassInfo
            {
                Name = StringName.Get("Node2D"),
                Inherits = StringName.Get("CanvasItem"),
                RuntimeType = typeof(Node2D),
                Factory = () => new Node2D(),
            });
        else
            ClassDB.Register(ci);
    }

    private static void AddNodeProperties(ClassInfo ci)
    {
        ci.Properties[StringName.Get("name")] = new PropertyInfo
        {
            Name = StringName.Get("name"), Type = VariantType.StringName,
            Getter = o => Variant.From(((Node)o).Name),
            Setter = (o, v) => ((Node)o).Name = v.Type == VariantType.String
                ? StringName.Get(v.AsString())
                : v.AsStringName(),
        };
    }

    private static void AddCanvasItemProperties(ClassInfo ci)
    {
        ci.Properties[StringName.Get("visible")] = new PropertyInfo
        {
            Name = StringName.Get("visible"), Type = VariantType.Bool,
            Getter = o => Variant.From(((CanvasItem)o).Visible),
            Setter = (o, v) => ((CanvasItem)o).Visible = v.AsBool(),
        };
        ci.Properties[StringName.Get("modulate")] = new PropertyInfo
        {
            Name = StringName.Get("modulate"), Type = VariantType.Color,
            Getter = o => Variant.From(((CanvasItem)o).Modulate),
            Setter = (o, v) => ((CanvasItem)o).Modulate = v.AsColor(),
        };
        ci.Properties[StringName.Get("z_index")] = new PropertyInfo
        {
            Name = StringName.Get("z_index"), Type = VariantType.Int,
            Getter = o => Variant.From((int)((CanvasItem)o).ZIndex),
            Setter = (o, v) => ((CanvasItem)o).ZIndex = (int)v.AsInt(),
        };
    }

    private static void AddNode2DProperties(ClassInfo ci)
    {
        ci.Properties[StringName.Get("position")] = new PropertyInfo
        {
            Name = StringName.Get("position"), Type = VariantType.Vector2,
            Getter = o => Variant.From(((Node2D)o).Position),
            Setter = (o, v) => ((Node2D)o).Position = v.AsVector2(),
        };
        ci.Properties[StringName.Get("rotation")] = new PropertyInfo
        {
            Name = StringName.Get("rotation"), Type = VariantType.Float,
            Getter = o => Variant.From(((Node2D)o).Rotation),
            Setter = (o, v) => ((Node2D)o).Rotation = (float)v.AsFloat(),
        };
        ci.Properties[StringName.Get("rotation_degrees")] = new PropertyInfo
        {
            Name = StringName.Get("rotation_degrees"), Type = VariantType.Float,
            Getter = o => Variant.From(((Node2D)o).RotationDegrees),
            Setter = (o, v) => ((Node2D)o).RotationDegrees = (float)v.AsFloat(),
        };
        ci.Properties[StringName.Get("scale")] = new PropertyInfo
        {
            Name = StringName.Get("scale"), Type = VariantType.Vector2,
            Getter = o => Variant.From(((Node2D)o).Scale),
            Setter = (o, v) => ((Node2D)o).Scale = v.AsVector2(),
        };
    }

    private static void AddSpecific(ClassInfo ci, string name)
    {
        switch (name)
        {
            case "Sprite":
            case "Sprite2D":
                ci.Properties[StringName.Get("texture")] = new PropertyInfo
                {
                    Name = StringName.Get("texture"), Type = VariantType.Object,
                    Getter = o => Variant.FromObject(((Sprite)o).Texture),
                    Setter = (o, v) => ((Sprite)o).Texture = v.AsRef<Texture2D>(),
                };
                ci.Properties[StringName.Get("centered")] = new PropertyInfo
                {
                    Name = StringName.Get("centered"), Type = VariantType.Bool,
                    Getter = o => Variant.From(((Sprite)o).Centered),
                    Setter = (o, v) => ((Sprite)o).Centered = v.AsBool(),
                };
                ci.Properties[StringName.Get("offset")] = new PropertyInfo
                {
                    Name = StringName.Get("offset"), Type = VariantType.Vector2,
                    Getter = o => Variant.From(((Sprite)o).Offset),
                    Setter = (o, v) => ((Sprite)o).Offset = v.AsVector2(),
                };
                ci.Properties[StringName.Get("flip_h")] = new PropertyInfo
                {
                    Name = StringName.Get("flip_h"), Type = VariantType.Bool,
                    Getter = o => Variant.From(((Sprite)o).FlipH),
                    Setter = (o, v) => ((Sprite)o).FlipH = v.AsBool(),
                };
                ci.Properties[StringName.Get("flip_v")] = new PropertyInfo
                {
                    Name = StringName.Get("flip_v"), Type = VariantType.Bool,
                    Getter = o => Variant.From(((Sprite)o).FlipV),
                    Setter = (o, v) => ((Sprite)o).FlipV = v.AsBool(),
                };
                break;
            case "Label":
                ci.Properties[StringName.Get("text")] = new PropertyInfo
                {
                    Name = StringName.Get("text"), Type = VariantType.String,
                    Getter = o => Variant.From(((Label)o).Text),
                    Setter = (o, v) => ((Label)o).Text = v.AsString(),
                };
                ci.Properties[StringName.Get("font_color")] = new PropertyInfo
                {
                    Name = StringName.Get("font_color"), Type = VariantType.Color,
                    Getter = o => Variant.From(((Label)o).FontColor),
                    Setter = (o, v) => ((Label)o).FontColor = v.AsColor(),
                };
                break;
            case "ColorRect":
                ci.Properties[StringName.Get("size")] = new PropertyInfo
                {
                    Name = StringName.Get("size"), Type = VariantType.Vector2,
                    Getter = o => Variant.From(((ColorRect)o).Size),
                    Setter = (o, v) => ((ColorRect)o).Size = v.AsVector2(),
                };
                ci.Properties[StringName.Get("color")] = new PropertyInfo
                {
                    Name = StringName.Get("color"), Type = VariantType.Color,
                    Getter = o => Variant.From(((ColorRect)o).Color),
                    Setter = (o, v) => ((ColorRect)o).Color = v.AsColor(),
                };
                break;
            case "Camera2D":
                ci.Properties[StringName.Get("current")] = new PropertyInfo
                {
                    Name = StringName.Get("current"), Type = VariantType.Bool,
                    Getter = o => Variant.From(((Camera2D)o).Current),
                    Setter = (o, v) => ((Camera2D)o).Current = v.AsBool(),
                };
                break;
            case "Timer":
                ci.Properties[StringName.Get("wait_time")] = new PropertyInfo
                {
                    Name = StringName.Get("wait_time"), Type = VariantType.Float,
                    Getter = o => Variant.From(((Timer)o).WaitTime),
                    Setter = (o, v) => ((Timer)o).WaitTime = (float)v.AsFloat(),
                };
                ci.Properties[StringName.Get("one_shot")] = new PropertyInfo
                {
                    Name = StringName.Get("one_shot"), Type = VariantType.Bool,
                    Getter = o => Variant.From(((Timer)o).OneShot),
                    Setter = (o, v) => ((Timer)o).OneShot = v.AsBool(),
                };
                ci.Properties[StringName.Get("autostart")] = new PropertyInfo
                {
                    Name = StringName.Get("autostart"), Type = VariantType.Bool,
                    Getter = o => Variant.From(((Timer)o).Autostart),
                    Setter = (o, v) => ((Timer)o).Autostart = v.AsBool(),
                };
                break;
        }
    }
}
