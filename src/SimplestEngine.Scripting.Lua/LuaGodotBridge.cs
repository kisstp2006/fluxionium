using MoonSharp.Interpreter;
using SimplestEngine.Abi;

namespace SimplestEngine.Scripting.Lua;

/// <summary>
/// 1:1 Godot-style API exposed in Lua. Globals: self, Vector2, Color, Rect2,
/// NodePath, Callable, print, randf, signal(), export(), onready(), etc.
/// All accesses go through IEngineAPI - bridge never touches Node internals.
/// </summary>
public static class LuaGodotBridge
{
    public static void SetupGlobals(Script lua, IGodotObjectAPI owner, LuaScriptLanguage lang)
    {
        var engine = lang.Engine ?? throw new InvalidOperationException("LuaScriptLanguage.Engine not set.");

        // --- 'self' as a proxy that delegates to IEngineAPI ----
        var selfTable = new Table(lua);
        var selfMeta = new Table(lua);
        selfMeta["__index"] = DynValue.NewCallback((ctx, args) =>
        {
            var key = args[1].String;
            // method dispatch via Callable wrapper
            if (engine is null) return DynValue.Nil;
            // Try property first
            var v = engine.GetProperty(owner, StringName.Get(key));
            if (v.Type != VariantType.Nil) return ToLua(lua, v);
            // Fallback: return a function that calls the method
            return DynValue.NewCallback((c2, ca2) =>
            {
                // skip self argument
                var localArgs = new DynValue[Math.Max(0, ca2.Count - 1)];
                for (int i = 1; i < ca2.Count; i++) localArgs[i - 1] = ca2[i];
                var variantArgs = new Variant[localArgs.Length];
                for (int i = 0; i < localArgs.Length; i++) variantArgs[i] = FromLua(localArgs[i]);
                var ret = engine.CallMethod(owner, StringName.Get(key), variantArgs);
                return ToLua(lua, ret);
            });
        });
        selfMeta["__newindex"] = DynValue.NewCallback((ctx, args) =>
        {
            engine.SetProperty(owner, StringName.Get(args[1].String), FromLua(args[2]));
            return DynValue.Nil;
        });
        selfTable.MetaTable = selfMeta;
        lua.Globals["self"] = selfTable;

        // --- Built-in value-type constructors --------------------------------
        // MoonSharp CallbackArguments is 0-indexed for free function calls.
        lua.Globals["Vector2"] = DynValue.NewCallback((c, a) =>
        {
            float x = (float)(a.Count > 0 ? a[0].Number : 0);
            float y = (float)(a.Count > 1 ? a[1].Number : 0);
            return ToLua(lua, Variant.From(new Vector2(x, y)));
        });
        lua.Globals["Color"] = DynValue.NewCallback((c, a) =>
        {
            float r = (float)(a.Count > 0 ? a[0].Number : 1);
            float g = (float)(a.Count > 1 ? a[1].Number : 1);
            float b = (float)(a.Count > 2 ? a[2].Number : 1);
            float al = (float)(a.Count > 3 ? a[3].Number : 1);
            return ToLua(lua, Variant.From(new Color(r, g, b, al)));
        });
        lua.Globals["Rect2"] = DynValue.NewCallback((c, a) =>
        {
            float x = (float)(a.Count > 0 ? a[0].Number : 0);
            float y = (float)(a.Count > 1 ? a[1].Number : 0);
            float w = (float)(a.Count > 2 ? a[2].Number : 0);
            float h = (float)(a.Count > 3 ? a[3].Number : 0);
            return ToLua(lua, Variant.From(new Rect2(x, y, w, h)));
        });
        lua.Globals["NodePath"] = DynValue.NewCallback((c, a) =>
            ToLua(lua, Variant.FromNodePath(new NodePath(a.Count > 0 ? a[0].String : ""))));

        // Callable(obj, "method") - keep simple
        lua.Globals["Callable"] = DynValue.NewCallback((c, a) =>
        {
            // We can't directly forward to engine.CallMethod from lua w/o knowing the API user-facing target;
            // return a closure that does the right thing at call time.
            var target = a[1];
            var methodName = a.Count > 2 ? a[2].String : "";
            return DynValue.NewCallback((c2, ca2) =>
            {
                // call target:methodName(args)
                if (target.Type == DataType.Table)
                {
                    var fn = target.Table.Get("__call_method");
                    if (fn.Type == DataType.Function) return lua.Call(fn, DynValue.NewString(methodName));
                }
                if (target.Type == DataType.UserData)
                {
                    // future: invoke via engine ABI
                }
                return DynValue.Nil;
            });
        });

        // print(...) -> stdout. MoonSharp's CallbackArguments is 0-indexed for regular
        // function calls (no implicit `self` unless invoked via `:`), so iterate from 0.
        // MoonSharp's ToPrintString does not honour metatable __tostring, so we format
        // our own typed value-tables (Vector2/Color/Rect2) here.
        lua.Globals["print"] = DynValue.NewCallback((c, a) =>
        {
            Console.WriteLine(FormatPrintArgs(a));
            return DynValue.Nil;
        });
        lua.Globals["printerr"] = DynValue.NewCallback((c, a) =>
        {
            Console.Error.WriteLine(FormatPrintArgs(a));
            return DynValue.Nil;
        });
        lua.Globals["tostring"] = DynValue.NewCallback((c, a) =>
            DynValue.NewString(a.Count > 0 ? Pretty(a[0]) : ""));

        // randf / randi / deg_to_rad / rad_to_deg
        var rand = new Random();
        lua.Globals["randf"] = DynValue.NewCallback((c, a) => DynValue.NewNumber(rand.NextDouble()));
        lua.Globals["randi"] = DynValue.NewCallback((c, a) => DynValue.NewNumber(rand.Next()));
        lua.Globals["deg_to_rad"] = DynValue.NewCallback((c, a) => DynValue.NewNumber(a[0].Number * Math.PI / 180.0));
        lua.Globals["rad_to_deg"] = DynValue.NewCallback((c, a) => DynValue.NewNumber(a[0].Number * 180.0 / Math.PI));

        // VariantType constants for export(name, TYPE, default)
        var vtypes = new Table(lua);
        vtypes["TYPE_NIL"] = DynValue.NewNumber((int)VariantType.Nil);
        vtypes["TYPE_BOOL"] = DynValue.NewNumber((int)VariantType.Bool);
        vtypes["TYPE_INT"] = DynValue.NewNumber((int)VariantType.Int);
        vtypes["TYPE_FLOAT"] = DynValue.NewNumber((int)VariantType.Float);
        vtypes["TYPE_STRING"] = DynValue.NewNumber((int)VariantType.String);
        vtypes["TYPE_VECTOR2"] = DynValue.NewNumber((int)VariantType.Vector2);
        vtypes["TYPE_COLOR"] = DynValue.NewNumber((int)VariantType.Color);
        foreach (var k in vtypes.Keys) lua.Globals[k] = vtypes.Get(k);

        // signal(name, ...args) - records signal declaration (declarative)
        lua.Globals["signal"] = DynValue.NewCallback((c, a) => DynValue.Nil);

        // export(name, type, default) - records exported var
        lua.Globals["export"] = DynValue.NewCallback((c, a) => DynValue.Nil);

        // onready(name, nodePath) - resolves $NodePath in _ready
        lua.Globals["onready"] = DynValue.NewCallback((c, a) => DynValue.Nil);

        // extends("ClassName") - records inheritance (no metatable chain in v1)
        lua.Globals["extends"] = DynValue.NewCallback((c, a) => DynValue.Nil);

        // tool() - allow editor-mode operation
        lua.Globals["tool"] = DynValue.NewCallback((c, a) => DynValue.Nil);

        // --- Input singleton (Godot's `Input.*` API) -----------------------
        // Lazily forwards to whichever IInputServer is currently wired into the
        // engine. We rebuild the table each script setup so live IEngineAPI
        // changes (test harness, headless mode) are picked up.
        // Input is invoked Godot-style (Input.is_action_pressed("foo")) - regular call,
        // no implicit self, so arguments are 0-indexed.
        var inputTable = new Table(lua);
        inputTable["has_action"] = DynValue.NewCallback((c, a) =>
            DynValue.NewBoolean(engine.Input?.HasAction(StringName.Get(a[0].String)) ?? false));
        inputTable["is_action_pressed"] = DynValue.NewCallback((c, a) =>
            DynValue.NewBoolean(engine.Input?.IsActionPressed(StringName.Get(a[0].String)) ?? false));
        inputTable["is_action_just_pressed"] = DynValue.NewCallback((c, a) =>
            DynValue.NewBoolean(engine.Input?.IsActionJustPressed(StringName.Get(a[0].String)) ?? false));
        inputTable["is_action_just_released"] = DynValue.NewCallback((c, a) =>
            DynValue.NewBoolean(engine.Input?.IsActionJustReleased(StringName.Get(a[0].String)) ?? false));
        inputTable["get_action_strength"] = DynValue.NewCallback((c, a) =>
            DynValue.NewNumber(engine.Input?.GetActionStrength(StringName.Get(a[0].String)) ?? 0f));
        inputTable["get_vector"] = DynValue.NewCallback((c, a) =>
        {
            if (engine.Input is null) return Vec2ToLua(lua, Vector2.Zero);
            var v = engine.Input.GetVector(
                StringName.Get(a[0].String),
                StringName.Get(a[1].String),
                StringName.Get(a[2].String),
                StringName.Get(a[3].String));
            return Vec2ToLua(lua, v);
        });
        inputTable["get_mouse_position"] = DynValue.NewCallback((c, a) =>
            Vec2ToLua(lua, engine.Input?.GetMousePosition() ?? Vector2.Zero));
        lua.Globals["Input"] = inputTable;
    }

