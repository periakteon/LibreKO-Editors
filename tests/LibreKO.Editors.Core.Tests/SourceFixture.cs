using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;

public sealed class SourceFixture : IDisposable
{
    public string Root { get; }
    public string WorkspaceRoot { get; }
    public SourceProfile Profile { get; }
    public static SourceFixture Create() => new();

    SourceFixture()
    {
        Root = Path.Combine(Path.GetTempPath(), "libreko-source-fixture-" + Guid.NewGuid().ToString("N"));
        WorkspaceRoot = Path.Combine(Root, "editor-workspace");
        Directory.CreateDirectory(WorkspaceRoot);
        var roots = Enum.GetValues<SourceRoot>().ToDictionary(x => x, x => Path.Combine(Root, x.ToString()));
        foreach (var path in roots.Values) Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(Root, "fixture-marker"), Path.GetFileName(Root));
        Profile = new(Guid.NewGuid().ToString("N"), "Disposable source fixture", roots);
    }

    public SourceFileKey Key(SourceRoot root, string relative)
        => new(Profile.Id, root, SourceIdentity.Root(Profile.Roots[root]), relative);

    public string Write(SourceRoot root, string relative, byte[] bytes)
    {
        var path = Path.Combine(Profile.Roots[root], relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public SourceValidationRegistry Validators()
    {
        var registry = new SourceValidationRegistry(new(WorkspaceRoot), Profile);
        registry.Register("fixture-bytes/1", k => k.Root is SourceRoot.ServerData or SourceRoot.ServerMaps, _ => []);
        return registry;
    }

    // Fixture diagnostics are intentionally retained; this owns no persistent handles.
    public void Dispose() { }
}
