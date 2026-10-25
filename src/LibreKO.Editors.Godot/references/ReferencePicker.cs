using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.References;

namespace LibreKO.Editors.Desktop;

public static class ReferencePicker
{
    public static void Show(Node owner, EditorSession session, NamedReferenceQuery query, Action<NamedReference> choose,
        Func<bool> current, InventoryItemFilter? inventory = null)
    {
        var scope = session.References.Scope;
        var d = Ui.Add(owner, new ConfirmationDialog { Title = inventory == null ? "Find " + query.Kind : "Compatible items · " + InventoryLayout.Name(inventory.Slot), Exclusive = true,
            MinSize = new(720, 590), OkButtonText = inventory == null ? "Use selected" : "Add to slot", DialogHideOnOk = false });
        var panel = Ui.Column(d); panel.Position = new(14, 40); panel.CustomMinimumSize = new(680, 0);
        var search = Ui.Input(panel, "Search by name or ID", query.Text);
        var actions = Ui.Row(panel); var caption = Ui.Label(actions, "", 12, Ui.Muted);
        var list = Ui.Add(panel, new ItemList { CustomMinimumSize = new(680, 340), FixedIconSize = new(28, 28), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        var note = Ui.Label(panel, query.Kind == "npcs" && query.IsMonster == null
            ? "Stores a numeric prototype ID. Type labels distinguish candidates; the consuming field's rule determines the runtime match."
            : "Numeric ID and source are retained. Select a row to use it.", 11, Ui.Muted);
        note.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var error = Ui.Label(panel, "", 12, Ui.Accent); error.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        NamedReference[] entries = [];
        var page = query.Page; var serial = 0; var closed = false; var loading = false;
        bool Valid() => !closed && GodotObject.IsInstanceValid(d) && d.IsInsideTree() && !session.IsClosing && current() && session.References.Scope == scope;
        async Task Load()
        {
            if (!Valid()) return;
            var request = ++serial; loading = true; list.Clear(); entries = [];
            var text = search.Text; var requestedPage = page;
            try
            {
                Page<NamedReference> result;
                if (inventory != null)
                {
                    var compatible = await session.Client.SendAsync<Page<ReferenceEntry>>("references.list", new ReferenceQuery("items", text, requestedPage, Inventory: inventory));
                    result = new(compatible.Rows.Select(x => new NamedReference(new("items", x.Id), x.Label, "Database", "resolved",
                        new("content", "items", new() { ["id"] = x.Id }, session.HistoryRoot), x.Details)).ToArray(), compatible.Total, compatible.Number, compatible.Size);
                }
                else result = await session.Client.SendAsync<Page<NamedReference>>("references.named.list", query with { Text = text, Page = requestedPage });
                if (!Valid() || request != serial || search.Text != text || page != requestedPage) return;
                entries = result.Rows.ToArray();
                for (var i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];
                    list.AddItem(ReferenceLabels.Display(entry), query.Kind == "items" && session.Shell.Assets.Available ? LibreKO.ItemIcons.Get(entry.Key.Id) : null);
                    list.SetItemTooltip(i, ReferenceLabels.Display(entry) + "\n" + entry.Source + "\n" + entry.State);
                }
                caption.Text = $"{result.Total:N0} results · page {page + 1}";
                error.Text = entries.Length == 0 ? inventory == null ? "No matching records, or this lookup source is unavailable." : "No matching items fit this position and the other hand." : "";
            }
            catch (Exception e) { if (Valid() && request == serial) error.Text = e.Message; }
            finally { if (request == serial) loading = false; }
        }
        Ui.Button(actions, "Search", () => { page = 0; _ = Load(); });
        Ui.Button(actions, "Previous", () => { page = Math.Max(0, page - 1); _ = Load(); });
        Ui.Button(actions, "Next", () => { if (entries.Length == query.Size) { page++; _ = Load(); } });
        var open = Ui.Button(actions, "Open related", async () =>
        {
            var selected = list.GetSelectedItems();
            if (loading || !Valid() || selected.Length == 0 || !session.Shell.CanOpenTarget(entries[selected[0]].Target)) return;
            d.Hide();
            await session.Shell.OpenTargetAsync(entries[selected[0]].Target!);
            if (Valid()) d.PopupCentered(); else if (GodotObject.IsInstanceValid(d)) d.QueueFree();
        });
        open.Disabled = true;
        list.ItemSelected += i => open.Disabled = !session.Shell.CanOpenTarget(entries[(int)i].Target);
        void Select()
        {
            if (loading || !Valid()) return;
            var selected = list.GetSelectedItems(); if (selected.Length == 0) return;
            try { choose(entries[selected[0]]); closed = true; d.QueueFree(); }
            catch (Exception e) { error.Text = e.Message; }
        }
        d.Confirmed += Select; list.ItemActivated += _ => Select();
        d.Canceled += () => { closed = true; d.QueueFree(); };
        search.TextSubmitted += text => { page = 0; _ = Load(); };
        d.PopupCentered(); search.GrabFocus(); _ = Load();
    }

    public static void ShowCodes(Node owner, IReadOnlyDictionary<string, string> choices, Action<ReferenceEntry> choose, Func<bool> current)
    {
        var d = Ui.Add(owner, new ConfirmationDialog { Title = "Find code", Exclusive = true, MinSize = new(640, 480), OkButtonText = "Use selected" });
        var column = Ui.Column(d); column.Position = new(14, 40); column.CustomMinimumSize = new(600, 0);
        var search = Ui.Input(column, "Search by name or ID");
        var list = Ui.Add(column, new ItemList { CustomMinimumSize = new(600, 340) });
        ReferenceEntry[] rows = [];
        void Filter()
        {
            rows = choices.Where(x => x.Key.Contains(search.Text) || x.Value.Contains(search.Text, StringComparison.OrdinalIgnoreCase))
                .Select(x => new ReferenceEntry(int.Parse(x.Key), x.Value)).ToArray();
            list.Clear(); foreach (var row in rows) list.AddItem(row.Id + " · " + row.Label);
        }
        search.TextChanged += _ => Filter();
        void Select() { var selected = list.GetSelectedItems(); if (!current() || selected.Length == 0) return; choose(rows[selected[0]]); d.QueueFree(); }
        d.Confirmed += Select; list.ItemActivated += _ => Select(); d.Canceled += () => d.QueueFree();
        Filter(); d.PopupCentered();
    }
}
