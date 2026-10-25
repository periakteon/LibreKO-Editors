using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Events;

namespace LibreKO.Editors.Desktop;
public static class EventsChecks
{
    public static async Task Run(EditorShell shell, string mode)
    {
        if (mode == "events-gallery")
        {
            await EventGalleries.Run(shell, false);
            return;
        }

        if (mode is "events-cancel-recover" or "events-helper-exit")
        {
            await CancelRecover(shell, mode == "events-helper-exit");
            return;
        }

        await shell.RequestWorkspaceAsync("events");
        var app = (EventsWorkspace)shell.Active!;
        if (!shell.Session.Connected)
            throw new Exception("Events checks require the isolated database.");
        if (mode == "events-world")
        {
            await World(app);
            return;
        }

        await app.SelectFamilyAsync(EventFamily.CollectionRace);
        if (mode == "events-gallery")
            return;
        var id = await shell.Session.Client.SendAsync<int>("events.next-id", new { table = EventTable.CollectionRaces });
        await app.CreateAsync(id);
        var root = app.Draft!.Rows[0];
        var zoneInput = app.FindChild("Field_ZoneId", true, false) as LineEdit ?? throw new Exception("Race zone field missing");
        zoneInput.Text = "210"; zoneInput.EmitSignal(LineEdit.SignalName.TextSubmitted, zoneInput.Text);
        var name = app.FindChild("Field_Name", true, false) as LineEdit ?? throw new Exception("Named event form missing");
        name.Text = "Native event fixture";
        name.EmitSignal(LineEdit.SignalName.TextSubmitted, name.Text);
        await app.AddRowAsync(EventTable.CollectionRaceSchedules);
        var schedule = app.Draft.Rows.Single(r => r.Key.Table == EventTable.CollectionRaceSchedules);
        app.Draft.Set(schedule.Key, "Hour", 9);
        if (mode == "events-calendar")
        {
            var tabs = app.FindChildren("*", "TabContainer", true, false).OfType<TabContainer>().Single();
            tabs.CurrentTab = 1;
            var repeat = (OptionButton)app.FindChild("Field_Repeat", true, false);
            repeat.Select(2); repeat.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
            var date = (LineEdit)app.FindChild("Field_Date", true, false);
            if (date.PlaceholderText != "yyyy-MM-dd") throw new Exception("Date format hint missing.");
            date.Text = "2026-02-30"; date.EmitSignal(LineEdit.SignalName.TextSubmitted, date.Text);
            if (app.InvalidFields.Count == 0) throw new Exception("Invalid calendar date accepted.");
            date.Text = "2026-10-25"; date.EmitSignal(LineEdit.SignalName.TextSubmitted, date.Text);
            if (app.InvalidFields.Count != 0) throw new Exception("Valid calendar date rejected.");
            await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
            var overview = app.FindChildren("*", "", true, false).OfType<EventScheduleView>().Single();
            var labels = overview.FindChildren("*", "Label", true, false).OfType<Label>().Select(l => l.Text).ToArray();
            if (!labels.Any(t => t.Contains("Once · 2026-10-25")) || labels.Contains("09:00")) throw new Exception("One-time rule is missing or repeated across the week.");
            var rowLabels = app.FindChildren("*", "ItemList", true, false).OfType<ItemList>().SelectMany(l => Enumerable.Range(0, l.ItemCount).Select(l.GetItemText));
            if (!rowLabels.Any(t => t.StartsWith(schedule.Key.Id + " · Once · 2026-10-25"))) throw new Exception("Schedule selector mislabels a one-time rule.");
            if (DisplayServer.GetName() != "headless")
                await EventGalleries.Capture(shell, "events-calendar", new(1280, 800));
        }
        if (mode == "events-drafts")
        {
            app.Undo(false);
            if (app.Draft.Rows.Single(r => r.Key == schedule.Key).Values["Hour"]!.ToString() == "9")
                throw new Exception("Undo did not restore the schedule");
            app.Undo(true);
            app.Draft.Set(schedule.Key, "Hour", 25);
            if (await app.SaveAsync())
                throw new Exception("Invalid event saved");
            var nav = shell.RequestWorkspaceAsync(null);
            var dialog = shell.GetChildren().OfType<ConfirmationDialog>().Single();
            dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
            await nav;
            if (shell.Active != app || !app.HasUnsavedChanges)
                throw new Exception("Cancel lost the draft");
            app.Draft.Set(schedule.Key, "Hour", 9);
        }

        if (!await app.SaveAsync())
            throw new Exception("Native event save failed");
        await app.SelectAsync(new(EventFamily.CollectionRace, id));
        if (app.Draft!.Rows.Count != 2)
            throw new Exception("Child schedule did not persist");
        if (mode == "events-calendar" && app.Draft.Rows.Single(r => r.Key == schedule.Key).Values["Date"]?.ToString() != "2026-10-25")
            throw new Exception("Native date entry did not survive save/reload.");
        if (mode == "events-drafts")
        {
            var definition = app.Draft.Rows.Single(r => r.Key.Table == EventTable.CollectionRaces);
            app.Draft.Set(definition.Key, "Name", "Saved navigation fixture");
            var navigation = shell.RequestWorkspaceAsync("content");
            var guard = shell.GetChildren().OfType<ConfirmationDialog>().Single();
            guard.EmitSignal(AcceptDialog.SignalName.Confirmed);
            await navigation;
            if (shell.Active?.Id != "content")
                throw new Exception("Save did not allow workspace switching");
            await shell.RequestWorkspaceAsync("events");
            app.Draft!.Set(definition.Key, "Name", "Discard navigation fixture");
            navigation = shell.RequestWorkspaceAsync(null);
            guard = shell.GetChildren().OfType<ConfirmationDialog>().Single();
            guard.EmitSignal(ConfirmationDialog.SignalName.CustomAction, "discard");
            await navigation;
            if (shell.Active != null)
                throw new Exception("Discard did not open Home");
            await shell.RequestWorkspaceAsync("events");
            if (app.Draft!.Rows.Single(r => r.Key == definition.Key).Values["Name"]!.ToString() != "Saved navigation fixture")
                throw new Exception("Discard did not restore saved data");
        }

        if (mode == "events-child-duplicate")
        {
            var tabs = app.FindChildren("*", "TabContainer", true, false).OfType<TabContainer>().Single();
            tabs.CurrentTab = 1;
            var duplicate = app.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == "Duplicate row") ?? throw new Exception("Child duplication action is missing");
            duplicate.EmitSignal(BaseButton.SignalName.Pressed);
            while (!shell.Session.Operations.CanMutate)
                await shell.ToSignal(shell.GetTree().CreateTimer(.02), SceneTreeTimer.SignalName.Timeout);
            if (app.Draft!.Rows.Count != 3)
                throw new Exception("Child row was not duplicated");
            app.Undo(false);
            if (app.Draft!.Rows.Count != 2)
                throw new Exception("Child duplication was not one undo step");
        }

