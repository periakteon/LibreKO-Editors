using Godot;
namespace LibreKO.Editors.Desktop;
public partial class WorldViewport
{
    void ShowControls()
    {
        var dialog=Ui.Add(session.Shell,new AcceptDialog{Title="Map controls",DialogText="Right-drag: orbit camera\nMiddle-drag: pan · Wheel: zoom\nLeft-click: select · Shift+click: select multiple\nDrag a selected marker or axis: move in the draft\nFocus selected: zoom to the selection\nTop view: orthographic overhead camera\nFly camera: click map, W/A/S/D to fly, Q/E down/up\nGrid snap: round moved coordinates to whole units\nGround snap: follow the sampled terrain height\nPatrol: edit a spawn's ordered waypoints\nLayers: show/hide map overlays\nExpand map: use the whole editor area\nF11: toggle full screen · Escape: exit expanded map\nAll placement changes remain unsaved until you choose Save.",MinSize=new(610,480)});
        dialog.Confirmed+=()=>dialog.QueueFree();dialog.Canceled+=()=>dialog.QueueFree();dialog.PopupCentered();
    }
}
