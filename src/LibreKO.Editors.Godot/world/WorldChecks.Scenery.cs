using System.Text;
using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public static partial class WorldChecks
{
    static async Task Scenery(EditorShell shell)
    {
        _=SourceChecks.Marker(shell);var session=shell.Session;var store=session.Sources.Store!;var stem="native-scenery";
        SourceFileKey Key(string name)=>store.Key(SourceRoot.ClientProject,"assets/terrain/"+stem+"/"+name);
        void Write(string name,byte[] bytes){var path=SourcePaths.Resolve(store.Profile,Key(name),true);Directory.CreateDirectory(Path.GetDirectoryName(path)!);SourcePaths.Resolve(store.Profile,Key(name),true);File.WriteAllBytes(path,bytes);}
        Write("heightmap.png",ClientHeightmapCodec.Encode(new(3,4,new float[9])));
        void WriteAsset(string relative,byte[] bytes){var key=store.Key(SourceRoot.ClientProject,relative);var path=SourcePaths.Resolve(store.Profile,key,true);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);}
        WriteAsset("assets/objects/source_gate.glb",NpcVisualFixture());WriteAsset("assets/objects/edited_gate.glb",NpcVisualFixture());WriteAsset("assets/fx/native_spark.json","{\"life\":1,\"parts\":[]}"u8.ToArray());
        foreach(var name in new[]{"placements.json","areas.json","water.json","fx_placements.json"}){var row=ClientMapCatalog.Default(name);if(name=="placements.json"){row["name"]="source_gate";row["e"]=72;row["custom"]=9007199254740993L;}if(name=="fx_placements.json")row["fx"]="native_spark";Write(name,Encoding.UTF8.GetBytes(new JsonArray(row).ToJsonString()));}
        MapPoint[] originalCollision=[new(0,1,0),new(7,1,0),new(0,1,7)];Write("collision.bin",CollisionCodec.Write(originalCollision));var before=store.Read(Key("placements.json"));var collisionBefore=store.Read(Key("collision.bin"));
        await shell.RequestWorkspaceAsync("world");var app=(WorldWorkspace)shell.Active!;app.ShowSources();await app.SourceView!.OpenAsync(before.Key);await app.SourceView.PreviewReady;
        SceneryTools View()=>app.SourceView.FindChildren("*","",true,false).OfType<SceneryTools>().Single(v=>v.IsVisibleInTree());
        var view=View();view.Transform(new(2,1,3),[0,0,.6f,.8f],new(2,1,2));view.Duplicate();view.Move(-1);view.Remove();view.SetProperty("name",JsonValue.Create("edited_gate"));
        var mutation=session.Sources.CaptureDrafts()!;if(mutation.Files.Count!=1||store.Read(collisionBefore.Key).Sha256!=collisionBefore.Sha256)throw new Exception("Moving scenery implicitly edited collision.");
        var values=JsonNode.Parse(Encoding.UTF8.GetString(store.Blobs.Read(mutation.Files[0].After!)))!.AsArray()[0]!.AsObject();if((long)values["custom"]! !=9007199254740993L||(int)values["e"]! !=72)throw new Exception("Scenery operations lost metadata.");
        await app.SourceView.OpenAsync(Key("water.json"));await app.SourceView.PreviewReady;view=View();view.SetProperty("level_y",JsonValue.Create(5f));var waterRow=ClientMapCatalog.Default("water.json");foreach(var v in waterRow["verts"]!.AsArray())v!.AsArray()[1]=5f;view.SetProperty("verts",waterRow["verts"]);
        await app.SourceView.OpenAsync(Key("fx_placements.json"));await app.SourceView.PreviewReady;View().Transform(new(4,2,6),[0,1,0,0],new(3,3,3));
        await app.SourceView.OpenAsync(Key("areas.json"));await app.SourceView.PreviewReady;View().SetProperty("maxX",JsonValue.Create(7));
        await app.SourceView.OpenAsync(Key("collision.bin"));await app.SourceView.PreviewReady;var collision=app.SourceView.FindChildren("*","",true,false).OfType<CollisionTools>().Single(v=>v.IsVisibleInTree());collision.Transform([0],new(1,2,3));
        var change=session.Sources.CaptureDrafts()!;if(change.Files.Count!=5)throw new Exception("Hidden client source changes were not captured.");foreach(var file in change.Files){var issues=ClientMapCatalog.Validate(file.Before.Key,store.Blobs.Read(file.After!));if(issues.Count>0)throw new Exception(string.Join("; ",issues.Select(x=>x.Message)));}
        var invalid=change.Files.Single(f=>f.Before.Key.RelativePath.EndsWith("fx_placements.json"));var invalidDocument=JsonNode.Parse(Encoding.UTF8.GetString(store.Blobs.Read(invalid.After!)))!;invalidDocument[0]!["q"]=new JsonArray(0,0,0,0);var bad=await session.Sources.ApplyAsync(new(Guid.NewGuid(),[invalid with{After=store.Blobs.Put(Encoding.UTF8.GetBytes(invalidDocument.ToJsonString()))}]));if(bad.State!=FileApplyState.Invalid)throw new Exception("Invalid changed quaternion reached Apply.");
        if(store.Read(before.Key).Sha256!=before.Sha256)throw new Exception("Source tools wrote before reviewed Apply.");
        var unique="editor_scenery_probe_"+Guid.NewGuid().ToString("N");var packPath=Path.Combine(session.Profile.WorkspaceRoot,unique+".pck");using(var packer=new PckPacker())
        {if(packer.PckStart(packPath)!=Error.Ok)throw new Exception("Cannot start scenery reader fixture.");foreach(var file in change.Files)if(packer.AddFileFromBuffer("res://assets/terrain/"+unique+"/"+Path.GetFileName(file.Before.Key.RelativePath),store.Blobs.Read(file.After!))!=Error.Ok)throw new Exception("Cannot stage stock-reader fixture.");if(packer.Flush()!=Error.Ok)throw new Exception("Cannot finish stock-reader fixture.");}
        if(!ProjectSettings.LoadResourcePack(packPath,false))throw new Exception("Cannot mount unique scenery fixture.");var stock=LibreKO.ZoneObjects.LoadJson(unique)!.Items.Single();if(stock.Name!="edited_gate"||stock.MapPos!=new Vector3(2,1,3)||stock.EventId!=72)throw new Exception("Stock placement reader disagrees.");
        if(LibreKO.ZoneWater.LoadJson(unique)!.Patches.Single().Verts.Any(v=>v.Y!=5)||LibreKO.FxPlacements.LoadJson(unique)!.Items.Single().Scale!=3||LibreKO.ZoneCollision.Load(unique)!.Verts[0]!=new Vector3(1,3,3))throw new Exception("Stock water/FX/collision reader disagrees.");
        foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,800)}){shell.GetWindow().Size=size;shell.GetViewport().WarpMouse(new(4,4));await EventGalleries.Capture(shell,"world-scenery",size);}
        var applied=await session.Sources.ApplyAsync(change);if(applied.State!=FileApplyState.Applied||session.Sources.HasUnsavedChanges)throw new Exception("Client source batch did not rebase after Apply.");foreach(var file in change.Files)if(store.Read(file.Before.Key).Sha256!=file.After!.Sha256)throw new Exception("Client source save differs from reviewed bytes.");
    }
}
