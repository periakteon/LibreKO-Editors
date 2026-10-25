using LibreKO.Editors.Core;
using LibreKO.Editors.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LibreKO.Editors.Desktop;
public partial class EventsWorkspace
{
    public WorkspaceLocation CaptureLocation() => new(Id, new JsonObject
    {
        ["identity"] = $"{Family}:{Selected}", ["family"] = (int)Family,
        ["key"] = JsonSerializer.SerializeToNode(Selected, JsonConfig.Options),
        ["row"] = JsonSerializer.SerializeToNode(SelectedRow, JsonConfig.Options),
        ["table"] = SelectedTable == null ? null : (int)SelectedTable,
        ["search"] = search.Text, ["page"] = page
    });
    public async Task RestoreLocationAsync(WorkspaceLocation location)
    {
        var s = location.State; Family = (EventFamily)s["family"]!.GetValue<int>();
        search.Text = s["search"]!.GetValue<string>(); page = s["page"]!.GetValue<int>();
        SelectedTable = s["table"] == null ? null : (EventTable)s["table"]!.GetValue<int>();
        SelectedRow = s["row"]?.Deserialize<EventRowKey>(JsonConfig.Options);
        foreach (var pair in familyButtons) Ui.Navigation(pair.Value, pair.Key == Family);
        Catalog ??= await Session.Client.SendAsync<EventCatalog>("events.catalog", new { });
        await LoadListAsync();
        if (s["key"]?.Deserialize<EventDocumentKey>(JsonConfig.Options) is {} key) await SelectAsync(key);
        else { Selected = null; Draft = null; RenderDocument(); }
    }
}
