using Godot;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public static class WorldGalleries
{
    public static async Task Run(EditorShell shell)
    {
        var databases=await shell.Session.Client.SendAsync<DockerDatabase[]>("docker.discover",new{});
        var selected=databases.First(d=>d.Label.Contains("libreko-db"));var prepared=await shell.Session.Client.SendAsync<DockerDatabase>("docker.prepare",new{containerId=selected.ContainerId});await shell.Session.ConnectAsync(prepared.ConnectionString);
        await shell.RequestWorkspaceAsync("world");var app=(WorldWorkspace)shell.Active!;
        var zones=await shell.Session.Client.SendAsync<Page<WorldRowSnapshot>>("world.list",new WorldQuery(WorldTable.ZoneInfos,"Moradon",Size:1));
        if(zones.Rows.Count==0)zones=await shell.Session.Client.SendAsync<Page<WorldRowSnapshot>>("world.list",new WorldQuery(WorldTable.ZoneInfos,Size:1));
        foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,800)})
        {
            shell.GetWindow().Size=size;await app.SelectAsync(zones.Rows[0].Key);await app.PreviewReady;await EventGalleries.Capture(shell,"world-map",size);
            var spawn=await shell.Session.Client.SendAsync<Page<WorldRowSnapshot>>("world.list",new WorldQuery(WorldTable.NpcPositions,Zone:zones.Rows[0].Key.Id,Size:1));
            if(spawn.Rows.Count>0){await app.SelectAsync(spawn.Rows[0].Key);await app.PreviewReady;app.MapView.SetSelection([spawn.Rows[0].Key]);app.MapView.FocusSelection();await app.MapView.SceneryReady;await EventGalleries.Capture(shell,"world-spawn",size);}
            var npcs=await shell.Session.Client.SendAsync<Page<WorldRowSnapshot>>("world.list",new WorldQuery(WorldTable.Npcs,Size:1));
            if(npcs.Rows.Count>0){await app.SelectAsync(npcs.Rows[0].Key);await app.PreviewReady;await EventGalleries.Capture(shell,"world-npc",size);}
            var drops=await shell.Session.Client.SendAsync<Page<WorldRowSnapshot>>("world.list",new WorldQuery(WorldTable.NpcItems,Size:1));
            if(drops.Rows.Count>0){await app.SelectAsync(drops.Rows[0].Key);await EventGalleries.Capture(shell,"world-drops",size);}
        }
    }
}
