namespace SimplestEngine.Abi;

/// <summary>A loaded script source (lua, c#, native, wasm). One per file.</summary>
public interface IScript
{
    string SourcePath { get; }
    IScriptLanguage Language { get; }
    IScriptInstance Instantiate(IGodotObjectAPI owner);
    bool HasMethod(StringName method);
}

/// <summary>Per-object script state.</summary>
public interface IScriptInstance
{
    IScript Script { get; }
    IGodotObjectAPI Owner { get; }
    Variant Call(StringName method, ReadOnlySpan<Variant> args);
    bool HasMethod(StringName method);
    Variant GetProperty(StringName name);
    bool SetProperty(StringName name, Variant value);
    void Notification(int what);
}

/// <summary>Script language plugin. Registered in <see cref="IScriptServer"/>.</summary>
public interface IScriptLanguage
{
    string Name { get; }
    string[] FileExtensions { get; }
    IScript? Load(string path);
    void Reload(IScript script);   // for hot reload
}

public interface IScriptServer
{
    void Register(IScriptLanguage lang);
    IScriptLanguage? FindForExtension(string extension);
    IScript? Load(string path);
}
