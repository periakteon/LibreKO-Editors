using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.World;

namespace LibreKO.Editors.Desktop;
public partial class WorldWorkspace
{
    internal static string TableLabel(WorldTable table) => table switch
    {
        WorldTable.ZoneInfos => "Zones", WorldTable.StartPositions => "Nation starts & gates", WorldTable.Homes => "Nation home locations",
        WorldTable.Npcs => "NPCs & monsters", WorldTable.NpcPositions => "Spawn positions", WorldTable.NpcItems => "NPC drop groups",
        WorldTable.MakeItemGroups => "Ordered item pools", WorldTable.MonsterSummons => "Monster summons", WorldTable.ItemExchanges => "NPC item exchanges",
        WorldTable.PetLevels => "Pet level progression", WorldTable.PetTransforms => "Pet transformations", WorldTable.GameEvents => "Map event definitions",
        WorldTable.MapObjects => "Database map objects", WorldTable.EventTriggers => "NPC / trap triggers", WorldTable.Warps => "Legacy database warps", _ => table.ToString()
    };
    void BuildUi()
    {
        var column = Ui.Column(this); workspaceColumn=column;column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var nav = Ui.Row(column);
        foreach (var (text, table) in new[] { ("Maps", WorldTable.ZoneInfos), ("NPCs & monsters", WorldTable.Npcs), ("Drops", WorldTable.NpcItems), ("Shops & exchanges", WorldTable.ItemExchanges), ("Summons & pets", WorldTable.MonsterSummons) })
        { var selected = table; Ui.Button(nav, text, () => _ = SelectTableAsync(selected)); }
        Ui.Button(nav, "Sources…", () => SourceSettingsView.Show(Session));
        Ui.Button(nav,"Database",ShowDatabase);Ui.Button(nav,"Source files",ShowSources);
        Ui.Add(nav, new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        Ui.Label(nav, "MAP & NPC", 12, Ui.Accent);
        var body = Ui.Add(column, new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffsets = [255] }); databaseBody=body;
        var libraryFrame = Ui.Add(body, new PanelContainer { CustomMinimumSize = new(235, 0) }); libraryFrame.AddThemeStyleboxOverride("panel", Ui.Surface(10));
        var library = Ui.Column(libraryFrame); Ui.Heading(library, "WORLD LIBRARY", 11).AddThemeColorOverride("font_color", Ui.Accent);
        tables = Ui.Add(library, new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false });
        foreach (var table in Enum.GetValues<WorldTable>()) tables.AddItem(TableLabel(table));
        tables.ItemSelected += index => { if (!rebuilding) _ = SelectTableAsync((WorldTable)index); };
        search = Ui.Input(library, "Search by name or ID"); search.TextSubmitted += text => { page = 0; _ = RunAsync(LoadListAsync); };
        Ui.Add(library,new ActivityIndicator(Session.Loading,"data"));
        kindFilter = Ui.Add(library, new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        kindFilter.AddItem("All prototype types"); kindFilter.AddItem("NPC definitions"); kindFilter.AddItem("Monster definitions");
        kindFilter.ItemSelected += index => { if (!rebuilding) { page = 0; _ = RunAsync(LoadListAsync); } };
        Library = Ui.Add(library, new ItemList { Name = "WorldLibrary", SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new(0, 160), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        Library.ItemSelected += index => { if (index < Documents.Count) _ = SelectAsync(Documents[(int)index].Key); };
        caption = Ui.Label(library, "Connect to browse world data", 11, Ui.Muted); caption.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var paging = Ui.Row(library); Ui.Button(paging, "Previous", () => { if (page > 0) { page--; _ = RunAsync(LoadListAsync); } }); Ui.Button(paging, "Next", () => { if (Documents.Count == 50) { page++; _ = RunAsync(LoadListAsync); } });
        var center = Ui.Add(body, new HSplitContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SplitOffsets = [520] });
        var overviewFrame = Ui.Add(center, new PanelContainer { CustomMinimumSize = new(240, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill }); overviewFrame.AddThemeStyleboxOverride("panel", Ui.Surface(10));
        var overview = Ui.Column(overviewFrame); title = Ui.Heading(overview, "World database", 19); title.ClipText = true;
        var tools = Ui.Row(overview); Ui.Button(tools, "New…", () => _ = ShowNewAsync()); Ui.Button(tools, "Duplicate…", () => _ = ShowDuplicateAsync()); Ui.Button(tools, "Remove…", () => _ = ShowRemoveAsync());Ui.Button(tools,"Compare seed…",()=>_ = CompareSeed());
        PreviewHost = Ui.Column(overview); PreviewHost.Name = "WorldPreview"; PreviewHost.SizeFlagsVertical = SizeFlags.ExpandFill;
        MapView = Ui.Add(PreviewHost,new WorldViewport(Session,ApplySpatialBatch)); MapView.SelectionChanged += keys => _ = SelectSpatialAsync(keys); MapView.ContextRequested+=ShowPreviewContext;
        previewDetails = Ui.Column(PreviewHost); previewDetails.SizeFlagsVertical = SizeFlags.ExpandFill;
        Related = Ui.Add(overview, new WorldRelatedRows(this) { Name="Related",CustomMinimumSize = new(0, 210) });
        var inspector = Ui.Add(center, new PanelContainer { CustomMinimumSize = new(350, 0) }); inspector.AddThemeStyleboxOverride("panel", Ui.Surface(10));
        var details = Ui.Column(inspector); Ui.Heading(details, "INSPECTOR", 11).AddThemeColorOverride("font_color", Ui.Accent);
        Ui.Add(details,new ActivityIndicator(Session.Loading,"references"));
        notice = Ui.Label(details, "Select a world record", 11, Ui.Muted); notice.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        contextTabs=Ui.Add(details,new TabContainer{SizeFlagsVertical=SizeFlags.ExpandFill});
        var raw=Ui.Add(contextTabs,new VBoxContainer{Name="Properties"});
        var scroll = Ui.Add(raw, new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled });
        Form = Ui.Column(scroll); Form.Name = "WorldForm";
        Contents=Ui.Add(contextTabs,new NpcContentsView(this));contextTabs.SetTabTitle(1,"Shop & loot");
        expandHide.AddRange([nav,libraryFrame,inspector,Related,title,tools,previewDetails]);
        MapView.ExpandRequested+=ToggleExpandedMap;MapView.FullscreenRequested+=ToggleMapFullscreen;
        SyncTable();
    }
    void ShowPreviewContext()
    {
        var dialog=Ui.Add(Session.Shell,new AcceptDialog{Title="Map & NPC context",MinSize=new(740,500)});var body=Ui.Column(dialog);body.Position=new(16,40);body.CustomMinimumSize=new(700,400);
        var text=Ui.Add(body,new TextEdit{Editable=false,CustomMinimumSize=new(690,350),WrapMode=TextEdit.LineWrappingMode.Boundary});
        text.Text=string.Join("\n\n",previewDetails.FindChildren("*","Label",true,false).OfType<Label>().Select(l=>l.Text));
        dialog.Confirmed+=()=>dialog.QueueFree();dialog.Canceled+=()=>dialog.QueueFree();dialog.PopupCentered();
    }
    void SyncTable()
    {
        rebuilding = true; tables.Select((int)Table); kindFilter.Visible = Table is WorldTable.Npcs or WorldTable.NpcItems or WorldTable.NpcPositions;
        if (!kindFilter.Visible) kindFilter.Select(0); rebuilding = false;
    }
    internal void RenderDocument()
    {
        ViewGeneration++; PendingInputs.Clear(); Ui.Clear(Form); Ui.Clear(previewDetails); RowEditor = null;
        var rows = Draft?.Rows ?? []; if (!rows.Any(r => r.Key == Selected)) Selected = rows.FirstOrDefault()?.Key;
        title.Text = Selected == null ? TableLabel(Table) : TableLabel(Selected.Table) + " · " + Selected.Id + (Selected.IsMonster is bool monster ? monster ? " · Monster" : " · NPC" : "");
        if (Selected != null && Catalog != null && Draft != null)
        {
            var schema = Catalog.Tables.Single(t => t.Table == Selected.Table);
            RowEditor = Ui.Add(Form, new WorldRowEditor(this)); RowEditor.Configure(Draft, schema, Selected);
            var values = rows.Single(r => r.Key == Selected).Values;
            Ui.Label(previewDetails, values["Name"]?.ToString() ?? values["MapName"]?.ToString() ?? TableLabel(Selected.Table), 21, Ui.Accent).AutowrapMode = TextServer.AutowrapMode.WordSmart;
            Ui.Label(previewDetails, "Database · " + Selected.Table + " · " + Selected.Id + (Selected.Zone is int zone ? " · zone " + zone : ""), 12, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (Selected.Table == WorldTable.Warps) Ui.Label(previewDetails, "LEGACY DATABASE ROW\nThis server loads its live warp destinations from SMD. Use the server map view to manage those destinations.", 13, Ui.Accent).AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (Selected.Table == WorldTable.ZoneInfos) Ui.Label(previewDetails, "Initial coordinates are stored server values. Shared-map start positions and spawn inheritance are resolved by the server; explicit rows take precedence.", 12, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (Selected.Table is WorldTable.ZoneInfos or WorldTable.StartPositions) _ = DescribeMapContextAsync(Selected, ViewGeneration);
            if (Selected.Table == WorldTable.ItemExchanges) Ui.Label(previewDetails, "NPC exchanges are database records. Merchant shops and other service lists use the selected server source profile.", 12, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
            Ui.Label(previewDetails, "Source profile: " + (Session.Sources.Profile?.Label ?? "Not configured"), 12, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        }
        else Ui.Label(Form, Session.Connected ? "Select a supported record or create a new definition." : "Connect to a database to edit world data.", 13, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        UpdateNotice(); MapView.SyncDraft(Draft); _ = Related.LoadAsync(Selected); PreviewReady=RefreshMapPreviewAsync(Selected,ViewGeneration);
        contextTabs.SetTabHidden(1,Selected?.Table is not(WorldTable.Npcs or WorldTable.NpcItems or WorldTable.MakeItemGroups));Contents.Show(Selected);
    }
    async Task DescribeMapContextAsync(WorldKey key, int view)
    {
        var label = Ui.Label(previewDetails, "Resolving effective starts and spawns…", 12, Ui.Muted); label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        try
        {
            var context = await Session.Client.SendAsync<WorldMapContext>("world.map-context", new { zone = key.Id });
            if (!IsInstanceValid(label) || !label.IsInsideTree() || view != ViewGeneration || Selected != key) return;
            label.Text = (context.StartSourceZone is int start ? start == key.Id ? "Start positions: explicit zone row" : "Start positions: inherited from zone " + start : "No start row; runtime falls back to stored Zone InitX/InitZ, then Moradon") + "\n" +
                "Spawn rows: zone " + context.SpawnSourceZone + (context.SpawnSourceZone == key.Id ? " (own rows / no confirmed sibling)" : " (same map family and SMD)") + "\n" +
                "Start ranges add independent positive offsets. Nonzero nation ranges override shared ranges.\n" + string.Join("\n", context.Diagnostics);
            if (context.StartSourceZone is int source) Ui.Button(previewDetails, "Open effective start positions · " + source, () => _ = SelectAsync(new(WorldTable.StartPositions, source)));
        }
        catch (Exception e) { if (IsInstanceValid(label) && label.IsInsideTree() && view == ViewGeneration) label.Text = "Runtime context unavailable · " + e.Message; }
    }
    void UpdateNotice()
    {
        if (notice == null) return;
        var changes = Draft?.Build(Guid.Empty).Rows ?? [];
        notice.Text = InvalidFields.Count > 0 ? "Invalid input · correct the highlighted field" : changes.Count > 0 ? changes.Count + " unsaved row changes · Database" : "Database snapshot · keys are immutable";
        notice.Modulate = changes.Count > 0 || InvalidFields.Count > 0 ? Ui.Accent : Ui.Muted;
    }
}
