using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public static class AuthoringChecks
{
    public static async Task Run(EditorShell shell,string mode)
    {
        var marker=SourceChecks.Marker(shell);var session=shell.Session;var sources=session.Sources;
        if(mode=="world-mixed-save")
        {
            await WorldChecks.Run(shell,"world-file-apply");
            await shell.RequestWorkspaceAsync("world");var app=(WorldWorkspace)shell.Active!;app.ShowSources();var view=app.SourceView!;
            var key=sources.Store!.Key(SourceRoot.ServerData,"ItemSells.json");await view.OpenAsync(key);
            var field=view.FindChildren("SourceField_ItemId","LineEdit",true,false).OfType<LineEdit>().Single(x=>x.IsVisibleInTree());field.Text="not a numeric item";field.EmitSignal(LineEdit.SignalName.TextChanged,field.Text);
            var hidden=sources.Store.Key(SourceRoot.ServerData,"MakeItemGroups.json");await view.OpenAsync(hidden);
            if(!sources.HasUnsavedChanges)throw new Exception("Invalid hidden source field became clean.");
            var invalid=await sources.ApplyAsync(sources.CaptureDrafts()!);if(invalid.State!=FileApplyState.Invalid||!sources.HasUnsavedChanges)throw new Exception("Invalid hidden source field was applied or lost.");
            sources.Drafts!.Discard();if(sources.HasUnsavedChanges)throw new Exception("Discard did not reset hidden invalid fields.");return;
        }
        var saved=Path.Combine(marker["fixtureRoot"]!.GetValue<string>(),"last-client-build-manifest.txt");if(!File.Exists(saved))await BuildChecks.Run(shell);
        var previous=BuildManifest.Read(File.ReadAllText(saved).Trim());sources.Select(previous.Plan.Profile!);var store=sources.Store!;
        void Write(string path,byte[] bytes){var full=SourcePaths.Resolve(sources.Profile!,store.Key(SourceRoot.ClientProject,path),true);Directory.CreateDirectory(Path.GetDirectoryName(full)!);File.WriteAllBytes(full,bytes);}
        shell.Assets.EnsureLoaded();var prototype=new JsonObject{["Id"]=100,["ModelId"]=100,["Size"]=100,["IsMonster"]=true};
        var reference=NpcPreviewBuilder.Build(prototype);if(reference==null)throw new Exception("Installed reference model is unavailable.");int packed=Count(reference);reference.Free();if(packed==3)throw new Exception("Source fixture is indistinguishable from packed reference.");
        const string target="assets/npcs/mob_kecoon.glb";Write(target,WorldChecks.NpcVisualFixture());Write("assets/npcs/index.json","{\"100\":\"mob_kecoon\"}"u8.ToArray());
        Write("assets/npcs/plugs/source_badge.glb",WorldChecks.NpcVisualFixture());Write("assets/npcs/mob_kecoon.plugs.json","{\"plugs\":[{\"slot\":0,\"joint\":0,\"model\":\"source_badge\",\"pos\":[1,0,0],\"quat\":[0,0,0,1],\"scale\":[0.4,0.4,0.4],\"anim\":\"idle\"}]}"u8.ToArray());
        Write("assets/npcs/mob_kecoon.fxplug.json","{\"parts\":[{\"fx\":\"source_spark\",\"bone\":0,\"p\":[0,0.6,0],\"scale\":0.4}]}"u8.ToArray());
        Write("assets/fx/source_spark.json","{\"life\":30,\"parts\":[{\"type\":\"BillBoard\",\"life\":30,\"sizeW\":0.4,\"sizeH\":0.4,\"tex\":[\"source_blue\"],\"frameCount\":1,\"color\":[0.2,0.5,1,1]}]}"u8.ToArray());Write("assets/fx/tex/source_blue.png",RgbaPngCodec.Encode(new(1,1,[90,150,255,255])));
        var keyModel=store.Key(SourceRoot.ClientProject,target);var doc=sources.Drafts!.Open(keyModel);var candidate=doc.Render();int offset=candidate.AsSpan().IndexOf("0.65"u8);if(offset<0)throw new Exception("Fixture material evidence missing.");"0.25"u8.CopyTo(candidate.AsSpan(offset));doc.EditBytes(candidate);
        var plan=await BuildProjectFactory.PrepareAsync(sources,true,default);var manifest=ClientSourceView.CreateManifest(sources,plan,keyModel,"mob_kecoon") with{Prototype=prototype,AutoCloseFrames=mode=="world-authoring-shutdown"?0:30};
        await sources.Previews.OpenAsync(manifest,default);var proof=JsonNode.Parse(File.ReadAllBytes(sources.Previews.LastResultPath!))!;var authored=store.Blobs.Put(candidate);string? displayedSourceHash=proof["displayedSourceHash"]?.GetValue<string>();
        if(displayedSourceHash!=authored.Sha256)throw new Exception("Packed cache shown as authored preview");if(proof["vertices"]!.GetValue<int>()<6||proof["bones"]!.GetValue<int>()<1||proof["effects"]!.GetValue<int>()<1||proof["attachments"]!.GetValue<int>()<2)throw new Exception("Fresh source geometry/skin/attachment/effect differs from authored model.");
        if(mode=="world-authoring-shutdown")
        {
            var ownedChildren=sources.Previews.OwnedProcessIds.Select(Process.GetProcessById).ToArray();await shell.RequestWorkspaceAsync("content");var active=shell.Active;sources.Previews.MarkChanged();await sources.Previews.Completion;if(shell.Active!=active||active?.Id!="content")throw new Exception("Late source preview changed the Content workspace.");
            if(ownedChildren.Any(p=>!p.HasExited))throw new Exception("Preview process survived shutdown");foreach(var p in ownedChildren)p.Dispose();
            sources.Drafts.Discard();sources.Select(sources.Profile);if(session.Client.IsClosed)throw new Exception("Source shutdown closed DB helper.");return;
        }
        await sources.Previews.Completion;
        var change=sources.CaptureDrafts()!;var result=await sources.ApplyAsync(change);if(result.State!=FileApplyState.Applied)throw new Exception("Source model apply failed: "+result.State);
        var reopened=sources.Drafts.Open(keyModel);if(reopened.IsDirty||reopened.Snapshot.Sha256!=authored.Sha256)throw new Exception("Saved source model reopen differs from preview.");
        var installed=NpcPreviewBuilder.Build(prototype)!;int stillPacked=Count(installed);installed.Free();if(stillPacked!=packed)throw new Exception("Source process polluted parent installed cache.");
        Write("assets/objects/source_gate.glb",WorldChecks.NpcVisualFixture());
        var heightKey=store.Key(SourceRoot.ClientProject,"assets/terrain/build-fixture/heightmap.png");var heightDoc=sources.Drafts.Open(heightKey);var heightField=ClientHeightmapCodec.Decode(heightDoc.Render());heightDoc.EditBytes(ClientHeightmapCodec.Encode(TerrainBrush.Apply(heightField,new("raise",1,1,0,2,0))));
        var placementKey=store.Key(SourceRoot.ClientProject,"assets/terrain/build-fixture/placements.json");var placementSnapshot=store.Read(placementKey);var placementDoc=new SourceDocument(placementSnapshot,placementSnapshot.Content==null?"[]"u8.ToArray():store.Blobs.Read(placementSnapshot.Content));using var placementOwner=sources.Drafts.Register(placementDoc,()=>{});var placement=ClientMapCatalog.Default("placements.json");placement["name"]="source_gate";placement["p"]=new JsonArray(4,5,4);placementDoc.SetText(new JsonArray(placement).ToJsonString());
        var savedMap=await sources.ApplyAsync(sources.CaptureDrafts()!);if(savedMap.State!=FileApplyState.Applied)throw new Exception("Terrain/scenery save failed: "+savedMap.State);
        if(sources.Drafts.Open(heightKey).IsDirty||sources.Drafts.Open(placementKey).IsDirty)throw new Exception("Saved map source reopen retained unapplied drafts.");
        var mapPlan=await BuildProjectFactory.PrepareAsync(sources,false,default);var mapManifest=ClientSourceView.CreateManifest(sources,mapPlan,heightKey) with{AutoCloseFrames=30};await sources.Previews.OpenAsync(mapManifest,default);var mapProof=JsonNode.Parse(File.ReadAllBytes(sources.Previews.LastResultPath!))!;if(mapProof["displayedSourceHash"]!.GetValue<string>()!=store.Read(heightKey).Sha256||mapProof["vertices"]!.GetValue<int>()<6)throw new Exception("Saved terrain/scenery did not appear in fresh output.");await sources.Previews.Completion;
        var objectKey=store.Key(SourceRoot.ClientProject,"assets/objects/source_gate.glb");var objectPlan=await BuildProjectFactory.PrepareAsync(sources,false,default);var objectManifest=ClientSourceView.CreateManifest(sources,objectPlan,objectKey,"source_gate") with{AutoCloseFrames=30};await sources.Previews.OpenAsync(objectManifest,default);var objectProof=JsonNode.Parse(File.ReadAllBytes(sources.Previews.LastResultPath!))!;if(objectProof["modelResourcePath"]!.GetValue<string>()!=objectKey.RelativePath||objectProof["vertices"]!.GetValue<int>()!=3||objectProof["attachments"]!.GetValue<int>()!=0)throw new Exception("Object source preview loaded an NPC resource/assembly.");await sources.Previews.Completion;
        await shell.RequestWorkspaceAsync("world");var world=(WorldWorkspace)shell.Active!;world.ShowSources();await world.SourceView!.OpenAsync(keyModel);await world.SourceView.PreviewReady;
        foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,800)}){shell.GetWindow().Size=size;await EventGalleries.Capture(shell,"world-source-authoring",size);}
        if(sources.Previews.OwnedProcessIds.Count>0)throw new Exception("Source preview left owned processes.");
    }
    static int Count(Node root)=>root.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null).Sum(m=>Enumerable.Range(0,m.Mesh.GetSurfaceCount()).Sum(i=>m.Mesh.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length));
}
