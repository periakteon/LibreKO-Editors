using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public static partial class WorldChecks
{
    static async Task NpcVisuals(EditorShell shell)
    {
        _=SourceChecks.Marker(shell);var session=shell.Session;var store=session.Sources.Store!;
        SourceFileKey Key(string relative)=>store.Key(SourceRoot.ClientProject,relative);
        void Write(SourceFileKey key,byte[] bytes){var path=SourcePaths.Resolve(store.Profile,key,true);Directory.CreateDirectory(Path.GetDirectoryName(path)!);SourcePaths.Resolve(store.Profile,key,true);File.WriteAllBytes(path,bytes);}
        var stem="native_npc_"+Guid.NewGuid().ToString("N")[..10];var indexKey=Key("assets/npcs/index.json");Write(indexKey,Encoding.UTF8.GetBytes(new JsonObject{["100"]=stem,["200"]=stem,["future"]=9007199254740993L}.ToJsonString()));Write(Key("assets/npcs/"+stem+".glb"),NpcVisualFixture());
        Write(Key("assets/npcs/"+stem+".anim.json"),"{\"animations\":[{\"index\":0,\"name\":\"basic\",\"exported\":\"idle\",\"start\":0,\"end\":30,\"fps\":30,\"sound0\":12.50,\"strike0\":22,\"future\":9007199254740993}]}"u8.ToArray());
        Write(Key("assets/npcs/"+stem+".plugs.json"),"{\"plugs\":[]}"u8.ToArray());Write(Key("assets/npcs/"+stem+".fxplug.json"),"{\"parts\":[]}"u8.ToArray());
        var npcKey=store.Key(SourceRoot.ServerData,"Npcs.json");Write(npcKey,"[{\"Id\":7,\"IsMonster\":false,\"Name\":\"Visual NPC\",\"ModelId\":100},{\"Id\":7,\"IsMonster\":true,\"Name\":\"Visual monster\",\"ModelId\":200}]"u8.ToArray());
        await shell.RequestWorkspaceAsync("world");var app=(WorldWorkspace)shell.Active!;app.ShowSources();await app.SourceView!.OpenAsync(indexKey);await app.SourceView.PreviewReady;
        NpcVisualView View()=>app.SourceView.FindChildren("*","",true,false).OfType<NpcVisualView>().Single(v=>v.IsVisibleInTree());var view=View();await view.LoadPreview();
        if(view.Preview.Model==null||NpcPreviewBuilder.Find<Skeleton3D>(view.Preview.Model)?.GetBoneCount()!=1||view.Preview.Animation?.HasAnimation("idle")!=true)throw new Exception("Loose source NPC did not produce its exact skeleton/clip.");
        if(!view.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text.Contains("NPC 7 · Visual NPC")&&l.Text.Contains("Monster 7 · Visual monster")))throw new Exception("Shared users lost the typed NPC identity.");
        var original=store.Read(Key("assets/npcs/"+stem+".glb"));var doc=NpcVisualDocument.Open(store.Read(indexKey),store.Blobs.Read(store.Read(indexKey).Content!));var clone=doc.Clone(stem,stem+"_clone",store);foreach(var file in clone.Files)app.SourceView.OpenCandidate(file);view=View();await app.SourceView.PreviewReady;
        await app.SourceView.OpenAsync(indexKey);view=View();view.SetMapping(200,stem+"_clone");
        await app.SourceView.OpenAsync(Key("assets/npcs/"+stem+".anim.json"));await app.SourceView.PreviewReady;view=View();view.SetNested("fps",JsonValue.Create(42));
        var importedPath=Path.Combine(store.Profile.Roots[SourceRoot.KoInput],"native-import.glb");File.WriteAllBytes(importedPath,NpcVisualFixture());var incoming=AssetImportPlan.Prepare(store.Profile,importedPath,Key("assets/npcs/"+stem+"_imported.glb"),store);foreach(var file in incoming.Files)app.SourceView.OpenCandidate(file);await app.SourceView.PreviewReady;view=View();if(view.Preview.Model==null)throw new Exception("Reviewed import draft did not load as loose source.");
        var unsafePath=Path.Combine(store.Profile.Roots[SourceRoot.KoInput],"native-unsafe.gltf");File.WriteAllText(unsafePath,"{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"../escape.bin\",\"byteLength\":4}]}");try{AssetImportPlan.Prepare(store.Profile,unsafePath,Key("assets/npcs/unsafe.glb"),store);throw new Exception("Escaping import was accepted.");}catch(InvalidDataException){}
        if(store.Read(original.Key).Sha256!=original.Sha256||store.Read(Key("assets/npcs/"+stem+"_imported.glb")).Exists)throw new Exception("NPC source tools wrote a destination before Apply.");
        var change=session.Sources.CaptureDrafts()!;foreach(var file in change.Files.Where(f=>NpcVisualCatalog.Accepts(f.Before.Key)||AssetDependencyAudit.Accepts(f.Before.Key))){var issues=AssetDependencyAudit.Validate(store.Profile,file.Before.Key,file.After!,store,change.Files.ToDictionary(f=>f.Before.Key,f=>f.After));if(issues.Count>0)throw new Exception(string.Join("; ",issues.Select(i=>i.Message)));}
        foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,800)}){shell.GetWindow().Size=size;shell.GetViewport().WarpMouse(new(4,4));await EventGalleries.Capture(shell,"world-npc-visuals",size);}
        var saved=await session.Sources.ApplyAsync(change);if(saved.State!=FileApplyState.Applied||session.Sources.HasUnsavedChanges)throw new Exception("NPC visual source review did not Apply/rebase.");
        var anim=store.Read(Key("assets/npcs/"+stem+".anim.json"));if(!Encoding.UTF8.GetString(store.Blobs.Read(anim.Content!)).Contains("\"sound0\":12.50"))throw new Exception("Animation events were rewritten by a different field edit.");
    }
    internal static byte[] NpcVisualFixture()
    {
        using var binary=new MemoryStream();using(var w=new BinaryWriter(binary,Encoding.UTF8,true))
        {
            foreach(var v in new[]{-1f,0,0,1,0,0,0,2,0})w.Write(v);for(int i=0;i<3;i++){w.Write(0f);w.Write(0f);w.Write(1f);}for(int i=0;i<12;i++)w.Write((ushort)0);for(int i=0;i<3;i++){w.Write(1f);w.Write(0f);w.Write(0f);w.Write(0f);}foreach(var index in new ushort[]{0,1,2,0})w.Write(index);w.Write(0f);w.Write(1f);foreach(var v in new[]{0f,0,0,0,.2f,0})w.Write(v);for(int i=0;i<16;i++)w.Write(i%5==0?1f:0f);
        }
        var json=JsonNode.Parse("""
        {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"name":"Rig","children":[1,2]},{"name":"root"},{"name":"Triangle","mesh":0,"skin":0}],"skins":[{"joints":[1],"skeleton":1,"inverseBindMatrices":7}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"JOINTS_0":2,"WEIGHTS_0":3},"indices":4,"material":0}]}],"materials":[{"doubleSided":true,"pbrMetallicRoughness":{"baseColorFactor":[0.65,0.42,0.12,1],"metallicFactor":0,"roughnessFactor":0.8}}],"buffers":[{"byteLength":248}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36},{"buffer":0,"byteOffset":72,"byteLength":24},{"buffer":0,"byteOffset":96,"byteLength":48},{"buffer":0,"byteOffset":144,"byteLength":6},{"buffer":0,"byteOffset":152,"byteLength":8},{"buffer":0,"byteOffset":160,"byteLength":24},{"buffer":0,"byteOffset":184,"byteLength":64}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[-1,0,0],"max":[1,2,0]},{"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":2,"componentType":5123,"count":3,"type":"VEC4"},{"bufferView":3,"componentType":5126,"count":3,"type":"VEC4"},{"bufferView":4,"componentType":5123,"count":3,"type":"SCALAR"},{"bufferView":5,"componentType":5126,"count":2,"type":"SCALAR","min":[0],"max":[1]},{"bufferView":6,"componentType":5126,"count":2,"type":"VEC3"},{"bufferView":7,"componentType":5126,"count":1,"type":"MAT4"}],"animations":[{"name":"idle","channels":[{"sampler":0,"target":{"node":1,"path":"translation"}}],"samplers":[{"input":5,"output":6}]}]}
        """)!;var raw=Encoding.UTF8.GetBytes(json.ToJsonString());int length=(raw.Length+3)/4*4;var result=new byte[12+8+length+8+248];"glTF"u8.CopyTo(result);BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4),2);BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(8),result.Length);BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(12),length);"JSON"u8.CopyTo(result.AsSpan(16));result.AsSpan(20,length).Fill((byte)' ');raw.CopyTo(result,20);BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(20+length),248);"BIN\0"u8.CopyTo(result.AsSpan(24+length));binary.ToArray().CopyTo(result,28+length);return result;
    }
}
