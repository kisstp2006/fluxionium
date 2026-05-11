using MoonSharp.Interpreter;
using SimplestEngine.Abi;

namespace SimplestEngine.Scripting.Lua;

/// <summary>
/// Per-object Lua script instance. Holds a MoonSharp Script with the source
/// loaded and globals bound to this object via metatable.
/// </summary>
public sealed class LuaScriptInstance : IScriptInstance
{
    private readonly LuaScript _script;
    private readonly IGodotObjectAPI _owner;
    private readonly Script _lua;

    public IScript Script => _script;
    public IGodotObjectAPI Owner => _owner;
    public Script Vm => _lua;

    public LuaScriptInstance(LuaScript script, IGodotObjectAPI owner)
    {
        _script = script;
        _owner = owner;

        _lua = new Script(CoreModules.Preset_HardSandbox
                          | CoreModules.Math | CoreModules.String | CoreModules.Table
                          | CoreModules.Bit32 | CoreModules.Coroutine);

        LuaGodotBridge.SetupGlobals(_lua, owner, script.Lang);

        try
        {
            _lua.DoString(script.Source, codeFriendlyName: script.SourcePath);
        }
        catch (InterpreterException ex)
        {
            Console.Error.WriteLine($"[Lua] error loading {script.SourcePath}: {ex.DecoratedMessage}");
        }
    }

    public Variant Call(StringName method, ReadOnlySpan<Variant> args)
    {
        var name = method.ToString();
        var fn = _lua.Globals.Get(name);
        if (fn.Type != DataType.Function) return Variant.Nil;
        var converted = new DynValue[args.Length];
        for (int i = 0; i < args.Length; i++) converted[i] = LuaGodotBridge.ToLua(_lua, args[i]);
        try
        {
            var result = _lua.Call(fn, converted);
            return LuaGodotBridge.FromLua(result);
        }
        catch (InterpreterException ex)
        {
            Console.Error.WriteLine($"[Lua] error calling {name}() in {_script.SourcePath}: {ex.DecoratedMessage}");
            return Variant.Nil;
        }
    }

    public bool HasMethod(StringName method) =>
        _lua.Globals.Get(method.ToString()).Type == DataType.Function;

    public Variant GetProperty(StringName name)
    {
        var v = _lua.Globals.Get(name.ToString());
        return LuaGodotBridge.FromLua(v);
    }

    public bool SetProperty(StringName name, Variant value)
    {
        _lua.Globals.Set(name.ToString(), LuaGodotBridge.ToLua(_lua, value));
        return true;
    }

    public void Notification(int what)
    {
        var n = _lua.Globals.Get("_notification");
        if (n.Type == DataType.Function)
        {
            try { _lua.Call(n, DynValue.NewNumber(what)); }
            catch (InterpreterException ex)
            { Console.Error.WriteLine($"[Lua] _notification error: {ex.DecoratedMessage}"); }
        }
    }
}
