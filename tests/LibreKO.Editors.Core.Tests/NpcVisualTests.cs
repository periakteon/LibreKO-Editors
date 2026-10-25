using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Sources;
public class NpcVisualTests
{
    [Fact]
    public async Task ReviewedModelDependencyMustKeepItsExactBytesUntilReplacement()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var index=f.Key(SourceRoot.ClientProject,"assets/npcs/index.json");f.Write(index.Root,index.RelativePath,"{\"100\":\"\"}"u8.ToArray());var model=f.Key(index.Root,"assets/npcs/safe.glb");var path=f.Write(model.Root,model.RelativePath,Glb("{\"asset\":{\"version\":\"2.0\"},\"nodes\":[]}"));
        var before=store.Read(index);var registry=new SourceValidationRegistry(store.Blobs,f.Profile);NpcVisualCatalog.Register(registry,store);var change=new FileChange(Guid.NewGuid(),[store.Stage(index,"{\"100\":\"safe\"}"u8.ToArray())]);
        var result=await new FileApply(f.Profile,f.WorkspaceRoot,registry,(step,_)=>{if(step=="before-replace")File.WriteAllBytes(path,Glb("{\"asset\":{\"version\":\"2.0\"},\"nodes\":[],\"extras\":1}"));}).ApplyAsync(change);
        Assert.NotEqual(FileApplyState.Applied,result.State);Assert.Equal(before.Sha256,store.Read(index).Sha256);Assert.Contains(result.Issues,x=>x.Message.Contains("dependency",StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void StockOptionalRoleGroupsAndExplicitDisabledVisualsRemainEditable()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var role=f.Key(SourceRoot.ClientProject,"assets/npcs/role_fx.json");var bytes="{\"npcs\":{\"501\":\"\"},\"future\":9007199254740993}"u8.ToArray();Assert.Empty(NpcVisualCatalog.Validate(role,bytes));Assert.Empty(AssetDependencyAudit.Validate(f.Profile,role,store.Blobs.Put(bytes),store));
        var index=f.Key(role.Root,"assets/npcs/index.json");Assert.Empty(NpcVisualCatalog.Validate(index,"{\"100\":\"\"}"u8.ToArray()));Assert.Empty(AssetDependencyAudit.Validate(f.Profile,index,store.Blobs.Put("{\"100\":\"\"}"u8.ToArray()),store));
    }
    [Fact]
    public void SharedNpcLooksRemainExplicitAndImportCannotEscape()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var key=f.Key(SourceRoot.ClientProject,"assets/npcs/index.json");var bytes="{\"100\":\"shared\",\"200\":\"shared\",\"custom\":{\"n\":9007199254740993}}"u8.ToArray();f.Write(key.Root,key.RelativePath,bytes);
        var doc=NpcVisualDocument.Open(store.Read(key),bytes);WorldRowSnapshot[] prototypes=[Prototype(7,false,100),Prototype(7,true,200),Prototype(8,true,100)];Assert.Equal(3,doc.SharedUsers("shared",prototypes).Count);doc.SetMapping(100,"cloned");Assert.Single(doc.SharedUsers("shared",prototypes));Assert.Contains("9007199254740993",Encoding.UTF8.GetString(doc.Render()));
        var model=f.Key(SourceRoot.ClientProject,"assets/npcs/shared.glb");f.Write(model.Root,model.RelativePath,Glb("{\"asset\":{\"version\":\"2.0\"},\"nodes\":[{\"name\":\"root\"}],\"skins\":[{\"joints\":[0]}],\"animations\":[{\"name\":\"idle\",\"channels\":[],\"samplers\":[]}] }"));
        var clone=doc.Clone("shared","cloned",store);Assert.Contains(clone.Files,m=>m.Before.Key.RelativePath=="assets/npcs/cloned.glb");Assert.False(store.Read(f.Key(model.Root,"assets/npcs/cloned.glb")).Exists);
        var unsafeAsset=f.Key(SourceRoot.ClientProject,"assets/npcs/unsafe.gltf");var candidate=store.Blobs.Put("{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"../outside.bin\",\"byteLength\":4}]}"u8.ToArray());Assert.NotEmpty(AssetDependencyAudit.Validate(f.Profile,unsafeAsset,candidate,store));
        var before=store.Read(model);var import=f.Write(SourceRoot.KoInput,"bad.gltf",store.Blobs.Read(candidate));Assert.Throws<InvalidDataException>(()=>AssetImportPlan.Prepare(f.Profile,import,unsafeAsset,store));Assert.Equal(before.Sha256,store.Read(before.Key).Sha256);
        foreach(var uri in new[]{"https://example.test/model.bin","file:///c:/secret","C:/secret","a%2f..%2foutside.bin"}){var root=new JsonObject{["asset"]=new JsonObject{["version"]="2.0"},["buffers"]=new JsonArray(new JsonObject{["uri"]=uri,["byteLength"]=4})};Assert.NotEmpty(AssetDependencyAudit.Validate(f.Profile,unsafeAsset,store.Blobs.Put(Encoding.UTF8.GetBytes(root.ToJsonString())),store));}
    }
    [Fact]
    public void VisualMetadataRetainsEventsAndValidatesBonesClipsAndAttachments()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var model=f.Key(SourceRoot.ClientProject,"assets/npcs/custom.glb");f.Write(model.Root,model.RelativePath,Glb("{\"asset\":{\"version\":\"2.0\"},\"nodes\":[{\"name\":\"root\"}],\"skins\":[{\"joints\":[0]}],\"animations\":[{\"name\":\"idle\",\"channels\":[],\"samplers\":[]}]}"));
        var anim=f.Key(model.Root,"assets/npcs/custom.anim.json");var bytes="{\"animations\":[{\"index\":0,\"name\":\"basic\",\"exported\":\"idle\",\"start\":10,\"end\":30,\"fps\":30,\"sound0\":12.50,\"strike0\":22,\"future\":9007199254740993}]}"u8.ToArray();f.Write(anim.Root,anim.RelativePath,bytes);
        var doc=NpcVisualDocument.Open(store.Read(anim),bytes);doc.SetNested("animations",0,"fps",JsonValue.Create(42));var saved=doc.Build(store);Assert.Empty(NpcVisualCatalog.Validate(anim,store.Blobs.Read(saved.After!)));Assert.Empty(AssetDependencyAudit.Validate(f.Profile,anim,saved.After!,store));Assert.Contains("\"sound0\":12.50",Encoding.UTF8.GetString(doc.Render()));Assert.Contains("9007199254740993",Encoding.UTF8.GetString(doc.Render()));
        doc.SetNested("animations",0,"exported",JsonValue.Create("missing"));Assert.NotEmpty(AssetDependencyAudit.Validate(f.Profile,anim,doc.Build(store).After!,store));
        var fx=f.Key(model.Root,"assets/npcs/custom.fxplug.json");var bad=store.Blobs.Put("{\"parts\":[{\"fx\":\"spark\",\"bone\":2,\"p\":[0,1,0],\"dir\":[0,0,1],\"scale\":1}]}"u8.ToArray());Assert.NotEmpty(AssetDependencyAudit.Validate(f.Profile,fx,bad,store));
        Assert.NotEmpty(NpcVisualCatalog.Validate(f.Key(model.Root,"assets/npcs/custom.plugs.json"),"{\"plugs\":[{\"slot\":0,\"joint\":0,\"model\":\"blade\",\"pos\":[0,0,0],\"quat\":[0,0,0,0],\"scale\":[1,1,1]}]}"u8.ToArray()));
    }
    [Fact]
    public void CompatibleImportCapturesDependenciesBeforeAnyDestinationWrite()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var json="{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"mesh.bin\",\"byteLength\":4}],\"images\":[{\"uri\":\"tex/color.png\"}]}"u8.ToArray();var input=f.Write(SourceRoot.KoInput,"import/model.gltf",json);f.Write(SourceRoot.KoInput,"import/mesh.bin",[1,2,3,4]);f.Write(SourceRoot.KoInput,"import/tex/color.png",RgbaPngCodec.Encode(new(1,1,[1,2,3,255])));
        var dest=f.Key(SourceRoot.ClientProject,"assets/npcs/imported.gltf");var change=AssetImportPlan.Prepare(f.Profile,input,dest,store);Assert.Equal(3,change.Files.Count);Assert.All(change.Files,m=>Assert.False(store.Read(m.Before.Key).Exists));
        var corrupted=Glb("{\"asset\":{\"version\":\"2.0\"}}");corrupted[8]=1;Assert.NotEmpty(AssetDependencyAudit.Validate(f.Profile,dest with{RelativePath="assets/npcs/invalid.glb"},store.Blobs.Put(corrupted),store));
        var duplicate=Glb("{\"asset\":{\"version\":\"2.0\"},\"animations\":[{\"name\":\"same\"},{\"name\":\"same\"}]}");Assert.NotEmpty(AssetDependencyAudit.Validate(f.Profile,dest with{RelativePath="assets/npcs/duplicate.glb"},store.Blobs.Put(duplicate),store));
    }
    static WorldRowSnapshot Prototype(int id,bool monster,int model)=>new(new(WorldTable.Npcs,id,IsMonster:monster),new JsonObject{["Id"]=id,["IsMonster"]=monster,["Name"]=monster?"Monster":"Npc",["ModelId"]=model},"fixture");
    [Fact]
    public async Task ReplacingOrDeletingAResourceRevalidatesExistingConsumers()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var json="{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"mesh.bin\",\"byteLength\":4}]}"u8.ToArray();f.Write(SourceRoot.ClientProject,"assets/npcs/body.gltf",json);f.Write(SourceRoot.ClientProject,"assets/npcs/mesh.bin",[1,2,3,4]);var key=f.Key(SourceRoot.ClientProject,"assets/npcs/mesh.bin");var registry=new SourceValidationRegistry(store.Blobs,f.Profile);NpcVisualCatalog.Register(registry,store);
        var deleted=new FileChange(Guid.NewGuid(),[new(store.Read(key),null)]);Assert.NotEmpty(await registry.ValidateBatchAsync(deleted));
        var altered=new FileChange(Guid.NewGuid(),[new(store.Read(key),store.Blobs.Put([1]))]);Assert.NotEmpty(await registry.ValidateBatchAsync(altered));
        var valid=new FileChange(Guid.NewGuid(),[new(store.Read(key),store.Blobs.Put([5,6,7,8]))]);Assert.Empty(await registry.ValidateBatchAsync(valid));
    }
    [Fact]
    public void NonfiniteBinaryAccessorsAreRefusedBeforeNativeImport()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var key=f.Key(SourceRoot.ClientProject,"assets/npcs/nonfinite.gltf");var bad=Convert.ToBase64String(BitConverter.GetBytes(float.NaN));var json=Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"data:application/octet-stream;base64,"+bad+"\",\"byteLength\":4}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":4}],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":1,\"type\":\"SCALAR\"}]}");Assert.NotEmpty(AssetDependencyAudit.Validate(f.Profile,key,store.Blobs.Put(json),store));
    }
    [Fact]
    public void ImportIncludesCompatibleLocalPlugModelDependencies()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var input=f.Write(SourceRoot.KoInput,"with-plug/body.glb",Glb("{\"asset\":{\"version\":\"2.0\"},\"nodes\":[{\"name\":\"root\"}],\"skins\":[{\"joints\":[0]}]}"));f.Write(SourceRoot.KoInput,"with-plug/body.plugs.json","{\"plugs\":[{\"slot\":0,\"joint\":0,\"model\":\"blade\",\"pos\":[0,0,0],\"quat\":[0,0,0,1],\"scale\":[1,1,1],\"anim\":\"\"}]}"u8.ToArray());f.Write(SourceRoot.KoInput,"with-plug/plugs/blade.glb",Glb("{\"asset\":{\"version\":\"2.0\"}}"));var change=AssetImportPlan.Prepare(f.Profile,input,f.Key(SourceRoot.ClientProject,"assets/npcs/new-body.glb"),store);Assert.Equal(3,change.Files.Count);Assert.Contains(change.Files,m=>m.Before.Key.RelativePath=="assets/npcs/plugs/blade.glb");
    }
    [Fact]
    public void CloneUsesReviewedDraftBytesAndImportRefusesLinkedDependencies()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var index=f.Key(SourceRoot.ClientProject,"assets/npcs/index.json");f.Write(index.Root,index.RelativePath,"{\"100\":\"body\"}"u8.ToArray());var body=f.Key(index.Root,"assets/npcs/body.glb");f.Write(body.Root,body.RelativePath,Glb("{\"asset\":{\"version\":\"2.0\"}}"));var replacement=store.Blobs.Put(Glb("{\"asset\":{\"version\":\"2.0\",\"generator\":\"draft\"}}"));var doc=NpcVisualDocument.Open(store.Read(index),store.Blobs.Read(store.Read(index).Content!));var clone=doc.Clone("body","copy",store,new Dictionary<SourceFileKey,BlobRef?>{{body,replacement}});Assert.Equal(replacement,Assert.Single(clone.Files).After);
        var outside=Path.Combine(f.Root,"outside-input");Directory.CreateDirectory(outside);File.WriteAllBytes(Path.Combine(outside,"mesh.bin"),[1,2,3,4]);var input=f.Write(SourceRoot.KoInput,"linked-import/body.gltf","{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"link/mesh.bin\",\"byteLength\":4}]}"u8.ToArray());var link=Path.Combine(Path.GetDirectoryName(input)!,"link");
        if(OperatingSystem.IsWindows()){var start=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"/c","mklink","/J",link,outside})start.ArgumentList.Add(arg);using var process=System.Diagnostics.Process.Start(start)!;process.WaitForExit();Assert.Equal(0,process.ExitCode);}else Directory.CreateSymbolicLink(link,outside);
        Assert.Throws<InvalidDataException>(()=>AssetImportPlan.Prepare(f.Profile,input,f.Key(SourceRoot.ClientProject,"assets/npcs/linked.gltf"),store));Assert.False(store.Read(f.Key(index.Root,"assets/npcs/linked.gltf")).Exists);
    }
    public static byte[] Glb(string json)
    {var raw=Encoding.UTF8.GetBytes(json);var padded=new byte[(raw.Length+3)/4*4];Array.Fill(padded,(byte)' ');raw.CopyTo(padded,0);var bytes=new byte[20+padded.Length];"glTF"u8.CopyTo(bytes);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),2);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8),bytes.Length);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12),padded.Length);"JSON"u8.CopyTo(bytes.AsSpan(16));padded.CopyTo(bytes,20);return bytes;}
}