        if (mode == "events-focused-save")
        {
            var rootRow = app.Draft!.Rows.Single(r => r.Key.Table == EventTable.CollectionRaces);
            app.Draft.Set(rootRow.Key, "Name", "Focused save fixture");
            var tabs = app.FindChildren("*", "TabContainer", true, false).OfType<TabContainer>().Single();
            tabs.CurrentTab = 1;
            await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
            var hour = app.FindChild("Field_Hour", true, false) as LineEdit ?? throw new Exception("Hour field missing");
            hour.GrabFocus();
            hour.SelectAll();
            shell.GetViewport().PushInput(new InputEventKey { Pressed = true, Keycode = Key.Backspace });
            foreach (var character in "12")
                shell.GetViewport().PushInput(new InputEventKey { Pressed = true, Unicode = character });
            if (hour.Text != "12")
                throw new Exception("Focused typing setup failed: " + hour.Text);
            shell._Input(new InputEventKey { Pressed = true, CtrlPressed = true, Keycode = Key.S });
            while (!shell.Session.Operations.CanMutate)
                await shell.ToSignal(shell.GetTree().CreateTimer(.02), SceneTreeTimer.SignalName.Timeout);
            if (app.Draft!.Rows.Single(r => r.Key.Table == EventTable.CollectionRaceSchedules).Values["Hour"]!.ToString() != "12")
                throw new Exception("Ctrl+S lost focused input");
        }

