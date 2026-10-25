using System.Text;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Sources;

public class SourceProfileTests
{
    [Theory]
    [InlineData("assets/objects/tree.glb","assets/objects/tree.glb",false)]
    [InlineData("assets/npcs/plugs/hat.glb","assets/npcs/plugs/hat.glb",false)]
    [InlineData("assets/npcs/tree.glb","assets/npcs/tree.glb",true)]
    public void SourcePreviewResolvesTheActualSelectedModelPath(string relative,string expected,bool assembled)
    {
        var preview=new SourcePreviewManifest(Guid.NewGuid(),"p",1,[],"C:/snapshot"){Target=new("p",SourceRoot.ClientProject,"r",relative),Stem=Path.GetFileNameWithoutExtension(relative)};Assert.Equal(expected,preview.ModelResourcePath);Assert.Equal(assembled,preview.AssembleNpc);
    }
    [Fact]
    public void OldProfileLoadsWithoutAuthoringAndRejectsEscapes()
    {
        using var f = SourceFixture.Create();
        var path = Path.Combine(f.WorkspaceRoot, "old.json");
        File.WriteAllText(path, "{\"assetRoot\":\"assets\",\"workspaceRoot\":\"work\",\"connectionEnvironmentVariable\":\"CUSTOM_DB\",\"serverScheduleTimeZone\":\"Europe/Istanbul\"}", new UTF8Encoding(true));
        var old = ProfileStore.Load(path)!;
        Assert.Null(old.Sources);
        Assert.Null(old.ActiveSourceId);
        Assert.Equal("CUSTOM_DB", old.ConnectionEnvironmentVariable);
        Assert.Equal("Europe/Istanbul", old.ServerScheduleTimeZone);
        Assert.Throws<InvalidOperationException>(() => SourcePaths.Resolve(f.Profile, f.Key(SourceRoot.ServerMaps, "../other.smd"), true));
        Assert.Throws<InvalidOperationException>(() => SourcePaths.Resolve(f.Profile, f.Key(SourceRoot.KoInput, "Zones/a.gtd"), true));
        ProfileStore.Save(path, old with { Sources = [f.Profile], ActiveSourceId = f.Profile.Id });
        var loaded = ProfileStore.Load(path)!;
        Assert.Equal("assets", loaded.AssetRoot);
        Assert.Equal("work", loaded.WorkspaceRoot);
        Assert.Equal("CUSTOM_DB", loaded.ConnectionEnvironmentVariable);
        Assert.Equal("Europe/Istanbul", loaded.ServerScheduleTimeZone);
        Assert.Equal(f.Profile.Id, loaded.ActiveSourceId);
        Assert.Equal(f.Profile.Roots[SourceRoot.ServerMaps], loaded.Sources![0].Roots[SourceRoot.ServerMaps]);
    }

    [Fact]
    public void BlobSnapshotsKeepExactBytesAndDetectStorageCorruption()
    {
        using var f = SourceFixture.Create();
        byte[] bytes = [239, 187, 191, 123, 125, 13, 10];
        var path = f.Write(SourceRoot.ServerData, "test.json", bytes);
        var store = new SourceStore(f.Profile, f.WorkspaceRoot);
        var key = f.Key(SourceRoot.ServerData, "test.json");
        var snapshot = store.Read(key);
        Assert.True(snapshot.Exists);
        Assert.Equal(bytes, store.Blobs.Read(snapshot.Content!));
        var staged = store.Stage(key, [1, 2, 3]);
        Assert.Equal(bytes, File.ReadAllBytes(path));
        Assert.Equal(new byte[] { 1, 2, 3 }, store.Blobs.Read(staged.After!));
        Assert.Throws<InvalidDataException>(() => store.Blobs.Read(new("../escape", 2)));
        Assert.Throws<InvalidDataException>(() => store.Blobs.Read(new(snapshot.Sha256, 1)));
        Assert.Throws<InvalidDataException>(() => store.Blobs.Read(new(snapshot.Sha256, long.MaxValue)));
        var blobPath = Directory.GetFiles(f.WorkspaceRoot, snapshot.Sha256, SearchOption.AllDirectories).Single();
        File.WriteAllBytes(blobPath, [9]);
        Assert.Throws<InvalidDataException>(() => store.Blobs.Read(snapshot.Content!));
    }

    [Fact]
    public void ATargetRequiresExactlyOneExplicitCodec()
    {
        using var f = SourceFixture.Create();
        var blobs = new BlobStore(f.WorkspaceRoot);
        var registry = new SourceValidationRegistry(blobs, f.Profile);
        var ticket = new SourceValidationTicket(f.Key(SourceRoot.ServerData, "a.json"), blobs.Put("{}"u8.ToArray()));
        Assert.NotEmpty(registry.Validate(ticket));
        registry.Register("json/1", key => key.Root == SourceRoot.ServerData, _ => []);
        Assert.Empty(registry.Validate(ticket));
        registry.Register("other/1", key => key.RelativePath.EndsWith(".json"), _ => []);
        Assert.NotEmpty(registry.Validate(ticket));
        var codeTicket = ticket with { Key = f.Key(SourceRoot.ClientProject, "src/Boot.cs") };
        Assert.NotEmpty(registry.Validate(codeTicket));
    }

    [Fact]
    public void OutboundFramesAreLimitedByUtf8BytesBeforeSending()
    {
        Assert.Throws<InvalidDataException>(() => Protocol.ValidateOutbound(new string('ğ', Protocol.Limit / 2 + 1)));
        Protocol.ValidateOutbound("{\"method\":\"ping\"}");
    }
}
