using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;

namespace LibreKO.Editors.Desktop;
public sealed class HistoryRouter(EditorSession session)
{
    static bool Shared(EventFamily family) => family is EventFamily.MapEvent or EventFamily.MapObject or EventFamily.NpcTrigger;
    public async Task<IReadOnlyList<OperationLink>> ListAsync(string area)
    {
        if (area == "files") return session.Sources.History?.List() ?? [];
        if (!session.Connected) return [];
        var scope = session.HistoryRoot; var generation = session.Generation; var links = new List<OperationLink>();
        if (area is "world" or "events" or "all" or "world-related")
        {
            var world = await session.Client.SendAsync<OperationLink[]>("world.history", new { });
            links.AddRange(world.Where(x => area is "world" or "all" || x.Targets.Any(t => t.Kind is "GameEvents" or "MapObjects" or "EventTriggers")));
            if (area != "world-related")
            {
                var events = await session.Client.SendAsync<EventHistorySummary[]>("events.history", new { });
                links.AddRange(events.Where(x => area is "events" or "all" || Shared(x.Document.Family)).Select(x => new OperationLink("Events", x.OperationId, scope, x.Document.Family + " " + EventsWorkspace.KeyLabel(x.Document) + " · " + x.Label, x.Pending,
                    [new("events", x.Document.Family.ToString(), new() { ["id"] = x.Document.Id, ["zone"] = x.Document.Zone }, scope)])));
            }
        }
        if (area is "content" or "all")
        {
            OperationLink Content(RecordChange c, bool pending, string label) => new("Content", c.OperationId, scope, c.Before.Key.Kind + " " + c.Before.Key.Id + " · " + label, pending,
                [new("content", c.Before.Key.Kind switch { RecordKind.Item => "items", RecordKind.Character => "characters", _ => "accounts" }, new() { ["id"] = c.Before.Key.Id }, scope)]);
            var directory = Path.Combine(scope, "changes");
            if (Directory.Exists(directory)) foreach (var path in Directory.GetFiles(directory, "*.json"))
            {
                var saved = JsonNode.Parse(File.ReadAllText(path))!;
                links.Add(Content(saved["change"]!.Deserialize<RecordChange>(JsonConfig.Options)!, false, File.GetLastWriteTimeUtc(path).ToString("u")));
            }
            links.AddRange(new OperationJournal(scope).Pending().Select(c => Content(c, true, "Pending · reconcile outcome")));
        }
        if (scope != session.HistoryRoot || generation != session.Generation || !session.Connected) throw new InvalidOperationException("The selected database changed while loading history.");
        return links.OrderByDescending(x => x.Pending).ThenByDescending(x => x.Label, StringComparer.Ordinal).ToArray();
    }
    public async Task OpenAsync(OperationLink link)
    {
        var expected = link.Origin == "files" ? session.Sources.Profile?.Id : session.HistoryRoot;
        if (link.ScopeId != expected) throw new InvalidOperationException("This operation belongs to another database/source scope.");
        switch (link.Origin)
        {
            case "World": await WorldHistoryView.OpenAsync(session, link.OperationId); break;
            case "Events": await EventHistoryView.OpenAsync(session, link.OperationId); break;
            case "files": if (await session.Shell.CanLeaveAsync()) FileHistoryView.Show(session); break;
            case "Content":
                await session.Shell.RequestWorkspaceAsync("content");
                if (session.Shell.Active is EditorApp content && link.ScopeId == session.HistoryRoot) ((IEditorWorkspace)content).ShowHistory();
                break;
            default: throw new InvalidOperationException("Unsupported history origin.");
        }
    }
    public async Task ShowAsync(string area)
    {
        if (!await session.Shell.CanLeaveAsync()) return;
        await session.RunAsync(async () =>
        {
            var scope = session.HistoryRoot; var generation = session.Generation; var links = (await ListAsync(area)).ToArray();
            var dialog = Ui.Add(session.Shell, new AcceptDialog { Title = "Related database operations", MinSize = new(900, 500) });
            var body = Ui.Column(dialog); body.Position = new(16, 40); body.CustomMinimumSize = new(860, 410);
            Ui.Heading(body, "DATABASE HISTORY", 17); Ui.Label(body, "Each operation opens through its original editor and applies its complete reviewed batch.", 12, Ui.Muted);
            var list = Ui.Add(body, new ItemList { CustomMinimumSize = new(850, 300) });
            foreach (var link in links) list.AddItem(link.Origin + " · " + link.Label);
            Ui.Button(body, "Review selected operation", async () =>
            {
                if (scope != session.HistoryRoot || generation != session.Generation || list.GetSelectedItems().Length != 1) return;
                var link = links[list.GetSelectedItems()[0]]; dialog.QueueFree(); await OpenAsync(link);
            });
            dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree(); dialog.PopupCentered();
        });
    }
}
