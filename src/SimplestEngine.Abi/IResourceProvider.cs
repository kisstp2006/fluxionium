namespace SimplestEngine.Abi;

public interface IResource
{
    string ResourcePath { get; }
    StringName ResourceClass { get; }
}

public interface IResourceProvider
{
    /// <summary>Load by path (`res://...`) or UID (`uid://...`).</summary>
    IResource? Load(string pathOrUid);

    void Save(IResource resource, string path);

    bool Exists(string pathOrUid);

    string? UidToPath(string uid);
    string? PathToUid(string path);
}
