using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Events;

namespace LibreKO.Editors.Desktop;
public static class EventHistoryView
{
    public static async Task OpenAsync(EditorSession session, Guid? selectedId = null)
    {
        if (!session.Connected)
        {
            session.Shell.SetStatus("Connect to view this database's event history.");
            return;
        }

        if (!await session.Shell.CanLeaveAsync())
            return;
        await session.RunAsync(async () =>
        {
            var history = await session.Client.SendAsync<EventHistorySummary[]>("events.history", new { });
            Show(session, history, selectedId);
        });
    }

    static void Show(EditorSession session, EventHistorySummary[] history, Guid? selectedId)
    {
        var generation = session.Generation;
        var d = Ui.Add(session.Shell, new AcceptDialog { Name = "EventHistory", Title = "Event database history", MinSize = new(1000, 620) });
        var panel = Ui.Column(d);
        panel.Position = new(14, 38);
        panel.CustomMinimumSize = new(960, 520);
        Ui.Heading(panel, "EVENT OPERATIONS", 16);
        Ui.Label(panel, "History is scoped to the selected database. Review affected rows before replaying or reversing.", 12, Ui.Muted);
        var split = Ui.Add(panel, new HSplitContainer { CustomMinimumSize = new(950, 370), SplitOffsets = [400] });
        var list = Ui.Add(split, new ItemList { CustomMinimumSize = new(370, 360), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var preview = Ui.Add(split, new TextEdit { Editable = false, CustomMinimumSize = new(520, 360), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, WrapMode = TextEdit.LineWrappingMode.Boundary });
        foreach (var row in history)
            list.AddItem(row.Document.Family + " " + EventsWorkspace.KeyLabel(row.Document) + " · " + row.Label);
        EventHistorySummary? selected = null;
        bool valid() => GodotObject.IsInstanceValid(d) && generation == session.Generation;
        var actions = Ui.Row(panel);
        Ui.Button(actions, "Related World operations…", () => { d.QueueFree(); _ = new HistoryRouter(session).ShowAsync("world-related"); });
        var status = Ui.Label(panel, "Select an operation", 12, Ui.Muted);
        Button reconcile = null !, reverse = null !, replay = null !;
        async Task Run(string method, object payload)
        {
            if (selected == null || !valid())
                return;
            var ok = await session.RunAsync(async () =>
            {
                if (method == "events.reconcile")
                {
                    var result = await session.Client.SendAsync<RecoveryDisposition>(method, payload);
                    if (valid())
                        status.Text = "Reconciliation: " + result;
                }
                else
                {
                    var result = await session.Client.SendAsync<EventSaveResult>(method, payload);
                    if (valid())
                        status.Text = result.Message ?? result.Status.ToString();
                    if (result.Status == SaveStatus.Saved)
                    {
                        session.NotifyDatabaseChange();
                        if (session.Shell.Active != null)
                            await session.Shell.Active.RefreshAsync();
                    }

                    if (result.Status == SaveStatus.UnknownCommit)
                        session.Connected = false;
                }
            });
            if (ok && valid())
            {
                reconcile.Disabled = reverse.Disabled = replay.Disabled = true;
            }
        }

        reconcile = Ui.Button(actions, "Reconcile pending", () =>
        {
            if (selected != null)
                _ = Run("events.reconcile", new { id = selected.OperationId });
        });
        reverse = Ui.Button(actions, "Reverse operation", () =>
        {
            if (selected != null)
                _ = Run("events.replay", new { id = selected.OperationId, reverse = true });
        });
        replay = Ui.Button(actions, "Replay operation", () =>
        {
            if (selected != null)
                _ = Run("events.replay", new { id = selected.OperationId, reverse = false });
        });
        reconcile.Disabled = reverse.Disabled = replay.Disabled = true;
        list.ItemSelected += async index =>
        {
            selected = history[(int)index];
            var item = selected;
            reconcile.Disabled = !item.Pending;
            reverse.Disabled = replay.Disabled = item.Pending;
            preview.Text = item.Pending ? "Pending database outcome. Reconcile before retrying any save." : "Loading affected rows…";
            if (item.Pending)
                return;
            try
            {
                var entry = await session.Client.SendAsync<EventHistoryEntry>("events.history.get", new { id = item.OperationId });
                if (!valid() || selected != item)
                    return;
                preview.Text = string.Join("\n\n", entry.Change.Rows.Select(row => (row.Before == null ? "INSERT" : row.After == null ? "DELETE" : "UPDATE") + " " + row.Key.Table + " · " + row.Key.Id + (row.Key.Zone is { } z ? " · zone " + z : "") + "\n" + (row.Before != null && row.After != null ? string.Join("\n", EventRecovery.ChangedFields(row).Select(f => f + ": " + row.Before.Values[f] + " → " + row.After[f])) : (row.After ?? row.Before!.Values).ToJsonString())));
                var names = await EventRewardView.DescribeChangesAsync(session, entry.Change.Rows);
                if (valid() && selected == item) preview.Text += "\n\nCurrent database reference names:\n" + names;
            }
            catch (Exception e)
            {
                if (valid())
                    status.Text = e.Message;
            }
        };
        d.Canceled += () => d.QueueFree();
        d.Confirmed += () => d.QueueFree();
        d.PopupCentered();
        if (selectedId is Guid id && Array.FindIndex(history, x => x.OperationId == id) is var selectedIndex && selectedIndex >= 0)
        { list.Select(selectedIndex); list.EmitSignal(ItemList.SignalName.ItemSelected, selectedIndex); }
    }
}
