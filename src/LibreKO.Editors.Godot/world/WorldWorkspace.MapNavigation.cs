using Godot;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public partial class WorldWorkspace
{
    readonly Dictionary<Control,bool> expandedVisibility=[];
    bool expandedMap, mapFullscreen;
    Window.ModeEnum previousWindowMode;
    internal bool MapExpanded=>expandedMap;
    internal bool MapFullscreen=>mapFullscreen;
    async Task FocusOpenedSpawnAsync(WorldKey key)
    {
        if(key.Table!=WorldTable.NpcPositions||Selected!=key)return;
        await PreviewReady;
        if(!Active||Selected!=key||!MapView.Rows.ContainsKey(key))return;
        MapView.SetSelection([key],false);MapView.FocusSelection();Session.Shell.RememberContext();
    }
    internal async Task FocusRelatedSpawnAsync(WorldRowSnapshot row)
    {
        if(row.Key.Table!=WorldTable.NpcPositions)return;
        await PreviewReady;
        if(!Active)return;
        if(Selected?.Table==WorldTable.ZoneInfos&&MapView.Rows.ContainsKey(row.Key))
        {MapView.SetSelection([row.Key],false);MapView.FocusSelection();Session.Shell.RememberContext();}
        else await SelectAsync(row.Key);
    }
    internal void ToggleExpandedMap()
    {
        if(!expandedMap)
        {
            expandedMap=true;
            foreach(var control in expandHide){expandedVisibility[control]=control.Visible;control.Visible=false;}
        }
        else
        {
            expandedMap=false;foreach(var pair in expandedVisibility)pair.Key.Visible=pair.Value;expandedVisibility.Clear();
            if(mapFullscreen){GetWindow().Mode=previousWindowMode;mapFullscreen=false;}
        }
        MapView.SetExpanded(expandedMap);
    }
    internal void ToggleMapFullscreen()
    {
        if(!Active||!MapView.Visible)return;
        if(mapFullscreen){GetWindow().Mode=previousWindowMode;mapFullscreen=false;return;}
        if(!expandedMap)ToggleExpandedMap();
        previousWindowMode=GetWindow().Mode;GetWindow().Mode=Window.ModeEnum.Fullscreen;mapFullscreen=true;
    }
    internal bool ExitMapExpansion()
    {if(!expandedMap&&!mapFullscreen)return false;if(expandedMap)ToggleExpandedMap();else{GetWindow().Mode=previousWindowMode;mapFullscreen=false;}return true;}
    readonly List<Control> expandHide=[];
}
