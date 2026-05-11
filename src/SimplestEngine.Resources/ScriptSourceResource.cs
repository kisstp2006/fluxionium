using SimplestEngine.Abi;

namespace SimplestEngine.Resources;

/// <summary>
/// A script resource whose runtime language is not (yet) executable in this engine.
/// Used for .gd (GDScript) and .cs files imported from Godot projects when the matching
/// language plugin is not registered. The source text is preserved so the editor can
/// display it, and so a future language plugin can take it over without re-importing.
/// </summary>
public sealed class ScriptSourceResource : Resource
{
    /// <summary>"gdscript", "csharp", "lua", ...</summary>
    public string Language { get; }
    public string SourceCode { get; }
    public string SourceFile { get; }

    public ScriptSourceResource(string sourceCode, string language, string sourceFile)
    {
        SourceCode = sourceCode;
        Language = language;
        SourceFile = sourceFile;
        ResourcePath = sourceFile;
        ResourceClass = StringName.Get(language switch
        {
            "gdscript" => "GDScript",
            "csharp" => "CSharpScript",
            _ => "Script",
        });
    }
}
