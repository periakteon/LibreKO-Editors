using Godot;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Sources;

namespace LibreKO.Editors.Desktop;

public static class SourceChecks
{
    public static async Task Run(EditorShell shell, string mode)
    {
        var marker = Marker(shell);
        var session = shell.Session;
        async Task Frame() => await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
        async Task Wait(Func<bool> done)
        {
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (!done()) { if (DateTime.UtcNow > deadline) throw new Exception("Source UI operation timed out"); await Frame(); }
        }
        if (mode == "source-profiles")
        {
            var oldPath = Path.Combine(marker["fixtureRoot"]!.GetValue<string>(), "old-profile.json");
            var old = ProfileStore.Load(oldPath)!;
            if (old.Sources != null || old.ActiveSourceId != null) throw new Exception("Old profile changed meaning");
            var previousPath = System.Environment.GetEnvironmentVariable("LIBREKO_EDITOR_PROFILE");
            EditorSession oldSession;
            try { System.Environment.SetEnvironmentVariable("LIBREKO_EDITOR_PROFILE", oldPath); oldSession = new(shell); }
            finally { System.Environment.SetEnvironmentVariable("LIBREKO_EDITOR_PROFILE", previousPath); }
            using (oldSession)
            {
                await oldSession.InitializeAsync();
                if (oldSession.Connected || oldSession.Sources.Profile != null) throw new Exception("Old-profile startup unexpectedly configured authoring");
                await oldSession.Sources.SelectAsync(session.Profile.Sources![1]);
                var offline = await oldSession.Client.SendAsync<NamedReference[]>("references.named.resolve", new NamedReferenceResolve([new("npcs", 880009, IsMonster: true)], session.Profile.Sources[1].Id));
                if (offline.Single().Label != "Source fixture two" || oldSession.Connected) throw new Exception("Offline source configuration requires a database");
                await oldSession.Sources.SelectAsync(null);
                var cleared = await oldSession.Client.SendAsync<NamedReference[]>("references.named.resolve", new NamedReferenceResolve([new("npcs", 880009)], session.Profile.Sources[1].Id));
                if (cleared.Single().State != "unavailable") throw new Exception("Disabled sources retained old names");
                oldSession.Dispose();
            }
            var connected = session.Connected;
            session.Connected = false;
            SourceSettingsView.Show(session); await Frame();
            var dialog = shell.GetChildren().OfType<ConfirmationDialog>().Single(d => d.Title == "Source profiles");
            if (!dialog.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().Any(x => x.Text == session.Sources.Profile!.Roots[SourceRoot.ServerData]))
                throw new Exception("Source roots cannot be reviewed offline");
            dialog.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Review roots").EmitSignal(BaseButton.SignalName.Pressed);
            var maps = session.Sources.Profile!.Roots[SourceRoot.ServerMaps];
            var reviewTag = Guid.NewGuid().ToString("N");
            var originalMaps = maps + "-review-original-" + reviewTag; var replacementMaps = maps + "-review-replacement-" + reviewTag;
            Directory.Move(maps, originalMaps); Directory.CreateDirectory(maps);
            try
            {
                dialog.EmitSignal(AcceptDialog.SignalName.Confirmed); await Frame();
                if (!GodotObject.IsInstanceValid(dialog) || !dialog.FindChildren("*", "TextEdit", true, false).OfType<TextEdit>()
                    .Any(t => t.Text.Contains("changed after review", StringComparison.OrdinalIgnoreCase)))
                    throw new Exception("Source selection accepted a replaced reviewed root");
            }
            finally { Directory.Move(maps, replacementMaps); Directory.Move(originalMaps, maps); }
            dialog.Hide(); dialog.QueueFree(); await Frame(); session.Connected = connected;
            if (!connected) throw new Exception("Use the isolated DB fixture to check source cancellation");
            bool backgroundCompleted = false;
            var canceled = session.RunAsync(OperationKind.Source, async ct =>
            {
                try { await Task.Delay(Timeout.Infinite, ct); }
                catch (OperationCanceledException)
                {
                    await session.Client.SendAsync<JsonElement>("ping", new { });
                    backgroundCompleted = true;
                }
                ct.ThrowIfCancellationRequested();
            });
            session.Cancel(); await canceled;
            if (!session.Connected) throw new Exception("Pure source cancellation closed the database");
            if (!backgroundCompleted || session.Client.IsClosed) throw new Exception("File cancellation interrupted an independent lookup");
            var first = session.Profile.Sources![0]; var second = session.Profile.Sources![1];
            await session.Sources.SelectAsync(first);
            var slow = new DelayedSourceClient(session.Client);
            var cache = new ReferenceCache(() => slow, () => session.References.Scope);
            var presenter = new ReferencePresenter(cache);
            var label = Ui.Label(shell, "", 12);
            string shown = "";
            var key = new ReferenceIdentity("npcs", 880009, IsMonster: true);
            presenter.Bind(label, () => key, () => true, named => shown = named.Label, first.Id);
            await Wait(() => slow.Captured);
            await session.Sources.SelectAsync(second);
            presenter.Bind(label, () => key, () => true, named => shown = named.Label, second.Id);
            await Wait(() => shown == "Source fixture two"); slow.Release(); await Frame();
            if (shown != "Source fixture two") throw new Exception("Old source names repainted the new profile");
            label.QueueFree(); await session.Sources.SelectAsync(first);
            return;
        }
        var profile = session.Sources.Profile!;
        var store = session.Sources.Store!;
        SourceFileKey Key(string name) => new(profile.Id, SourceRoot.ServerData, SourceIdentity.Root(profile.Roots[SourceRoot.ServerData]), name);
        session.Sources.Validators!.Register("native-fixture-json/1", key => key.Root == SourceRoot.ServerData &&
            key.RelativePath is "first.json" or "second.json" or "unrelated.json", ticket =>
            { JsonNode.Parse(store.Blobs.Read(ticket.Candidate)); return []; });
        string[] DatabaseFiles() => Directory.Exists(session.HistoryRoot) ? Directory.GetFiles(session.HistoryRoot, "*", SearchOption.AllDirectories).Order().ToArray() : [];
        var databaseFiles = DatabaseFiles();
        var change = new FileChange(Guid.NewGuid(), [store.Stage(Key("first.json"), "{\"first\":1}"u8.ToArray()), store.Stage(Key("second.json"), "{\"second\":2}"u8.ToArray())]);
        var result = await new FileApply(profile, session.Profile.WorkspaceRoot, session.Sources.Validators,
            (step, _) => { if (step == "after-replace") throw new IOException("Native fixture interruption"); }).ApplyAsync(change);
        if (result.State != FileApplyState.Partial) throw new Exception("Partial operation was not preserved");
        var pendingTargets = session.Sources.History!.List().Where(x => x.Pending).SelectMany(x => x.Targets)
            .Select(x => x.Key.Deserialize<SourceFileKey>(JsonConfig.Options)!).ToArray();
        var unrelatedKey = Key("unrelated.json");
        if (pendingTargets.Contains(unrelatedKey)) throw new Exception("Unrelated target blocked");
        var unrelated = await session.Sources.ApplyAsync(new(Guid.NewGuid(), [store.Stage(unrelatedKey, "{\"ok\":true}"u8.ToArray())]));
        if (unrelated.State != FileApplyState.Applied) throw new Exception("Pending group blocked another target");
        FileHistoryView.Show(session); await Frame();
        var history = shell.GetChildren().OfType<AcceptDialog>().Single(d => d.Title == "Source file history");
        var list = history.FindChildren("*", "ItemList", true, false).OfType<ItemList>().Single();
        var pending = Enumerable.Range(0, list.ItemCount).Single(i => list.GetItemText(i).Contains(change.OperationId.ToString()));
        list.Select(pending); list.EmitSignal(ItemList.SignalName.ItemSelected, pending);
        history.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Reconcile").EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(() => session.Operations.CanMutate);
        history.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Prepare reversal").EmitSignal(BaseButton.SignalName.Pressed);
        await Wait(() => shell.GetChildren().OfType<ConfirmationDialog>().Any(d => d.Title == "Review file changes"));
        var review = shell.GetChildren().OfType<ConfirmationDialog>().Single(d => d.Title == "Review file changes");
        await Wait(() => !review.GetOkButton().Disabled);
        review.EmitSignal(AcceptDialog.SignalName.Confirmed);
        await Wait(() => session.Operations.CanMutate && !session.Sources.History.List().Any(x => x.Pending));
        if (File.ReadAllText(SourcePaths.Resolve(profile, Key("first.json"), false)) != "{}" || File.ReadAllText(SourcePaths.Resolve(profile, Key("second.json"), false)) != "{}")
            throw new Exception("File reversal did not restore exact originals");
        if (!databaseFiles.SequenceEqual(DatabaseFiles()))
            throw new Exception("File operations changed database history");
        history.Hide(); history.QueueFree();
    }

