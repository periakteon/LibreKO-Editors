using Godot;
using LibreKO.Editors.Contracts;
using System.Text.Json.Nodes;

namespace LibreKO.Editors.Desktop;
public static class NavigationChecks
{
    public static async Task Run(EditorShell shell)
    {
        if (!shell.Session.Connected || !shell.Session.ConnectionString.Contains("libreko_editor_test_")) throw new Exception("Use isolated navigation fixtures.");
        await shell.RequestWorkspaceAsync("content");
        var content = (EditorApp)shell.Active!; content.Module = 0;
        await content.SelectRecord(123450001); await content.PropertiesReady;
        content.search.Text = "Raptor"; content.Inspector.CurrentTab = 1; shell.RememberContext();
        var location = content.CaptureLocation();
        await shell.RequestWorkspaceAsync("world"); var world = (WorldWorkspace)shell.Active!;
        var rows = await shell.Session.Client.SendAsync<Page<WorldRowSnapshot>>("world.list", new WorldQuery(WorldTable.Npcs));
        var key = rows.Rows.First().Key; await world.SelectAsync(key);
        var worldLocation = world.CaptureLocation();
        await shell.OpenTargetAsync(new("content", "items", new JsonObject { ["id"] = 123450001 }, shell.Session.HistoryRoot));
        await shell.NavigateHistoryAsync(false);
        if (shell.Active != world || world.Selected != key) throw new Exception("Related link Back lost the originating NPC.");
        var priorFailure=shell.NavigationHistory.Current!;
        await shell.OpenTargetAsync(new("content","items",new JsonObject{["id"]=1999999999},shell.Session.HistoryRoot));
        if(shell.Active!=world||!shell.NavigationHistory.Current!.Matches(priorFailure))throw new Exception("A failed reference changed the screen or browser history.");
        await shell.NavigateHistoryAsync(true);
        if (shell.Active != content || content.SelectedId != 123450001) throw new Exception("Forward lost item destination.");
        content.Draft!.Set("Damage", content.Draft.Values["Damage"]!.GetValue<int>() + 1);
        var current = shell.NavigationHistory.Current!;
        var back = shell.NavigateHistoryAsync(false);
        await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
        var dialog = shell.GetChildren().OfType<ConfirmationDialog>().Single();
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); await back;
        if (shell.Active != content || !content.Draft.IsDirty || !shell.NavigationHistory.Current!.Matches(current)) throw new Exception("Cancel moved navigation or discarded a draft.");
        content.Discard();
        await shell.NavigateHistoryAsync(false);
        await shell.RequestWorkspaceAsync("events"); var events = (EventsWorkspace)shell.Active!;
        var eventLocation = events.CaptureLocation();
        await shell.RequestWorkspaceAsync(null); await shell.NavigateHistoryAsync(false);
        if (shell.Active != events || !events.CaptureLocation().Matches(eventLocation)) throw new Exception("Event history did not restore.");
        await content.RestoreLocationAsync(location);
        if (!content.CaptureLocation().Matches(location)) throw new Exception("Content filters, selection or tab lost on restore.");
        await shell.RequestWorkspaceAsync("world");world.ShowSources();
        var sourceKey=shell.Session.Sources.Store!.Key(SourceRoot.ServerData,"Npcs.json");await world.SourceView!.OpenAsync(sourceKey);
        var sourceRows=world.SourceView.FindChildren("*","OptionButton",true,false).OfType<OptionButton>().Single(o=>o.ItemCount>1&&o.GetItemText(0).StartsWith("/0"));
        sourceRows.Select(1);sourceRows.EmitSignal(OptionButton.SignalName.ItemSelected,1L);
        if(world.SourceView.SelectedRowOrdinal!=1)throw new Exception("Source row selection was not captured.");
        var sourceLocation=world.CaptureLocation();await shell.RequestWorkspaceAsync("content");await shell.NavigateHistoryAsync(false);
        if(shell.Active!=world||!world.CaptureLocation().Matches(sourceLocation))throw new Exception("Back did not restore the selected source document and row.");
        GD.Print("Browser history: related NPC→item Back/Forward, dirty Cancel, Events and Content context verified");
    }
}
