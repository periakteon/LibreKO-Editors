using Godot;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Desktop;

public static class FileHistoryView
{
    public static void Show(EditorSession session, Action? closed = null)
    {
        var history = session.Sources.History;
        if (history == null) { session.Shell.SetStatus("Select a source profile to view its file history."); closed?.Invoke(); return; }
        var d = Ui.Add(session.Shell, new AcceptDialog { Title = "Source file history", MinSize = new(960, 690) });
        var panel = Ui.Column(d); panel.Position = new(16, 40); panel.CustomMinimumSize = new(920, 590);
        Ui.Heading(panel, "FILE HISTORY & RECOVERY", 17);
        Ui.Label(panel, session.Sources.Profile!.Label + " · independent of database history", 12, Ui.Muted);
        var list = Ui.Add(panel, new ItemList { CustomMinimumSize = new(920, 240), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        var detail = Ui.Add(panel, new TextEdit { Editable = false, CustomMinimumSize = new(920, 220), WrapMode = TextEdit.LineWrappingMode.Boundary });
        var actions = Ui.Row(panel); var status = Ui.Label(panel, "Select an operation to inspect exact paths and hashes.", 12, Ui.Muted);
        status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        OperationLink[] rows = []; Guid? selected = null;
        bool Current() => GodotObject.IsInstanceValid(d) && !session.IsClosing && ReferenceEquals(history, session.Sources.History);
        void Load()
        {
            if (!Current()) return;
            rows = history.List().ToArray(); list.Clear();
            foreach (var row in rows) { list.AddItem((row.Pending ? "Pending · " : "Recorded · ") + row.Label + " · " + row.OperationId); list.SetItemTooltip(list.ItemCount - 1, row.OperationId.ToString()); }
        }
        list.ItemSelected += index =>
        {
            if (!Current()) return;
            selected = rows[(int)index].OperationId;
            try { detail.Text = Describe(history.Read(selected.Value)); }
            catch (Exception e) { status.Text = e.Message; }
        };
        Ui.Button(actions, "Reconcile", async () =>
        {
            if (selected == null || !Current()) return;
            var id = selected.Value;
            await session.RunAsync(OperationKind.Source, async ct =>
            {
                var result = await Task.Run(() => history.Reconcile(id), ct);
                if (Current()) { status.Text = result.State + " · " + string.Join("; ", result.Targets.Select(x => x.Key.RelativePath + ": " + x.State)); Load(); }
            });
        });
        async Task Prepare(bool reverse)
        {
            if (selected == null || !Current()) return;
            var id = selected.Value; FileChange? change = null;
            var ok = await session.RunAsync(OperationKind.Source, async ct => change = await Task.Run(() => reverse ? history.PrepareReverse(id) : history.PrepareCompletion(id), ct));
            if (!ok || change == null || !Current()) return;
            d.Hide(); Review(session, change, () => { if (Current()) { Load(); d.PopupCentered(); } });
        }
        Ui.Button(actions, "Prepare reversal", () => { _ = Prepare(true); });
        Ui.Button(actions, "Prepare completion", () => { _ = Prepare(false); });
        Ui.Button(actions, "Refresh", Load);
        void Close() { d.Hide(); d.QueueFree(); closed?.Invoke(); }
        d.Canceled += Close; d.Confirmed += Close;
        try { Load(); } catch (Exception e) { status.Text = e.Message; }
        d.PopupCentered();
    }

    public static void Review(EditorSession session, FileChange change, Action? closed = null)
    {
        var profile = session.Sources.Profile;
        var d = Ui.Add(session.Shell, new ConfirmationDialog { Title = "Review file changes", MinSize = new(920, 600),
            OkButtonText = "Apply reviewed files", DialogHideOnOk = false });
        var panel = Ui.Column(d); panel.Position = new(16, 40); panel.CustomMinimumSize = new(880, 500);
        Ui.Heading(panel, "APPLY FILE CHANGES", 17);
        Ui.Label(panel, "Each file is replaced atomically. The group is recoverable and may have partial outcomes.", 12, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var preview = Ui.Add(panel, new TextEdit { Editable = false, Text = Describe(change), CustomMinimumSize = new(880, 340), WrapMode = TextEdit.LineWrappingMode.Boundary });
        var status = Ui.Label(panel, "Validating candidates…", 12, Ui.Accent); status.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        d.GetOkButton().Disabled = true;
        bool Current() => GodotObject.IsInstanceValid(d) && !session.IsClosing && ReferenceEquals(profile, session.Sources.Profile);
        void Close() { d.Hide(); d.QueueFree(); closed?.Invoke(); }
        d.Canceled += Close;
        d.Confirmed += async () =>
        {
            if (!Current() || d.GetOkButton().Disabled || !session.Operations.CanMutate) return;
            d.GetOkButton().Disabled = true;
            FileApplyResult? outcome = null;
            await session.RunAsync(OperationKind.Source, async ct => outcome = await session.Sources.ApplyAsync(change, ct));
            if (!Current() || outcome == null) return;
            status.Text = outcome.State + " · " + string.Join("; ", outcome.Targets.Select(x => x.Key.RelativePath + ": " + x.State)) +
                "\n" + string.Join("; ", outcome.Issues.Select(x => x.Message));
            if (outcome.State == FileApplyState.Applied) Close();
            else status.Text += "\nReview file history before retrying. The operation ID is " + outcome.OperationId;
        };
        async Task Validate()
        {
            var issues = new List<ValidationIssue>();
            var ok = await session.RunAsync(OperationKind.Source, async ct => await Task.Run(async () =>
            {
                foreach (var file in change.Files)
                {
                    ct.ThrowIfCancellationRequested();
                    if (file.After == null) session.Sources.Validators!.Codec(file.Before.Key);
                    else issues.AddRange(session.Sources.Validators!.Validate(new(file.Before.Key, file.After)));
                }
                if(issues.Count==0)issues.AddRange(await session.Sources.Validators!.ValidateBatchAsync(change,ct));
            }, ct));
            if (!Current()) return;
            status.Text = !ok ? "Validation interrupted or unavailable. Reopen this review to validate again." : issues.Count == 0 ? "Candidates validated. Review every destination before applying." : string.Join("\n", issues.Select(x => x.Field + ": " + x.Message));
            d.GetOkButton().Disabled = !ok || issues.Count != 0;
        }
        d.PopupCentered(); _ = Validate();
    }

    static string Describe(FileChange change) => $"Operation {change.OperationId}\n" + string.Join("\n\n", change.Files.Select(file =>
        $"{file.Before.Key.Root} / {file.Before.Key.RelativePath}\nProfile {file.Before.Key.ProfileId} · root identity {file.Before.Key.RootIdentity}\n" +
        $"Before: {(file.Before.Exists ? file.Before.Sha256 : "absent")}\nAfter: {(file.After == null ? "deleted" : file.After.Sha256 + " · " + file.After.Length + " bytes")}"));
}