    private static string FormatPrintArgs(CallbackArguments a)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < a.Count; i++)
        {
            if (i > 0) sb.Append('\t');
            sb.Append(Pretty(a[i]));
        }
        return sb.ToString();
    }

    private static string Pretty(DynValue v)
    {
        if (v.Type == DataType.Table)
        {
            var t = v.Table;
            var type = t.Get("_type").String;
            switch (type)
            {
                case "Vector2":
                    return $"({t.Get("x").Number:0.###}, {t.Get("y").Number:0.###})";
                case "Color":
                    return $"Color({t.Get("r").Number:0.##}, {t.Get("g").Number:0.##}, {t.Get("b").Number:0.##}, {t.Get("a").Number:0.##})";
                case "Rect2":
                    return $"Rect2({t.Get("x").Number:0.##}, {t.Get("y").Number:0.##}, {t.Get("w").Number:0.##}, {t.Get("h").Number:0.##})";
            }
        }
        return v.ToPrintString();
    }

    // --- Variant <-> Lua conversion ----------------------------------------

    public static DynValue ToLua(Script lua, Variant v)
    {
        return v.Type switch
        {
            VariantType.Nil => DynValue.Nil,
            VariantType.Bool => DynValue.NewBoolean(v.AsBool()),
            VariantType.Int => DynValue.NewNumber(v.AsInt()),
            VariantType.Float => DynValue.NewNumber(v.AsFloat()),
            VariantType.String => DynValue.NewString(v.AsString()),
            VariantType.StringName => DynValue.NewString(v.AsStringName().ToString()),
            VariantType.Vector2 => Vec2ToLua(lua, v.AsVector2()),
            VariantType.Color => ColorToLua(lua, v.AsColor()),
            VariantType.Rect2 => Rect2ToLua(lua, v.AsRect2()),
            VariantType.Object => v.AsObject() is null ? DynValue.Nil : DynValue.FromObject(lua, v.AsObject()!),
            _ => DynValue.Nil,
        };
    }

    public static Variant FromLua(DynValue v)
    {
        return v.Type switch
        {
            DataType.Nil or DataType.Void => Variant.Nil,
            DataType.Boolean => Variant.From(v.Boolean),
            DataType.Number => Variant.From(v.Number),
            DataType.String => Variant.From(v.String),
            DataType.Table => TableToVariant(v.Table),
            DataType.UserData => Variant.FromObject(v.UserData.Object),
            _ => Variant.Nil,
        };
    }

    private static Variant TableToVariant(Table t)
    {
        // Recognise Vector2 / Color / Rect2 tables emitted by our constructors.
        if (t.Get("_type").String == "Vector2")
            return Variant.From(new Vector2((float)t.Get("x").Number, (float)t.Get("y").Number));
        if (t.Get("_type").String == "Color")
            return Variant.From(new Color(
                (float)t.Get("r").Number, (float)t.Get("g").Number,
                (float)t.Get("b").Number, (float)t.Get("a").Number));
        if (t.Get("_type").String == "Rect2")
            return Variant.From(new Rect2(
                (float)t.Get("x").Number, (float)t.Get("y").Number,
                (float)t.Get("w").Number, (float)t.Get("h").Number));
        return Variant.Nil;
    }

    private static DynValue Vec2ToLua(Script lua, Vector2 v)
    {
        var t = new Table(lua);
        t["_type"] = DynValue.NewString("Vector2");
        t["x"] = DynValue.NewNumber(v.X);
        t["y"] = DynValue.NewNumber(v.Y);
        // Binary metamethods receive (lhs, rhs) as args[0], args[1].
        var mt = new Table(lua);
        mt["__add"] = DynValue.NewCallback((c, a) =>
        {
            var av = FromLua(a[0]).AsVector2();
            var bv = FromLua(a[1]).AsVector2();
            return Vec2ToLua(lua, av + bv);
        });
        mt["__sub"] = DynValue.NewCallback((c, a) =>
        {
            var av = FromLua(a[0]).AsVector2();
            var bv = FromLua(a[1]).AsVector2();
            return Vec2ToLua(lua, av - bv);
        });
        mt["__mul"] = DynValue.NewCallback((c, a) =>
        {
            var av = FromLua(a[0]);
            var bv = FromLua(a[1]);
            if (av.Type == VariantType.Vector2 && bv.Type == VariantType.Float)
                return Vec2ToLua(lua, av.AsVector2() * (float)bv.AsFloat());
            if (av.Type == VariantType.Vector2 && bv.Type == VariantType.Int)
                return Vec2ToLua(lua, av.AsVector2() * (float)bv.AsInt());
            if (av.Type == VariantType.Float && bv.Type == VariantType.Vector2)
                return Vec2ToLua(lua, bv.AsVector2() * (float)av.AsFloat());
            if (av.Type == VariantType.Vector2 && bv.Type == VariantType.Vector2)
                return Vec2ToLua(lua, av.AsVector2() * bv.AsVector2());
            return DynValue.Nil;
        });
        mt["__unm"] = DynValue.NewCallback((c, a) =>
        {
            var av = FromLua(a[0]).AsVector2();
            return Vec2ToLua(lua, -av);
        });
        mt["__tostring"] = DynValue.NewCallback((c, a) =>
            DynValue.NewString(FromLua(a[0]).AsVector2().ToString()));
        t.MetaTable = mt;
        return DynValue.NewTable(t);
    }

    private static DynValue ColorToLua(Script lua, Color c)
    {
        var t = new Table(lua);
        t["_type"] = DynValue.NewString("Color");
        t["r"] = DynValue.NewNumber(c.R);
        t["g"] = DynValue.NewNumber(c.G);
        t["b"] = DynValue.NewNumber(c.B);
        t["a"] = DynValue.NewNumber(c.A);
        return DynValue.NewTable(t);
    }

    private static DynValue Rect2ToLua(Script lua, Rect2 r)
    {
        var t = new Table(lua);
        t["_type"] = DynValue.NewString("Rect2");
        t["x"] = DynValue.NewNumber(r.Position.X);
        t["y"] = DynValue.NewNumber(r.Position.Y);
        t["w"] = DynValue.NewNumber(r.Size.X);
        t["h"] = DynValue.NewNumber(r.Size.Y);
        return DynValue.NewTable(t);
    }
}
