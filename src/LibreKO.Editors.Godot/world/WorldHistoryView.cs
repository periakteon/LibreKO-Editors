using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.World;

namespace LibreKO.Editors.Desktop;
public static class WorldHistoryView
{
    public static async Task OpenAsync(EditorSession session, Guid? selectedId = null)
    {
        if (!session.Connected) { session.Shell.SetStatus("Connect to view this database's world history."); return; }
        if (!await session.Shell.CanLeaveAsync()) return;
        await session.RunAsync(async () => Show(session, await session.Client.SendAsync<OperationLink[]>("world.history", new { }), selectedId));
    }
    static void Show(EditorSession session, OperationLink[] history, Guid? selectedId)
    {
        var generation = session.Generation; var scope = session.HistoryRoot;
        var dialog = Ui.Add(session.Shell, new AcceptDialog { Name = "WorldHistory", Title = "Map & NPC database history", MinSize = new(1000, 650) });
        var body = Ui.Column(dialog); body.Position = new(16, 40); body.CustomMinimumSize = new(960, 550);
        Ui.Heading(body, "WORLD DATABASE OPERATIONS", 17); Ui.Label(body, "Review the complete operation. Reversal preserves unrelated fields and checks current dependencies.", 12, Ui.Muted);
        var split = Ui.Add(body, new HSplitContainer { CustomMinimumSize = new(950, 380), SplitOffsets = [390] });
        var list = Ui.Add(split, new ItemList { CustomMinimumSize = new(360, 380), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var preview = Ui.Add(split, new TextEdit { Editable = false, CustomMinimumSize = new(540, 380), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, WrapMode = TextEdit.LineWrappingMode.Boundary });
        foreach (var link in history) list.AddItem(link.Label + " · " + link.OperationId);
        var actions = Ui.Row(body); var status = Ui.Label(body, "Select an operation", 12, Ui.Muted); status.Name = "WorldHistoryStatus"; status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        OperationLink? selected = null; bool loaded = false;
        bool Current() => GodotObject.IsInstanceValid(dialog) && !session.IsClosing && generation == session.Generation && scope == session.HistoryRoot;
        Button reconcile = null!, reverse = null!, replay = null!;
        async Task Run(bool? reversing)
        {
            var item = selected;
            if (!Current() || item == null || item.ScopeId != scope || reversing != null && !loaded) return;
            var ok = await session.RunAsync(async () =>
            {
                if (!Current()) return;
                if (reversing == null)
                {
                    var disposition = await session.Client.SendAsync<RecoveryDisposition>("world.reconcile", new { id = item.OperationId });
                    if (Current()) status.Text = "Reconciliation: " + disposition;
                    if (disposition != RecoveryDisposition.Conflict) session.NotifyDatabaseChange();
                }
                else
                {
                    var result = await session.Client.SendAsync<WorldSaveResult>("world.replay", new { id = item.OperationId, reverse = reversing.Value });
                    if (Current()) status.Text = result.Message ?? result.Status.ToString();
                    if (result.Status == SaveStatus.Saved) session.NotifyDatabaseChange();
                    if (result.Status == SaveStatus.UnknownCommit) session.Connected = false;
                }
                if (session.Connected && session.Shell.Active != null) await session.Shell.Active.RefreshAsync();
            });
            if (ok && Current()) reconcile.Disabled = reverse.Disabled = replay.Disabled = true;
        }
        reconcile = Ui.Button(actions, "Reconcile pending", () => _ = Run(null));
        reverse = Ui.Button(actions, "Reverse operation", () => _ = Run(true));
        replay = Ui.Button(actions, "Replay operation", () => _ = Run(false));
        Ui.Button(actions, "Related Events history…", () => { dialog.QueueFree(); _ = new HistoryRouter(session).ShowAsync("world"); });
        reconcile.Disabled = reverse.Disabled = replay.Disabled = true;
        list.ItemSelected += async index =>
        {
            var item = history[(int)index]; selected = item; loaded = false;
            reconcile.Disabled = !item.Pending; reverse.Disabled = replay.Disabled = true;
            preview.Text = item.Pending ? "Pending world outcome. Reconcile before retrying a save." : "Loading the complete world batch…";
            if (item.Pending) return;
            try
            {
                var entry = await session.Client.SendAsync<WorldHistoryEntry>("world.history.get", new { id = item.OperationId });
                if (!Current() || selected != item || entry.ScopeId != scope) return;
                preview.Text = string.Join("\n\n", entry.Change.Rows.Select(r => (r.Before == null ? "INSERT" : r.After == null ? "DELETE" : "UPDATE") + " " + r.Key.Table + " · " + r.Key.Id + (r.Key.Zone is int zone ? " · zone " + zone : "") + (r.Key.IsMonster is bool monster ? monster ? " · Monster" : " · NPC" : "") + "\n" +
                    (r.Before != null && r.After != null ? string.Join("\n", WorldRecovery.ChangedFields(r).Select(f => f + ": " + r.Before.Values[f] + " → " + r.After[f])) : (r.After ?? r.Before!.Values).ToJsonString())));
                loaded = true; reverse.Disabled = replay.Disabled = false; status.Text = entry.Change.Rows.Count + " row changes · originating writer: World";
                var referenceScope = session.References.Scope;
                var names = await WorldReferenceNames.DescribeAsync(session, entry.Change.Rows);
                if (Current() && selected == item && referenceScope == session.References.Scope) preview.Text += "\n\nCurrent reference names and sources:\n" + names;
            }
            catch (Exception e) { if (Current() && selected == item) status.Text = e.Message; }
        };
        dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree(); dialog.PopupCentered();
        if (selectedId is Guid id && Array.FindIndex(history, x => x.OperationId == id) is var selectedIndex && selectedIndex >= 0)
        { list.Select(selectedIndex); list.EmitSignal(ItemList.SignalName.ItemSelected, selectedIndex); }
    }
}
