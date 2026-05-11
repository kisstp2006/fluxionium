using MoonSharp.Interpreter;
using SimplestEngine.Abi;

namespace SimplestEngine.Scripting.Lua;

public sealed class LuaScriptLanguage : IScriptLanguage
{
    public string Name => "Lua";
    public string[] FileExtensions => new[] { ".lua" };

    public IEngineAPI? Engine { get; set; }

    static LuaScriptLanguage()
    {
        // MoonSharp UserData registration could go here for any C# types we want directly used.
        UserData.RegisterAssembly(typeof(LuaScriptLanguage).Assembly);
    }

    public IScript? Load(string path)
    {
        var source = System.IO.File.ReadAllText(path);
        return new LuaScript(this, path, source);
    }

    public void Reload(IScript script)
    {
        if (script is LuaScript ls)
            ls.Reload(System.IO.File.ReadAllText(ls.SourcePath));
    }
}

public sealed class LuaScript : IScript
{
    public LuaScriptLanguage Lang { get; }
    public string SourcePath { get; }
    public IScriptLanguage Language => Lang;
    public string Source { get; private set; }

    private readonly HashSet<StringName> _methodCache = new();

    public LuaScript(LuaScriptLanguage lang, string path, string source)
    {
        Lang = lang; SourcePath = path; Source = source;
    }

    public void Reload(string source) { Source = source; _methodCache.Clear(); }

    public IScriptInstance Instantiate(IGodotObjectAPI owner)
        => new LuaScriptInstance(this, owner);

    public bool HasMethod(StringName method) => _methodCache.Contains(method);
    internal void RecordMethod(StringName n) => _methodCache.Add(n);
}