        if (mode == "events-history")
        {
            app.ShowHistory();
            var historyDeadline = DateTime.UtcNow.AddSeconds(10);
            while (!shell.Session.Operations.CanMutate && DateTime.UtcNow < historyDeadline)
                await shell.ToSignal(shell.GetTree().CreateTimer(.02), SceneTreeTimer.SignalName.Timeout);
            if (!shell.FindChildren("EventHistory*", "", true, false).Any())
                throw new Exception("History view missing");
            foreach (var d in shell.GetChildren().OfType<AcceptDialog>())
                d.QueueFree();
            var history = await shell.Session.Client.SendAsync<EventHistorySummary[]>("events.history", new { });
            var saved = history.First(h => h.Document == app.Draft.Original.Key && !h.Pending);
            var reversed = await shell.Session.Client.SendAsync<EventSaveResult>("events.replay", new { id = saved.OperationId, reverse = true });
            if (reversed.Status != SaveStatus.Saved)
                throw new Exception("History reverse failed: " + reversed.Message);
            app.Discard();
            await app.SelectFamilyAsync(EventFamily.Temple);
            return;
        }

        if (mode == "events-no-assets")
        {
            await app.AddRowAsync(EventTable.CollectionRaceRewards);
            if (!await app.SaveAsync())
                throw new Exception("Reward without icon failed to save");
        }

