using SimplestEngine.Abi;

namespace SimplestEngine.Scripting;

/// <summary>Global registry of script languages. Lua / C# / Native / WASM plug in here.</summary>
public sealed class ScriptServer : IScriptServer
{
    private readonly Dictionary<string, IScriptLanguage> _byExtension =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly List<IScriptLanguage> _languages = new();

    public IReadOnlyList<IScriptLanguage> Languages => _languages;

    public void Register(IScriptLanguage lang)
    {
        _languages.Add(lang);
        foreach (var ext in lang.FileExtensions)
            _byExtension[ext] = lang;
    }

    public IScriptLanguage? FindForExtension(string extension)
    {
        if (!extension.StartsWith('.')) extension = "." + extension;
        return _byExtension.GetValueOrDefault(extension);
    }

    public IScript? Load(string path)
    {
        var ext = System.IO.Path.GetExtension(path);
        return FindForExtension(ext)?.Load(path);
    }
}
