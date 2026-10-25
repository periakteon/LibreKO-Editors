using System.Diagnostics;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;

public class SourcePathTests
{
    [Theory]
    [InlineData("../escape.smd")]
    [InlineData("..\\escape.smd")]
    [InlineData("C:/escape.smd")]
    [InlineData("/escape.smd")]
    [InlineData("a.smd:stream")]
    [InlineData("sub/../a.smd")]
    [InlineData("sub/a. ")]
    [InlineData("CON.smd")]
    public void RejectsUnsafeRelativeNames(string relative)
    {
        using var f = SourceFixture.Create();
        Assert.Throws<InvalidOperationException>(() => SourcePaths.Resolve(f.Profile, f.Key(SourceRoot.ServerMaps, relative), true));
    }

    [Fact]
    public void ChangedRootCannotReuseTheOldKey()
    {
        using var f = SourceFixture.Create();
        var key = f.Key(SourceRoot.ServerMaps, "map.smd");
        var root = f.Profile.Roots[SourceRoot.ServerMaps];
        Directory.Move(root, root + "-old");
        Directory.CreateDirectory(root);
        Assert.Throws<InvalidOperationException>(() => SourcePaths.Resolve(f.Profile, key, true));
    }

    [Fact]
    public void LinkedParentAndLinkedRootAreRefused()
    {
        using var f = SourceFixture.Create();
        var outside = Path.Combine(f.Root, "outside");
        Directory.CreateDirectory(outside);
        var link = Path.Combine(f.Profile.Roots[SourceRoot.ServerMaps], "linked");
        Junction(link, outside);
        Assert.Throws<InvalidOperationException>(() => SourcePaths.Resolve(f.Profile, f.Key(SourceRoot.ServerMaps, "linked/map.smd"), true));
        var key = f.Key(SourceRoot.ServerMaps, "map.smd");
        var root = f.Profile.Roots[SourceRoot.ServerMaps];
        Directory.Move(root, root + "-old");
        Junction(root, outside);
        Assert.Throws<InvalidOperationException>(() => SourcePaths.Resolve(f.Profile, key, true));
    }

    [Fact]
    public void LinkedDocumentPathIsRefusedBeforeOpening()
    {
        using var f = SourceFixture.Create();
        var link = Path.Combine(f.Profile.Roots[SourceRoot.ServerMaps], "linked.smd");
        if (OperatingSystem.IsWindows()) Junction(link, f.Profile.Roots[SourceRoot.ServerData]);
        else File.CreateSymbolicLink(link, f.Write(SourceRoot.ServerData, "outside.json", [1, 2, 3]));
        Assert.Throws<InvalidOperationException>(() => new SourceStore(f.Profile, f.WorkspaceRoot).Read(f.Key(SourceRoot.ServerMaps, "linked.smd")));
    }

    [Fact]
    public void AnUnavailableOptionalRootDoesNotDisableOtherSourceSections()
    {
        using var f = SourceFixture.Create();
        var client = f.Profile.Roots[SourceRoot.ClientProject];
        var clientKey = f.Key(SourceRoot.ClientProject, "assets/npcs/index.json");
        Directory.Move(client, client + "-offline");
        f.Write(SourceRoot.ServerData, "available.json", [1, 2]);
        var store = new SourceStore(f.Profile, f.WorkspaceRoot);
        Assert.True(store.Read(f.Key(SourceRoot.ServerData, "available.json")).Exists);
        Assert.Throws<InvalidOperationException>(() => store.Read(clientKey));
    }

    [Fact]
    public void ForeignScopeAndOverlappingBuildOrHistoryAreRefused()
    {
        using var f = SourceFixture.Create();
        var key = f.Key(SourceRoot.ServerData, "a.json") with { ProfileId = "other" };
        Assert.Throws<InvalidOperationException>(() => SourcePaths.Resolve(f.Profile, key, false));
        var roots = new Dictionary<SourceRoot, string>(f.Profile.Roots) { [SourceRoot.BuildOutput] = Path.Combine(f.Profile.Roots[SourceRoot.ClientProject], "output") };
        Assert.Throws<InvalidOperationException>(() => new SourceStore(f.Profile with { Roots = roots }, f.WorkspaceRoot));
        Assert.Throws<InvalidOperationException>(() => new SourceStore(f.Profile, Path.Combine(f.Profile.Roots[SourceRoot.ServerData], "history")));
        Assert.Throws<InvalidOperationException>(() => SourcePaths.Resolve(f.Profile, f.Key(SourceRoot.Pipeline, "bake.py"), true));
        Assert.Throws<InvalidOperationException>(() => SourcePaths.Resolve(f.Profile, f.Key(SourceRoot.BuildOutput, "out.pck"), true));
    }

    static void Junction(string link, string target)
    {
        if (!OperatingSystem.IsWindows()) { Directory.CreateSymbolicLink(link, target); return; }
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink"); start.ArgumentList.Add("/J"); start.ArgumentList.Add(link); start.ArgumentList.Add(target);
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
    }
}