    internal static JsonObject Marker(EditorShell shell)
    {
        var path = System.Environment.GetEnvironmentVariable("LIBREKO_EDITOR_SOURCE_FIXTURE") ?? throw new Exception("Source checks require an explicit fixture marker");
        var marker = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var root = marker["fixtureRoot"]!.GetValue<string>();
        var id = marker["id"]!.GetValue<string>();
        if (!Guid.TryParseExact(id, "N", out _) || Path.GetFileName(root) != id ||
            SourcePaths.Canonical(root) != SourcePaths.Canonical(Path.Combine(shell.Session.Root, ".superpowers/sdd/2026-10-23-map-npc-01-foundations/native-fixtures", id)) ||
            SourcePaths.Canonical(path) != SourcePaths.Canonical(Path.Combine(root, "fixture-marker.json")) ||
            SourcePaths.Canonical(shell.Session.ProfilePath) != SourcePaths.Canonical(Path.Combine(root, "profile.json")) ||
            SourcePaths.Canonical(shell.Session.ProfilePath) != SourcePaths.Canonical(marker["profilePath"]!.GetValue<string>()) ||
            SourcePaths.Canonical(shell.Session.Profile.WorkspaceRoot) != SourcePaths.Canonical(Path.Combine(root, "editor-workspace")) ||
            shell.Session.Profile.Sources?.Count < 2 || shell.Session.Sources.Profile?.Id != marker["sourceProfileId"]!.GetValue<string>())
            throw new Exception("Source fixture marker/profile scope mismatch");
        SourcePaths.AssertNoLinks(root);
        foreach (var profile in shell.Session.Profile.Sources ?? [])
            foreach (var selected in profile.Roots.Values)
                if (!SourcePaths.Canonical(selected).StartsWith(SourcePaths.Canonical(root) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new Exception("Fixture root escapes the marked directory");
        return marker;
    }

    sealed class DelayedSourceClient(IEditorDataClient client) : IEditorDataClient
    {
        readonly TaskCompletionSource release = new();
        bool held;
        internal bool Captured;
        public async Task<T> SendAsync<T>(string method, object payload, CancellationToken cancellationToken = default)
        {
            var value = await client.SendAsync<T>(method, payload, cancellationToken);
            if (!held) { held = true; Captured = true; await release.Task; }
            return value;
        }
        internal void Release() => release.TrySetResult();
    }
}
