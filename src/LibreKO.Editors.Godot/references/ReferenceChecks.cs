using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.References;

namespace LibreKO.Editors.Desktop;

public static class ReferenceChecks
{
    public static async Task Run(EditorShell shell)
    {
        if (!shell.Session.Connected || !shell.Session.ConnectionString.Contains("Database=libreko_editor_test_", StringComparison.OrdinalIgnoreCase))
            throw new Exception("Use the isolated fixture for named references.");
        var candidates = await shell.Session.References.ResolveAsync(new("npcs", 880009), null, default);
        if (candidates.Count != 2 || candidates.Any(x => x.Key.IsMonster == null)) throw new Exception("NPC type identity was lost");
        var item = (await shell.Session.References.ResolveAsync(new("items", 123450001), null, default)).Single();
        if (string.IsNullOrWhiteSpace(item.Label)) throw new Exception("Item name missing");
        var inventoryReview = await ReferencePresenter.DescribeInventoryAsync(shell.Session,
            [new(14, 123450001, 100, 1, 0, 0, 0)], [new(14, 0, 0, 0, 0, 0, 0)]);
        if (!inventoryReview.Contains("123450001 · " + item.Label) || !inventoryReview.Contains("0 · None")) throw new Exception("Inventory history reference names missing");
        var group = (await shell.Session.References.ResolveAsync(new("drop-groups", 8809, IsMonster: true), null, default)).Single();
        if (!group.Label.Contains("Named fixture monster") || group.Key.IsMonster != true) throw new Exception("Drop group users or type missing");
        var missing = (await shell.Session.References.ResolveAsync(new("magic", 2147483000), null, default)).Single();
        if (!ReferenceLabels.Display(missing).Contains("2147483000 · Missing reference")) throw new Exception("Missing reference lost its ID");

        var fake = new DelayedNames();
        var scope = "database-1/source-1";
        var cache = new ReferenceCache(() => fake, () => scope);
        var presenter = new ReferencePresenter(cache);
        var label = Ui.Label(shell, "", 12);
        ReferenceIdentity latestKey = new("npcs", 1, IsMonster: true), shownKey = latestKey;
        string shownName = "";
        void Bind() => presenter.Bind(label, () => latestKey, () => true, r => { shownKey = r.Key; shownName = r.Label; });
        async Task Wait(Func<bool> done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (!done())
            {
                if (DateTime.UtcNow > deadline) throw new Exception("Named-reference callback timed out");
                await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
        Bind(); await Wait(() => fake.Replies.Count == 1);
        latestKey = new("npcs", 2, IsMonster: false); Bind(); await Wait(() => fake.Replies.Count == 2);
        fake.Complete(1, latestKey, "New fixture"); await Wait(() => shownName == "New fixture");
        fake.Complete(0, new("npcs", 1, IsMonster: true), "Old fixture");
        await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (shownKey != latestKey || shownName != "New fixture") throw new Exception("Stale typed-reference reply");
        scope = "database-2/source-1"; Bind(); await Wait(() => fake.Replies.Count == 3);
        scope = "database-2/source-2"; Bind(); await Wait(() => fake.Replies.Count == 4);
        fake.Complete(3, latestKey, "New source"); await Wait(() => shownName == "New source");
        fake.Complete(2, latestKey, "Old source");
        await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
        if (shownName != "New source") throw new Exception("Scope switch accepted a stale name");
        var batched = cache.ResolveBatchAsync(Enumerable.Range(10, 102).Select(id => new ReferenceIdentity("items", id)).ToArray());
        await Wait(() => fake.Replies.Count == 5);
        if (fake.Requests[4].Keys.Count != 100) throw new Exception("Name lookups were not batched");
        fake.CompleteBatch(4); await Wait(() => fake.Replies.Count == 6);
        if (fake.Requests[5].Keys.Count != 2) throw new Exception("Batch lookup exceeded the frame key bound");
        fake.CompleteBatch(5);
        if ((await batched).Count != 102) throw new Exception("Name batch lost references");
        label.QueueFree();

        await shell.RequestWorkspaceAsync("content");
        var content = (EditorApp)shell.Active!;
        content.Module = 0; await content.SelectRecord(123450001); await content.PropertiesReady;
        var originalDamage = int.Parse(content.Draft!.Values["Damage"]!.ToString());
        content.Draft.Set("Damage", originalDamage + 123);
        var navigation = shell.OpenTargetAsync(new("content", "items", new() { ["id"] = 123450002 }, shell.Session.HistoryRoot));
        var dialog = shell.GetChildren().OfType<ConfirmationDialog>().Single();
        dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled); await navigation;
        if (content.SelectedId != 123450001 || !content.Draft.IsDirty) throw new Exception("Related navigation bypassed Cancel");
        await Wait(() => !GodotObject.IsInstanceValid(dialog));
        var discard = shell.OpenTargetAsync(new("content", "items", new() { ["id"] = 123450001 }, shell.Session.HistoryRoot));
        dialog = shell.GetChildren().OfType<ConfirmationDialog>().Single();
        dialog.EmitSignal(ConfirmationDialog.SignalName.CustomAction, "discard"); await discard;
        if (content.Draft!.IsDirty || content.Draft.Values["Damage"]!.ToString() != originalDamage.ToString()) throw new Exception("Related navigation bypassed Discard");
        await Wait(() => !GodotObject.IsInstanceValid(dialog));
        var damage = int.Parse(content.Draft.Values["Damage"]!.ToString()) + 1;
        content.Draft.Set("Damage", damage);
        var save = shell.OpenTargetAsync(new("content", "items", new() { ["id"] = 123450001 }, shell.Session.HistoryRoot));
        dialog = shell.GetChildren().OfType<ConfirmationDialog>().Single();
        dialog.EmitSignal(AcceptDialog.SignalName.Confirmed); await save;
        if (content.Draft!.IsDirty || content.Draft.Values["Damage"]!.ToString() != damage.ToString()) throw new Exception("Related navigation failed to Save");
        await shell.RequestWorkspaceAsync("events");
        var events = (EventsWorkspace)shell.Active!;
        await events.SelectFamilyAsync(EventFamily.Temple);
        await events.SelectAsync(new(EventFamily.Temple, 5));
        await Wait(() => events.FindChildren("*", "ItemList", true, false).OfType<ItemList>()
            .Any(list => Enumerable.Range(0, list.ItemCount).Any(i => list.GetItemText(i).Contains("880001 · Event test NPC"))));
        if (!events.FindChildren("*", "ItemList", true, false).OfType<ItemList>().Any(list => Enumerable.Range(0, list.ItemCount)
            .Any(i => list.GetItemTooltip(i).Contains("Database")))) throw new Exception("Child row reference provenance missing from tooltip");
    }

    sealed class DelayedNames : IEditorDataClient
    {
        internal readonly List<TaskCompletionSource<NamedReference[]>> Replies = [];
        internal readonly List<NamedReferenceResolve> Requests = [];
        public async Task<T> SendAsync<T>(string method, object payload, CancellationToken cancellationToken = default)
        {
            if (method != "references.named.resolve" || payload is not NamedReferenceResolve request) throw new Exception("Unexpected reference request");
            Requests.Add(request);
            var reply = new TaskCompletionSource<NamedReference[]>(); Replies.Add(reply);
            return (T)(object)await reply.Task;
        }
        internal void Complete(int index, ReferenceIdentity key, string name) => Replies[index].SetResult([new(key, name, "Fixture", "resolved")]);
        internal void CompleteBatch(int index) => Replies[index].SetResult(Requests[index].Keys.Select(k => new NamedReference(k, "Batch fixture", "Fixture", "resolved")).ToArray());
    }
}
