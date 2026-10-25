using System.Text;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Maps;
using LibreKO.Editors.Core.Sources;

public class ClientMapTests
{
    [Fact] public async Task MissingStagedMapDependenciesCannotBeAppliedAndDeletionCannotOrphanMaps()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var key=f.Key(SourceRoot.ClientProject,"assets/terrain/map/placements.json");f.Write(key.Root,key.RelativePath,"[]"u8.ToArray());var row=ClientMapCatalog.Default("placements.json");row["name"]="missing_tree";var bytes=Encoding.UTF8.GetBytes(new JsonArray(row).ToJsonString());var registry=new SourceValidationRegistry(store.Blobs,f.Profile);ClientMapCatalog.Register(registry,store.Blobs,store);NpcVisualCatalog.Register(registry,store);
        var bad=await new FileApply(f.Profile,f.WorkspaceRoot,registry).ApplyAsync(new(Guid.NewGuid(),[store.Stage(key,bytes)]));Assert.Equal(FileApplyState.Invalid,bad.State);
        var model=f.Key(key.Root,"assets/objects/missing_tree.gltf");var glbKey=f.Key(key.Root,"assets/objects/missing_tree.glb");byte[] json=Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"},\"nodes\":[]}");using var packed=new MemoryStream();using(var w=new BinaryWriter(packed,Encoding.UTF8,true)){int length=(json.Length+3)&~3;w.Write(0x46546c67);w.Write(2);w.Write(20+length);w.Write(length);w.Write(0x4e4f534a);w.Write(json);for(int i=json.Length;i<length;i++)w.Write((byte)32);}
        var good=await new FileApply(f.Profile,f.WorkspaceRoot,registry).ApplyAsync(new(Guid.NewGuid(),[store.Stage(glbKey,packed.ToArray()),store.Stage(key,bytes)]));Assert.Equal(FileApplyState.Applied,good.State);
        var deleted=await new FileApply(f.Profile,f.WorkspaceRoot,registry).ApplyAsync(new(Guid.NewGuid(),[store.Stage(glbKey,null)]));Assert.Equal(FileApplyState.Invalid,deleted.State);Assert.True(store.Read(glbKey).Exists);
    }
    [Fact]
    public void SceneryEditPreservesMetadataAndDoesNotInventCollision()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);
        var key=f.Key(SourceRoot.ClientProject,"assets/terrain/custom/placements.json");
        var bytes=Encoding.UTF8.GetBytes("[{\"name\":\"gate\",\"p\":[1,2,3],\"r\":[0,0,0,1],\"s\":[1,1,1],\"e\":123,\"et\":4,\"b\":2,\"n\":77,\"custom\":9007199254740993}]");
        f.Write(key.Root,key.RelativePath,bytes);var before=store.Read(key);var doc=ClientMapDocument.Open(before,bytes);Assert.Equal(bytes,doc.Render());
        var token=doc.Json.RowAt(0).Token;doc.Transform(token,new(8,9,10),[0,0,.6f,.8f],new(2,3,4));
        var mutation=doc.Build(store);var sceneryOnly=new FileChange(Guid.NewGuid(),[mutation]);Assert.DoesNotContain(sceneryOnly.Files,m=>m.Before.Key.RelativePath.EndsWith("collision.bin"));
        string rendered=Encoding.UTF8.GetString(doc.Render());Assert.Contains("\"custom\":9007199254740993",rendered);var row=doc.Json.RowAt(0).Values;Assert.Equal(77,(int)row["n"]!);Assert.Equal(123,(int)row["e"]!);
        Assert.Empty(ClientMapCatalog.Validate(key,doc.Render()));Assert.Equal(before.Sha256,store.Read(key).Sha256);
        doc.Transform(token,new(8,9,10),[0,0,0,0],new(1,1,1));Assert.NotEmpty(ClientMapCatalog.Validate(key,doc.Render()));
        var fx=key with{RelativePath="assets/terrain/custom/fx_placements.json"};Assert.Empty(ClientMapCatalog.Validate(fx,"[{\"fx\":\"spark\",\"p\":[0,0,0],\"q\":[0,0,0,1],\"s\":2}]"u8.ToArray()));
        Assert.NotEmpty(ClientMapCatalog.Validate(fx,"[{\"fx\":\"spark\",\"p\":[0,0,0],\"r\":[0,0,0,1]}]"u8.ToArray()));
        var water=key with{RelativePath="assets/terrain/custom/water.json"};var valid="[{\"tex\":\"river\",\"cols\":2,\"rows\":2,\"level_y\":5,\"verts\":[[0,5,0],[1,5,0],[0,5,1],[1,5,1]],\"uv\":[[0,0],[1,0],[0,1],[1,1]]}]"u8.ToArray();
        Assert.Empty(ClientMapCatalog.Validate(water,valid));Assert.NotEmpty(ClientMapCatalog.Validate(water,Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(valid).Replace("\"cols\":2","\"cols\":3"))));
        var areas=key with{RelativePath="assets/terrain/custom/areas.json"};Assert.NotEmpty(ClientMapCatalog.Validate(areas,"[{\"id\":1,\"minX\":10,\"maxX\":1,\"minZ\":0,\"maxZ\":5}]"u8.ToArray()));
    }
    [Fact]
    public void CollisionIsExplicitFiniteBoundedTriangleSoupInMapSpace()
    {
        MapPoint[] inputVertices=[new(1,2,3),new(8,2,3),new(1,2,9),new(10,0,1),new(12,0,1),new(10,0,3)];
        var bytes=CollisionCodec.Write(inputVertices);Assert.Equal(inputVertices,CollisionCodec.Read(bytes));
        var changed=CollisionEdits.Transform(inputVertices,[1],new(4,5,6));Assert.Equal(inputVertices[..3],changed.Take(3));Assert.Equal(new(14,5,7),changed[3]);Assert.Equal(new(10,0,1),inputVertices[3]);
        Assert.Empty(CollisionCodec.Read(CollisionCodec.Write([])));Assert.Throws<InvalidDataException>(()=>CollisionCodec.Read(bytes[..^1]));Assert.Throws<InvalidDataException>(()=>CollisionCodec.Read([..bytes,0]));
        Assert.Throws<InvalidDataException>(()=>CollisionCodec.Read([0,..bytes[1..]]));Assert.Throws<InvalidDataException>(()=>CollisionCodec.Write([new(float.NaN,0,0),new(0,0,0),new(0,0,0)]));
        Assert.Throws<InvalidDataException>(()=>CollisionEdits.Transform(inputVertices,[2],new(1,0,0)));
        var rotated=CollisionEdits.Transform(inputVertices,[0],new(0,0,0),[0,1,0,0],new(2,1,1),new(0,0,0));Assert.Equal(new(-2,2,-3),rotated[0]);
    }
    [Fact]
    public void SmdRowRawIdentitySurvivesEqualRowReorderAndEditedCopy()
    {
        var doc=SmdCodec.Read(TerrainAuthoringTests.AlignedSmd());doc.Warps.Add(new(1,"same","",0,1,new(1,2,3),2,0));doc.Warps.Add(doc.Warps[0] with{});var bytes=SmdCodec.Write(doc);doc=SmdCodec.Read(bytes);var section=doc.Sections["Warps"];
        bytes[section.Offset+4+290]=17;bytes[section.Offset+4+320+290]=29;doc=SmdCodec.Read(bytes);
        var first=doc.Warps[0];doc.Warps.Reverse();var moved=SmdCodec.Write(doc);Assert.Equal(29,moved[section.Offset+4+290]);Assert.Equal(17,moved[section.Offset+4+320+290]);
        doc=SmdCodec.Read(bytes);doc.Warps[1]=doc.Warps[1] with{Fee=99};doc.Warps.Reverse();moved=SmdCodec.Write(doc);Assert.Equal(29,moved[section.Offset+4+290]);Assert.Equal(17,moved[section.Offset+4+320+290]);Assert.Equal((uint)99,SmdCodec.Read(moved).Warps[0].Fee);
    }
}
