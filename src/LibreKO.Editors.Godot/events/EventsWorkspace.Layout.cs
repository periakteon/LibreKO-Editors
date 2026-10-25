using Godot;
using LibreKO.Editors.Core.Events;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Desktop;
public partial class EventsWorkspace
{
    void BuildUi()
    {
        var layout = Ui.Column(this);
        layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var banner = Ui.Row(layout);
        Ui.Heading(banner, "EVENTS", 18);
        Ui.Label(banner, "Schedules, rewards and world configuration", 12, Ui.Muted);
        Ui.Add(banner, new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        Ui.Label(banner, "DATABASE WORKSPACE", 10, Ui.Accent);
        Ui.Add(banner,new ActivityIndicator(Session.Loading,"references"));
        var body = Ui.Add(layout, new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill });
        body.AddThemeConstantOverride("separation", 10);
        var navFrame = Ui.Add(body, new PanelContainer { CustomMinimumSize = new(166, 0) });
        var nav = Ui.Column(navFrame);
        Ui.Label(nav, "EVENT LIBRARY", 10, Ui.Accent);
        foreach (var(family, label)in new[]
        {
            (EventFamily.Temple, "Temple activities"),
            (EventFamily.CollectionRace, "Collection Race"),
            (EventFamily.Lottery, "Lottery"),
            (EventFamily.CastleSiege, "Castle Siege"),
            (EventFamily.MapEvent, "Map events"),
            (EventFamily.MapObject, "Map objects"),
            (EventFamily.NpcTrigger, "NPC triggers")
        }

        )
        {
            var selected = family;
            var button = Ui.Button(nav, label, async () =>
            {
                if (await Session.Shell.CanLeaveAsync())
                    await Session.RunAsync(() => SelectFamilyAsync(selected));
            });
            button.Alignment = HorizontalAlignment.Left;
            button.CustomMinimumSize = new(148, 38);
            Ui.Navigation(button, family == Family);
            familyButtons[family] = button;
        }

        Ui.Add(nav, new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        var boundary = Ui.Label(nav, "Bifrost, Krowaz and Bowl timings are configured in server files or code.", 11, Ui.Muted);
        boundary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        boundary.CustomMinimumSize = new(140, 0);
        var recordsFrame = Ui.Add(body, new PanelContainer { CustomMinimumSize = new(260, 0) });
        var records = Ui.Column(recordsFrame);
        Ui.Label(records, "FIND AN EVENT", 10, Ui.Accent);
        search = Ui.Input(records, "Search name or ID");
        Ui.Add(records,new ActivityIndicator(Session.Loading,"data"));
        search.TextSubmitted += text => _ = Session.RunAsync(async () =>
        {
            page = 0;
            await LoadListAsync();
        });
        list = Ui.Add(records, new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new(240, 0), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis, FixedIconSize = new(28, 28) });
        list.ItemSelected += async index =>
        {
            if (index < Documents.Count && await Session.Shell.CanLeaveAsync())
                await Session.RunAsync(() => SelectAsync(Documents[(int)index].Key));
        };
        var paging = Ui.Row(records);
        Ui.Button(paging, "‹", () => _ = Session.RunAsync(async () =>
        {
            page = Math.Max(0, page - 1);
            await LoadListAsync();
        }));
        pageCaption = Ui.Label(paging, "Connect to browse", 11, Ui.Muted);
        pageCaption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Ui.Button(paging, "›", () => _ = Session.RunAsync(async () =>
        {
            if (Documents.Count == 50)
            {
                page++;
                await LoadListAsync();
            }
        }));
        details = Ui.Column(body);
        details.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        notice = Ui.Label(layout, "Database saves update configuration. Server caches may require maintenance reload or restart; forced JSON reseeding can overwrite edits.", 11, Ui.Muted);
        notice.ClipText = true;
    }

