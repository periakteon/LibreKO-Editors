using LibreKO.Editors.Core;
using System.Text.Json.Nodes;

namespace LibreKO.Editors.Desktop;
public partial class EditorApp
{
    public WorkspaceLocation CaptureLocation() => new(Id, new JsonObject
    {
        ["identity"] = $"{Module}:{SelectedId}", ["module"] = Module, ["id"] = SelectedId,
        ["search"] = search.Text, ["page"] = PageNumber, ["filters"] = filters.DeepClone(),
        ["tab"] = Inspector.CurrentTab,
        ["scroll"] = Properties.GetParent<Godot.ScrollContainer>().ScrollVertical
    });
    public async Task RestoreLocationAsync(WorkspaceLocation location)
    {
        var state = location.State;
        ClearSelection(); Module = state["module"]!.GetValue<int>();
        search.Text = state["search"]!.GetValue<string>(); PageNumber = state["page"]!.GetValue<int>();
        filters = (JsonObject)state["filters"]!.DeepClone();
        await LoadList();
        if (state["id"]!.GetValue<int>() is > 0 and var id) await SelectRecord(id);
        await PropertiesReady;
        Inspector.CurrentTab = state["tab"]!.GetValue<int>();
        Properties.GetParent<Godot.ScrollContainer>().SetDeferred("scroll_vertical", state["scroll"]!.GetValue<int>());
    }
}
