using Godot;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Desktop;
public static class CompatibilityView
{
    public static async void Show(EditorSession session, string activeEditorId)
    {
        if (activeEditorId == "world") { ShowWorld(session); return; }
        if (activeEditorId == "content")
        {
            var workspace = (EditorApp)await session.Shell.EnsureWorkspaceAsync("content");
            workspace.ShowCompatibility();
            return;
        }

        if (activeEditorId != "events")
        {
            var chooser = Ui.Add(session.Shell, new AcceptDialog { Title = "Choose compatibility area", MinSize = new(540, 230) });
            var area = Ui.Column(chooser);
            area.Position = new(14, 38);
            Ui.Button(area, "Content · database, inventories and client assets", () =>
            {
                chooser.QueueFree();
                Show(session, "content");
            });
            Ui.Button(area, "Events · table and reference support", () =>
            {
                chooser.QueueFree();
                Show(session, "events");
            });
            Ui.Button(area, "Map & NPC · table and reference support", () => { chooser.QueueFree(); Show(session, "world"); });
            chooser.PopupCentered();
            return;
        }

        var generation = session.Generation;
        var d = Ui.Add(session.Shell, new AcceptDialog { Title = "Events database compatibility", MinSize = new(980, 650) });
        var panel = Ui.Column(d);
        panel.Position = new(14, 38);
        panel.CustomMinimumSize = new(940, 540);
        Ui.Heading(panel, "EVENT TABLE SUPPORT", 16);
        AddAreas(panel, session, "events", d);
        Ui.Label(panel, "Structural checks do not certify custom code, enum meanings or runtime behavior.", 12, Ui.Muted);
        var status = Ui.Label(panel, "", 12, Ui.Accent);
        var list = Ui.Add(panel, new ItemList { CustomMinimumSize = new(920, 280) });
        var detail = Ui.Add(panel, new RichTextLabel { CustomMinimumSize = new(920, 100), BbcodeEnabled = false });
        var rows = new List<EventTableSupport>();
        void Refresh()
        {
            if (!GodotObject.IsInstanceValid(d) || generation != session.Generation)
                return;
            status.Text = (session.EventCompatibility?.Database ?? "Disconnected") + (session.EventReportStale ? " · STALE: database changed" : " · schema checked");
            rows.Clear();
            list.Clear();
            foreach (var table in session.EventCompatibility?.Tables ?? [])
            {
                rows.Add(table);
                list.AddItem(table.Table + " · " + (table.CanSave ? "Read / write" : table.CanBrowse ? "Read only" : "Unavailable"));
            }

            detail.Text = "Select a table to inspect its support and lookup issues.";
        }

        list.ItemSelected += i =>
        {
            var row = rows[(int)i];
            detail.Text = row.Reason + "\n" + string.Join("\n", row.Issues.Take(12).Select(issue => issue.Code + " · " + issue.Target + " · " + issue.Message));
        };
        var actions = Ui.Row(panel);
        Ui.Button(actions, "Recheck schema", () => _ = session.RunAsync(async () =>
        {
            await session.RefreshEventCompatibilityAsync();
            Refresh();
        }));
        Ui.Button(actions, "Open selected family", async () =>
        {
            if (generation != session.Generation)
                return;
            var selection = list.GetSelectedItems();
            if (selection.Length == 0)
                return;
            var table = rows[selection[0]].Table;
            var catalog = await session.Client.SendAsync<EventCatalog>("events.catalog", new { });
            var family = catalog.Families.First(f => f.Tables.Contains(table)).Family;
            d.QueueFree();
            await session.Shell.RequestWorkspaceAsync("events");
            if (session.Shell.Active is EventsWorkspace events)
                await events.SelectFamilyAsync(family);
        });
        d.Confirmed += () => d.QueueFree();
        d.Canceled += () => d.QueueFree();
        Refresh();
        d.PopupCentered();
    }