        var original = app.Draft.Original;
        app.Draft = new(original);
        foreach (var row in app.Draft.Rows)
            app.Draft.Remove(row.Key);
        if (!await app.SaveAsync())
            throw new Exception("Native event cleanup save failed");
        await app.SelectFamilyAsync(EventFamily.Temple);
        if (app.Documents.Count < 5)
            throw new Exception("Temple modes missing");
        await app.SelectFamilyAsync(EventFamily.Lottery);
    }

    static async Task World(EventsWorkspace app)
    {
        await app.SelectFamilyAsync(EventFamily.MapEvent);
        await app.SelectAsync(new(EventFamily.MapEvent, 31001, 210));
        var other = await app.Session.Client.SendAsync<EventDocumentSnapshot>("events.get", new EventDocumentKey(EventFamily.MapEvent, 31001, 211));
        var row = app.Draft!.Rows[0];
        var old = row.Values["Exec4"]!.DeepClone();
        app.Draft.Set(row.Key, "Exec4", Core.FieldValueParser.ReadInt32(old) == 111 ? 112 : 111);
        if (!await app.SaveAsync())
            throw new Exception("Compound event save failed");
        if ((await app.Session.Client.SendAsync<EventDocumentSnapshot>("events.get", other.Key)).Version != other.Version)
            throw new Exception("A different zone event changed");
        app.Draft!.Set(row.Key, "Exec4", old);
        if (!await app.SaveAsync())
            throw new Exception("Compound event restore failed");
        await app.SelectFamilyAsync(EventFamily.MapObject);
        if (app.Draft == null)
            throw new Exception("Map objects missing");
        var coordinate = app.FindChild("Field_X", true, false) as LineEdit ?? throw new Exception("Coordinate field missing");
        coordinate.Text = coordinate.Text == "1.25" ? "2.5" : "1.25";
        coordinate.EmitSignal(LineEdit.SignalName.TextSubmitted, coordinate.Text);
        if (!await app.SaveAsync())
            throw new Exception("Map coordinates failed to save");
        await app.SelectFamilyAsync(EventFamily.NpcTrigger);
        if (!app.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text == "Trap number"))
            throw new Exception("Trap match was mislabeled as NPC prototype");
        var trap = app.FindChild("Field_NpcId", true, false) as LineEdit ?? throw new Exception("Trap field missing");
        trap.Text = trap.Text == "43" ? "44" : "43";
        trap.EmitSignal(LineEdit.SignalName.TextSubmitted, trap.Text);
        if (!await app.SaveAsync())
            throw new Exception("Trap update failed");
        await app.SelectFamilyAsync(EventFamily.CastleSiege);
        await app.SelectAsync(new(EventFamily.CastleSiege, 31001));
        var before = app.Draft!.Rows[0];
        if (app.FindChild("Field_MasterKnights", true, false) != null || app.FindChild("Field_MoradonTax", true, false) != null)
            throw new Exception("Runtime state became writable");
        var day = app.FindChild("Field_WarDay", true, false) as OptionButton ?? throw new Exception("Named siege day selector missing");
        var nextDay = day.Selected == 2 ? 3 : 2;
        day.Select(nextDay);
        day.EmitSignal(OptionButton.SignalName.ItemSelected, nextDay);
        if (!await app.SaveAsync())
            throw new Exception("Siege configuration failed to save");
        foreach (var field in new[]
        {
            "MasterKnights",
            "MoradonTax",
            "SiegeType",
            "ChallengeListStr",
            "RequestList1"
        }

        )
            if (!System.Text.Json.Nodes.JsonNode.DeepEquals(before.Values[field], app.Draft!.Rows[0].Values[field]))
                throw new Exception("Siege runtime field changed: " + field);
        if (app.FindChildren("*", "Button", true, false).OfType<Button>().Any(b => b.Text is "New" or "Duplicate" or "Remove event"))
            throw new Exception("Siege root mutation exposed");
    }

    static async Task CancelRecover(EditorShell shell, bool helperExit = false)
    {
        var session = shell.Session;
        var connection = session.ConnectionString;
        if (!connection.Contains("Database=libreko_editor_test_", StringComparison.OrdinalIgnoreCase) || !connection.Contains("33307"))
            throw new Exception("Cancellation check requires the isolated native fixture.");
        var root = session.HistoryRoot;
        var pending = new EventJournal(root);
        if (pending.Pending().Count > 0)
            throw new Exception("Reconcile existing fixture operations first.");
        await shell.RequestWorkspaceAsync("events");
        var app = (EventsWorkspace)shell.Active!;
        await app.SelectFamilyAsync(EventFamily.CollectionRace);
        await app.CreateAsync(await session.Client.SendAsync<int>("events.next-id", new { table = EventTable.CollectionRaces }));
        app.Draft!.Set(app.Draft.Rows[0].Key, "Name", "Native interrupted event");
        app.Draft.Set(app.Draft.Rows[0].Key, "ZoneId", 210);
        var change = app.Draft.BuildChange(Guid.NewGuid());
        var operation = session.RunAsync(async () =>
        {
            await session.Client.SendAsync<EventSaveResult>("events.save", change);
        });
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (pending.Pending().Count == 0 && !operation.IsCompleted && DateTime.UtcNow < deadline)
            await shell.ToSignal(shell.GetTree().CreateTimer(.02), SceneTreeTimer.SignalName.Timeout);
        if (pending.Pending().Count == 0)
            throw new Exception("The native fixture must install its delayed-save trigger before this check.");
        if (helperExit)
        {
            var field = typeof(DataHostClient).GetField("process", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var process = (System.Diagnostics.Process)field.GetValue(session.Client)!;
            process.Kill(true);
            await process.WaitForExitAsync();
        }
        else
            session.Cancel();
        await operation;
        var stillConnected = session.Connected || !session.Client.IsClosed;
        await session.ConnectAsync(connection);
        var result = await session.Client.SendAsync<RecoveryDisposition>("events.reconcile", new { id = change.OperationId });
        if (result == RecoveryDisposition.Conflict || pending.Pending().Count != 0)
            throw new Exception("Interrupted save could not be reconciled");
        await app.RefreshAsync();
        if (stillConnected)
            throw new Exception("Interrupted helper remained connected");
    }
}
