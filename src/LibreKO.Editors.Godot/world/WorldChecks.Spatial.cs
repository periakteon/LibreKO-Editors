using System.Text.Json;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Maps;
namespace LibreKO.Editors.Desktop;
public static partial class WorldChecks
{
    static async Task Spatial(EditorShell shell)
    {
        _=SourceChecks.Marker(shell);var session=shell.Session;
        if(!session.ConnectionString.Contains("33307")||!session.ConnectionString.Contains("libreko_editor_test_native"))throw new Exception("Spatial checks require the isolated fixture.");
        await shell.RequestWorkspaceAsync("world");var app=(WorldWorkspace)shell.Active!;await app.SelectAsync(new(WorldTable.ZoneInfos,210));
        await app.PreviewReady;var view=app.MapView;
        var heights=new HeightField(3,4,[0,1,2,3,4,5,6,7,8]);await view.LoadAsync(heights,new(WorldTable.NpcPositions,Zone:210),default);
        var a=new WorldKey(WorldTable.NpcPositions,885001);var b=new WorldKey(WorldTable.NpcPositions,886000);view.SetSelection([a,b]);
        var clean=JsonSerializer.Serialize(app.Draft!.Rows);view.BeginManipulation();view.PreviewMove(new(4,0,2));
        if(app.Draft.IsDirty)throw new Exception("Spatial preview wrote a draft before release.");view.CommitManipulation();
        if(!app.Draft.IsDirty)throw new Exception("Completed multi-drag was not applied.");app.Undo(false);
        if(app.Draft.IsDirty||clean!=JsonSerializer.Serialize(app.Draft.Rows))throw new Exception("Multi-drag was not one undo step.");
        var baseline=JsonSerializer.Serialize(app.Draft.Rows);view.BeginManipulation();view.PreviewMove(new(8,0,4));view.CancelManipulation();
        if(baseline!=JsonSerializer.Serialize(app.Draft.Rows))throw new Exception("Escape cancellation changed persisted values.");
        view.ApplyPatrolPoints(a,[new(12,0,47),new(20,0,35)]);
        if(app.Draft.Rows.Single(r=>r.Key==a).Values["Path"]?.ToString()!="0012004700200035")throw new Exception("Patrol edit did not encode the reviewed points.");
        app.Undo(false);if(baseline!=JsonSerializer.Serialize(app.Draft.Rows))throw new Exception("Patrol edit was not one undo step.");
        var provisional=a with{Id=Random.Shared.Next(900_000_000,1_000_000_000)};var provisionalValues=(System.Text.Json.Nodes.JsonObject)app.Draft.Rows.Single(r=>r.Key==a).Values.DeepClone();provisionalValues["Index"]=provisional.Id;
        app.ApplyBatch([new(provisional,null,provisionalValues)]);if(!view.Rows.ContainsKey(provisional))throw new Exception("New spawn draft has no viewport marker.");
        app.Undo(false);if(view.Rows.ContainsKey(provisional))throw new Exception("Undo left a provisional spawn in the viewport.");
        var remove=app.Draft.Rows.Single(r=>r.Key==a);app.ApplyBatch([new(a,remove,null)]);
        if(((Node3D)view.FindChild("WorldRow_NpcPositions_"+a.Id,true,false)).Visible)throw new Exception("Staged spawn removal remains visible.");
        app.Undo(false);if(!((Node3D)view.FindChild("WorldRow_NpcPositions_"+a.Id,true,false)).Visible)throw new Exception("Undo did not restore the spawn marker.");
        view.ProcessInput(new InputEventMouseMotion { Relative=new(20,10),ButtonMask=MouseButtonMask.Right });
        view.ProcessInput(new InputEventMouseButton { Pressed=true,ButtonIndex=MouseButton.WheelUp });view.TopView();view.FocusSelection();
        if(app.Draft.IsDirty)throw new Exception("Camera-only input dirtied the world.");
        var roundtrip=MapSpace.FromGodot(MapSpace.ToGodot(new(12,3,47)));if(roundtrip!=new MapPoint(12,3,47))throw new Exception("Map coordinate mirror drifted.");
        var vertical=view.Rows.Keys.First(k=>k.Table==WorldTable.MapObjects);view.SetSelection([vertical]);view.FocusSelection();await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame);view.SnapGrid=false;view.SnapTerrain=false;
        var verticalNode=(Node3D)view.FindChild("WorldRow_MapObjects_"+vertical.Id,true,false);float beforeY=verticalNode.Position.Y;var cameraPose=view.CaptureCamera();var start=view.SourceScreen(verticalNode.Position+Vector3.Up*(.3f+Math.Clamp(cameraPose.Distance*.06f,2,20)));
        view.ProcessInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=start});for(int i=1;i<=10;i++)view.ProcessInput(new InputEventMouseMotion{Position=start+new Vector2(0,-3*i),Relative=new(0,-3),ButtonMask=MouseButtonMask.Left});
        if(Math.Abs(verticalNode.Position.Y-beforeY-30*cameraPose.Distance*.004f)>.001f)throw new Exception("Vertical drag lost accumulated mouse displacement.");view.ProcessInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=start+new Vector2(0,-30)});app.Undo(false);view.SnapGrid=true;view.SnapTerrain=true;
        using var cancel=new CancellationTokenSource();var loading=view.LoadAsync(new(257,4,new float[257*257]),new(WorldTable.NpcPositions,Zone:210),cancel.Token);cancel.Cancel();
        try{await loading;}catch(OperationCanceledException){}
        await view.LoadAsync(heights,new(WorldTable.NpcPositions,Zone:212),default);
        var npcKey=new WorldKey(WorldTable.NpcPositions,885002);
        if(view.PrototypeKeys[npcKey].IsMonster!=false)throw new Exception("NPC visual used the monster with the same numeric ID.");
        await view.LoadAsync(heights,new(WorldTable.NpcPositions,Zone:210),default);
        if(view.PrototypeKeys[a].IsMonster!=true)throw new Exception("Monster visual used the NPC with the same numeric ID.");
        var visualFixture=await session.Client.SendAsync<WorldRowSnapshot>("world.get",new WorldKey(WorldTable.Npcs,880019,IsMonster:true));var temporary=NpcPreviewBuilder.Build(visualFixture.Values)??throw new Exception("NPC lifetime fixture model missing");
        var animation=NpcPreviewBuilder.Find<AnimationPlayer>(temporary)??throw new Exception("NPC lifetime fixture animation missing");var animationId=animation.GetInstanceId();
        var metadata=(System.Collections.IDictionary)typeof(LibreKO.CharacterPreview).Assembly.GetType("LibreKO.Presentation.ModelNodes")!.GetField("AnimationMetaByIndex",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        shell.AddChild(temporary);temporary.Free();if(metadata.Contains(animationId))throw new Exception("Freed NPC preview retained instance animation metadata.");
        var malformed=new System.Text.Json.Nodes.JsonObject { ["Id"]=1,["IsMonster"]=true,["ModelId"]=int.MaxValue,["Size"]=100 };
        var missing=NpcPreviewBuilder.Build(malformed);if(missing!=null)missing.Free();
        var pending=view.LoadAsync(new(257,4,new float[257*257]),new(WorldTable.NpcPositions,Zone:210),default);await shell.RequestWorkspaceAsync("events");
        try{await pending;}catch(OperationCanceledException){}
        await shell.RequestWorkspaceAsync("world");if(app.Draft!.IsDirty)throw new Exception("Switching during terrain loading dirtied the draft.");
        if(shell.GetTree().Root.GetNodeOrNull("Net")!=null||shell.GetTree().Root.GetNodeOrNull("Boot")!=null)throw new Exception("World preview started a game session.");
    }
}