    public static void AddAreas(Node parent, EditorSession session, string current, Window dialog)
    {
        var row = Ui.Row(parent);
        Ui.Label(row, "AREA", 10, Ui.Muted);
        foreach (var area in new[]
        {
            "content",
            "events", "world"
        }

        )
        {
            var selected = area;
            var button = Ui.Button(row, area == "content" ? "Content + client" : area == "world" ? "Map & NPC database" : "Events database", () =>
            {
                if (selected == current)
                    return;
                dialog.QueueFree();
                Show(session, selected);
            });
            Ui.Navigation(button, area == current);
        }
    }

    static void ShowWorld(EditorSession session)
    {
        var generation = session.Generation; var scope = session.HistoryRoot;
        var dialog = Ui.Add(session.Shell, new AcceptDialog { Title = "Map & NPC database compatibility", MinSize = new(980, 650) });
        var body = Ui.Column(dialog); body.Position = new(16, 40); body.CustomMinimumSize = new(940, 550);
        Ui.Heading(body, "WORLD TABLE SUPPORT", 17); AddAreas(body, session, "world", dialog);
        Ui.Label(body, "Each supported section remains independent. Mapped updates preserve custom columns; insert/remove guards apply to the actual operation.", 12, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var list = Ui.Add(body, new ItemList { CustomMinimumSize = new(930, 280) }); var details = Ui.Add(body, new TextEdit { Editable = false, CustomMinimumSize = new(930, 130), WrapMode = TextEdit.LineWrappingMode.Boundary });
        WorldTableSupport[] tables = [];
        bool Current() => GodotObject.IsInstanceValid(dialog) && generation == session.Generation && scope == session.HistoryRoot;
        void Paint()
        {
            if (!Current()) return; list.Clear(); tables = session.WorldCompatibility?.Tables.ToArray() ?? [];
            foreach (var table in tables) list.AddItem(WorldWorkspace.TableLabel(table.Table) + " · " + (table.CanSave ? "Mapped updates supported" : table.CanBrowse ? "Read only" : "Unavailable"));
            details.Text = session.Connected ? session.DatabaseName + (session.WorldReportStale ? " · STALE: rescan schema" : " · schema checked") : "Connect to check this database.";
        }
        list.ItemSelected += index => { var table = tables[(int)index]; details.Text = table.Reason + "\n\n" + string.Join("\n", table.Issues.Select(i => i.Severity + " · " + i.Target + " · " + i.Message)); };
        var actions=Ui.Add(body,new HFlowContainer());
        Ui.Button(actions, "Check schema", async () => { await session.RunAsync(async () => { await session.RefreshWorldCompatibilityAsync(); Paint(); }); });
        Ui.Button(actions, "Open selected section", async () => { if (list.GetSelectedItems().Length == 1 && Current()) { var table = tables[list.GetSelectedItems()[0]].Table; dialog.QueueFree(); await session.Shell.RequestWorkspaceAsync("world"); if (session.Shell.Active is WorldWorkspace world) await world.SelectTableAsync(table); } });
        Ui.Button(actions,"Source format support",()=>
        {
            var support=Ui.Add(session.Shell,new AcceptDialog{Title="Selected source format support",MinSize=new(780,440)});var panel=Ui.Column(support);panel.Position=new(18,42);panel.CustomMinimumSize=new(740,320);
            Ui.Heading(panel,"SERVER SOURCE FORMATS",15);Ui.Label(panel,"JSON · all 15 world counterparts, ItemSells, ItemExchangeExps, MiningExchanges, MiningFishingItems; NpcPositions.zone* shards follow the server loader. Unknown JSON fields are preserved.\n\nSMD · bounded lossless terrain, objects, event tiles, regene and warp sections. Collision relocation requires verified shape-to-face provenance; complete section imports are separate.\n\nQuests · UTF-8 / BOM-declared UTF-16, explicit manifest membership and staged include compilation. Scripts are never executed.\n\nSaving sources does not refresh running server caches or mounted client packs.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
            support.Confirmed+=()=>support.QueueFree();support.Canceled+=()=>support.QueueFree();support.PopupCentered();
        });
        dialog.Confirmed += () => dialog.QueueFree(); dialog.Canceled += () => dialog.QueueFree(); Paint(); dialog.PopupCentered();
    }
}