    internal void RenderDocument()
    {
        PendingInputs.Clear();
        Ui.Clear(details);
        rowLists.Clear();
        scheduleView = null;
        if (Draft == null || Catalog == null)
        {
            var empty = Ui.Group(details, Family.ToString());
            Ui.Heading(empty, Session.Connected ? "Select or create an event" : "Connect to your database", 23);
            Ui.Label(empty, "Manage database-backed configuration with explicit saves and recoverable history.", 13, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (Session.Connected && Family is not (EventFamily.Temple or EventFamily.CastleSiege))
                Ui.Button(empty, "New definition", ShowCreateDialog);
            return;
        }

        var heading = Ui.Row(details);
        var title = Draft.Rows.FirstOrDefault(r => r.Values.ContainsKey("Name"))?.Values["Name"]?.ToString() ?? Documents.FirstOrDefault(d => d.Key == Draft.Original.Key)?.Label ?? Family.ToString();
        var documentTitle = Ui.Heading(heading, title, 23);
        documentTitle.ClipText = true;
        documentTitle.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        Ui.Label(heading, KeyLabel(Draft.Original.Key), 12, Ui.Accent);
        var actions = Ui.Row(details);
        Ui.Label(actions, Draft.IsDirty ? "UNSAVED DRAFT" : "DATABASE SNAPSHOT", 10, Ui.Accent).SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (Family is not (EventFamily.Temple or EventFamily.CastleSiege))
        {
            Ui.Button(actions, "New", ShowCreateDialog);
            Ui.Button(actions, "Duplicate", () => ShowCreateDialog(true));
            var remove = Ui.Button(actions, "Remove event", () => ConfirmRemoval(null));
            remove.Disabled = Draft.Original.Version != "missing" && Draft.Rows.Any(r => HasOpaqueColumns(r.Key.Table));
            if (remove.Disabled)
                remove.TooltipText = "Custom columns prevent exact deletion/history preservation; mapped edits remain available.";
        }

        if (Family == EventFamily.CastleSiege)
        {
            var siege = Ui.Add(details, new EventSiegeView());
            siege.Show(Draft, Session.Profile.ServerScheduleTimeZone);
        }
        else if (Family is EventFamily.MapEvent or EventFamily.MapObject or EventFamily.NpcTrigger)
        {
            var world = Ui.Add(details, new EventWorldViews());
            world.Show(Draft, Catalog);
        }

        var schedules = new EventScheduleView();
        scheduleView = schedules;
        Ui.Add(details, schedules);
        schedules.Show(CurrentSnapshot(), Session.Profile.ServerScheduleTimeZone);
        var rememberedTable = SelectedTable;
        var rememberedRow = SelectedRow;
        var tabs = Ui.Add(details, new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill });
        var tables = Catalog.Families.Single(f => f.Family == Family).Tables.Where(t => t != EventTable.ForgottenTempleWaves || Draft.Original.Key.Id == 5).ToArray();
        foreach (var table in tables)
        {
            var tab = Ui.Add(tabs, new VBoxContainer { Name = TabTitle(table) });
            var toolbar = Ui.Row(tab);
            Ui.Label(toolbar, TabTitle(table).ToUpperInvariant(), 10, Ui.Accent).SizeFlagsHorizontal = SizeFlags.ExpandFill;
            var root = table == tables[0] && Family != EventFamily.Temple;
            if (!root)
            {
                var selectedTable = table;
                Ui.Button(toolbar, "Add row", () => _ = Session.RunAsync(() => AddRowAsync(selectedTable)));
            }

            var split = Ui.Add(tab, new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = [0] });
            var rowSide = Ui.Column(split);
            rowSide.CustomMinimumSize = new(240, 0);
            rowSide.SizeFlagsHorizontal = SizeFlags.Fill;
            var rows = Ui.Add(rowSide, new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, FixedIconSize = new(34, 34), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
            rowLists[table] = rows;
            var values = Draft.Rows.Where(r => r.Key.Table == table).ToArray();
            foreach (var row in values)
                rows.AddItem(RowLabel(row), RewardIcon(row));
            var scroll = Ui.Add(split, new ScrollContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled });
            var form = Ui.Column(scroll);
            form.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            void ShowRow(EventRowSnapshot row)
            {
                Ui.Clear(form);
                InvalidFields.Clear();
                SelectedRow = row.Key;
                SelectedTable = table;
                var editor = Ui.Add(form, new EventRowEditor(this));
                editor.Configure(Draft!, Catalog.Tables.Single(t => t.Table == table), row.Key);
                if (!root)
                {
                    Ui.Button(form, "Duplicate row", () => _ = Session.RunAsync(() => DuplicateRowAsync(row.Key)));
                    var remove = Ui.Button(form, "Remove this row", () => ConfirmRemoval(row.Key));
                    remove.Disabled = row.Version != "missing" && HasOpaqueColumns(row.Key.Table);
                    if (remove.Disabled)
                        remove.TooltipText = "Custom columns cannot be preserved exactly by destructive history.";
                }
            }

            rows.ItemSelected += i => ShowRow(values[(int)i]);
            var selected = Array.FindIndex(values, v => v.Key == rememberedRow);
            if (selected < 0)
                selected = 0;
            if (values.Length > 0)
            {
                rows.Select(selected);
                ShowRow(values[selected]);
            }
            else
                Ui.Label(form, "No rows. Add a row to configure this event.", 12, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _ = EventRewardView.NameRowsAsync(this, rows, values);
        }

        SelectedTable = rememberedTable ?? tables[0];
        SelectedRow = rememberedRow;
        var active = Array.IndexOf(tables, SelectedTable.Value);
        tabs.CurrentTab = Math.Max(0, active);
        tabs.TabChanged += i =>
        {
            SelectedTable = tables[(int)i];
        };
        notice.Text = Draft.IsDirty ? "Unsaved changes · Save to database to apply" : "Database snapshot · server cache and reseed rules apply";
    }

    static string TabTitle(EventTable table) => table switch
    {
        EventTable.TempleEventSchedules or EventTable.CollectionRaceSchedules or EventTable.LotterySchedules => "Schedules",
        EventTable.TempleEventRewards => "Outcomes",
        EventTable.CollectionRaceRewards => "Rewards",
        EventTable.LotteryRewards => "Prizes",
        EventTable.CollectionRaceObjectives => "Objectives",
        EventTable.ForgottenTempleWaves => "Waves",
        _ => "Definition"
    };
    static string RowLabel(EventRowSnapshot row) => row.Key.Id + " · " + (row.Values.ContainsKey("Hour") ? EventScheduleProjection.DescribeSchedule(row) : row.Values.ContainsKey("ItemId") ? $"Item {row.Values["ItemId"]} × {row.Values["ItemCount"] ?? row.Values["Count"]}" : row.Values.ContainsKey("Name") ? row.Values["Name"]!.ToString() : row.Key.Table == EventTable.ForgottenTempleWaves ? $"Tier {row.Values["Tier"]} · wave {row.Values["Wave"]}" : row.Key.Table == EventTable.CollectionRaceObjectives ? $"Order {row.Values["Ordinal"]} · target {row.Values["TargetId"]}" : "Configuration");
    Texture2D? RewardIcon(EventRowSnapshot row)
    {
        if (!row.Values.ContainsKey("ItemId"))
            return null;
        Session.Shell.Assets.EnsureLoaded();
        return Session.Shell.Assets.Available ? LibreKO.ItemIcons.Get(Core.FieldValueParser.ReadInt32(row.Values["ItemId"])) : LibreKO.UiIcons.Get("game/main-hand");
    }
}
