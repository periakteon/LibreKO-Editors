using Godot;
using LibreKO.Editors.Core;
using LibreKO.Editors.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LibreKO.Editors.Desktop;
public partial class WorldWorkspace
{
    public WorkspaceLocation CaptureLocation()
    {
        var pose = MapView.CaptureCamera();
        var source = sourceMode&&sourceViewGeneration==Session.Sources.Generation ? SourceView?.SelectedDocument?.Snapshot.Key : null;
        return new(Id, new JsonObject
        {
            ["identity"] = sourceMode ? $"source:{source}:{SourceView?.SelectedRowOrdinal}" : $"{Table}:{Selected}",
            ["table"] = (int)Table, ["key"] = JsonSerializer.SerializeToNode(Selected, JsonConfig.Options),
            ["search"] = search.Text, ["page"] = page, ["filter"] = kindFilter.Selected,
            ["sourceMode"] = sourceMode, ["source"] = JsonSerializer.SerializeToNode(source, JsonConfig.Options),
            ["row"] = SourceView?.HasRowSelection==true?SourceView.SelectedRowOrdinal:null,
            ["contextTab"]=contextTabs.CurrentTab,["contents"]=Selected?.Table is WorldTable.Npcs or WorldTable.NpcItems or WorldTable.MakeItemGroups?Contents.CaptureState():null,
            ["camera"] = new JsonArray(pose.Focus.X, pose.Focus.Y, pose.Focus.Z, pose.Distance, pose.Yaw, pose.Pitch, pose.Free, (int)pose.Projection, pose.Size)
        });
    }
    public async Task RestoreLocationAsync(WorkspaceLocation location)
    {
        var s = location.State;
        if (s["sourceMode"]!.GetValue<bool>())
        {
            ShowSources();
            if (s["source"]?.Deserialize<SourceFileKey>(JsonConfig.Options) is {} source)
            { await SourceView!.OpenAsync(source);if(s["row"] is {} ordinal)SourceView.SelectSourceRow(ordinal.GetValue<int>()); }
            else SourceView?.ShowLibrary();
            return;
        }
        ShowDatabase(); Table = (WorldTable)s["table"]!.GetValue<int>(); SyncTable();
        search.Text = s["search"]!.GetValue<string>(); page = s["page"]!.GetValue<int>(); kindFilter.Select(s["filter"]!.GetValue<int>());
        Catalog ??= await Session.Client.SendAsync<WorldCatalog>("world.catalog", new { });
        await LoadListAsync();
        if (s["key"]?.Deserialize<WorldKey>(JsonConfig.Options) is {} key)
        {
            await LoadSelectionAsync(key);
            if (Selected != key) throw new InvalidOperationException("The saved destination no longer exists.");
            await PreviewReady;
        }
        else { Selected = null; Draft = null; RenderDocument(); }
        var c = (JsonArray)s["camera"]!;
        MapView.RestoreCamera(new(new(c[0]!.GetValue<float>(), c[1]!.GetValue<float>(), c[2]!.GetValue<float>()), c[3]!.GetValue<float>(), c[4]!.GetValue<float>(), c[5]!.GetValue<float>(), c[6]!.GetValue<bool>(), (Camera3D.ProjectionType)c[7]!.GetValue<int>(), c[8]!.GetValue<float>()));
        await Contents.ReadyTask;
        if(s["contents"] is JsonObject contents)await Contents.RestoreStateAsync(contents);
        contextTabs.CurrentTab=s["contextTab"]?.GetValue<int>()??0;
    }
}
