using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Maps;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public static partial class WorldChecks
{
    static async Task Smd(EditorShell shell)
    {
        _=SourceChecks.Marker(shell);var session=shell.Session;await session.Sources.EnsureRegisteredAsync();
        var store=session.Sources.Store!;var profile=session.Sources.Profile!;
        var original=SourcePaths.Resolve(profile,store.Key(SourceRoot.ServerMaps,"native-world.smd"),false);
        var doc=SmdCodec.Read(File.ReadAllBytes(original));
        if(doc.Warps.Count==0)doc.Warps.Add(new(7,"Native source warp","Fixture",0,210,new(12,3,16),2,0));
        if(doc.Regene.Count==0)doc.Regene.Add(new(new(14,3,16),2,3));
        var key=new SourceFileKey(profile.Id,SourceRoot.ServerMaps,SourceIdentity.Root(profile.Roots[SourceRoot.ServerMaps]),"native-world.smd");
        var fixturePath=SourcePaths.Resolve(profile,key,true);File.WriteAllBytes(fixturePath,SmdCodec.Write(doc));var before=store.Read(key);
        await shell.RequestWorkspaceAsync("world");var app=(WorldWorkspace)shell.Active!;await app.SelectAsync(new(WorldTable.ZoneInfos,210));await app.PreviewReady;
        await app.MapView.LoadAsync(SmdHeightAdapter.ToCanonical(doc),new(WorldTable.NpcPositions,Zone:9999),default);
        var source=new SmdMapLayers(app.MapView,session);source.Bind(doc,before);source.Select("Warps",0);source.Enabled=true;
        var tools=Ui.Add(app.PreviewHost,new SmdTools(session,source));
        var serverHeight=shell.FindChildren("*","Button",true,false).OfType<Button>().SingleOrDefault(b=>b.Text=="Set server height")??throw new Exception("Server-only SMD height authoring is inaccessible.");
        var heightX=shell.FindChildren("SmdHeightX","LineEdit",true,false).OfType<LineEdit>().Single();var heightZ=shell.FindChildren("SmdHeightZ","LineEdit",true,false).OfType<LineEdit>().Single();var heightValue=shell.FindChildren("SmdHeightValue","LineEdit",true,false).OfType<LineEdit>().Single();heightX.Text="1";heightZ.Text="2";heightValue.Text="987";serverHeight.EmitSignal(BaseButton.SignalName.Pressed);await source.TerrainReady;if(source.Document.Heights.Heights[1*source.Document.Heights.Size+2]!=987||app.MapView.Heights!.Heights[2*source.Document.Heights.Size+1]!=987)throw new Exception("Server-only height controls wrote or previewed the wrong coordinate.");source.Undo();await source.TerrainReady;
        if(!shell.FindChildren("*","Button",true,false).OfType<Button>().Any(b=>b.Text=="Import verified collision section…"))throw new Exception("Verified SMD section import is inaccessible.");
        shell.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Import verified collision section…").EmitSignal(BaseButton.SignalName.Pressed);var picker=shell.GetChildren().OfType<FileDialog>().Single(d=>d.Name=="SmdCollisionSectionImport");picker.EmitSignal(FileDialog.SignalName.FileSelected,original);var importReview=shell.GetChildren().OfType<ConfirmationDialog>().SingleOrDefault(d=>d.Title=="Review SMD collision section import")??throw new Exception("Collision import review failed: "+shell.FindChildren("SmdToolsError","Label",true,false).OfType<Label>().Single().Text);importReview.EmitSignal(ConfirmationDialog.SignalName.Confirmed);await source.TerrainReady;if(source.Draft.IsDirty)throw new Exception("Importing the identical collision section changed other SMD bytes.");
        var initial=source.Document.Warps[0];source.BeginGesture();source.PreviewMove(initial.Position with{X=initial.Position.X+4});
        if(source.Draft.IsDirty)throw new Exception("SMD preview changed draft before release.");source.CommitGesture();if(!source.Draft.IsDirty)throw new Exception("SMD move was lost.");
        source.Undo();if(source.Draft.IsDirty)throw new Exception("Source drag was not one undo.");source.Redo();source.BeginGesture();source.PreviewMove(new(99,2,99));source.CancelGesture();
        if(source.Document.Warps[0].Position.X!=initial.Position.X+4)throw new Exception("Cancelled source gesture changed committed draft.");
        source.Undo();source.BeginGesture();source.Paint(1,2,0);source.Paint(2,2,0);source.CommitGesture();source.Undo();if(source.Draft.IsDirty)throw new Exception("Tile stroke was not one undo.");
        if(store.Read(key).Sha256!=before.Sha256)throw new Exception("SMD manipulation wrote its target.");
        SmdInspector.Show(session,source);await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame);
        var inspector=shell.GetChildren().OfType<ConfirmationDialog>().Single(x=>x.Title.StartsWith("SMD Warps"));inspector.Hide();inspector.QueueFree();
        foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,800)}){shell.GetWindow().Size=size;await EventGalleries.Capture(shell,"world-smd",size);}
        tools.QueueFree();source.Enabled=false;app.MapView.SourceLayers=null;
    }
}
