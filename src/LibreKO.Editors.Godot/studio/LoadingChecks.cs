using Godot;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Desktop;
public static class LoadingChecks
{
    public static async Task Run(EditorShell shell)
    {
        _ = SourceChecks.Marker(shell);
        var tracker = shell.Session.Loading;
        using (var map = tracker.Begin("map", "Loading terrain…"))
        {
            using var names = tracker.Begin("references", "Resolving names…");
            await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!shell.FindChildren("*", "", true, false).OfType<ActivityIndicator>().Any(i => i.IsVisibleInTree())) throw new Exception("Global activity indicator is missing.");
        }
        await shell.RequestWorkspaceAsync("world"); var world = (WorldWorkspace)shell.Active!;
        world.ShowSources(); var view = world.SourceView!;
        var key = shell.Session.Sources.Store!.Key(SourceRoot.ServerData, "Npcs.json");
        await view.OpenAsync(key); var document = view.SelectedDocument!; document.SetText(document.Text + "\n");
        var close = view.CloseSelectedAsync();
        await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
        var dialog = shell.GetChildren().OfType<ConfirmationDialog>().Single(d => d.Title == "Close source document");
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
        if (await close || view.SelectedDocument != document || !document.IsDirty) throw new Exception("Closing Cancel lost a source draft.");
        close = view.CloseSelectedAsync();
        await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
        dialog = shell.GetChildren().OfType<ConfirmationDialog>().Single(d => d.Title == "Close source document");
        dialog.EmitSignal(AcceptDialog.SignalName.CustomAction, "discard");
        if (!await close || view.SelectedDocument != null || document.IsDirty) throw new Exception("Discard did not close the source snapshot.");
        await shell.RequestWorkspaceAsync(null);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (tracker.IsBusy() && DateTime.UtcNow < deadline) await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (tracker.IsBusy()) throw new Exception("Loading activity remained after work completed.");
        GD.Print("Loading and source snapshot Close/Cancel/Discard verified");
    }
}
