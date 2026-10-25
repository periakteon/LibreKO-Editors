using Godot;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public static class WorldNavigationChecks
{
    public static async Task Run(EditorShell shell)
    {
        _=SourceChecks.Marker(shell);
        await shell.RequestWorkspaceAsync("world");var app=(WorldWorkspace)shell.Active!;
        await app.SelectAsync(new(WorldTable.NpcPositions,885001));await app.PreviewReady;
        var expected=app.MapView.PositionOf(app.Draft!.Rows.Single(r=>r.Key==app.Selected));
        var camera=app.MapView.CaptureCamera();
        if(camera.Focus.DistanceTo(expected)>.01f||camera.Distance>60)throw new Exception("Selecting a spawn did not zoom to its exact mirrored position.");
        app.MapView.ProcessInput(new InputEventMouseMotion{Relative=new(30,10),ButtonMask=MouseButtonMask.Right});camera=app.MapView.CaptureCamera();
        var row=app.Draft.Rows.Single();app.SetValue(row.Key,"RegTime",row.Values["RegTime"]!.GetValue<int>()+1);
        if(app.MapView.CaptureCamera()!=camera)throw new Exception("Editing a spawn reset the camera.");
        var expand=app.FindChildren("*","Button",true,false).OfType<Button>().SingleOrDefault(b=>b.Text=="Expand map");
        if(expand==null)throw new Exception("Expanded map workspace is missing.");
        expand.EmitSignal(BaseButton.SignalName.Pressed);await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame);
        var exit=shell.FindChildren("*","Button",true,false).OfType<Button>().SingleOrDefault(b=>b.IsVisibleInTree()&&b.Text=="Exit expanded map");
        if(exit==null||app.MapView.CaptureCamera()!=camera||!app.Draft.IsDirty)throw new Exception("Expanded map lost context or its exit action.");
        exit.EmitSignal(BaseButton.SignalName.Pressed);await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame);
        if(app.MapView.CaptureCamera()!=camera||!app.Draft.IsDirty)throw new Exception("Restoring map layout lost the draft/camera.");
        app.Discard();
        await app.SelectAsync(new(WorldTable.ZoneInfos,210));await app.PreviewReady;
        var deadline=DateTime.UtcNow.AddSeconds(15);
        var related=app.Related.FindChildren("*","ItemList",true,false).OfType<ItemList>().Single();
        while(related.ItemCount==0&&DateTime.UtcNow<deadline)await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame);
        related.Select(0);related.EmitSignal(ItemList.SignalName.ItemSelected,0L);
        if(app.Selected?.Table!=WorldTable.ZoneInfos||app.MapView.CaptureCamera().Distance>60)throw new Exception("Zone spawn click lost the zone or did not focus.");
        foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,800),new Vector2I(1280,760)})
        {
            shell.GetWindow().Size=size;await EventGalleries.Capture(shell,"world-navigation",size);
            app.ToggleExpandedMap();await EventGalleries.Capture(shell,"world-expanded",size);app.ToggleExpandedMap();
        }
        if(DisplayServer.GetName()!="headless")
        {app.ToggleMapFullscreen();if(!app.MapFullscreen||!app.MapExpanded||shell.GetWindow().Mode!=Window.ModeEnum.Fullscreen)throw new Exception("Map full screen failed.");shell._Input(new InputEventKey{Keycode=Key.Escape,Pressed=true});if(app.MapFullscreen||app.MapExpanded)throw new Exception("Escape did not exit full screen.");}
        GD.Print("Spawn focus, zone click, retained camera, expanded map and full screen verified");
    }
}
