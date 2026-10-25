using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.Core.Sources;

namespace LibreKO.Editors.Desktop;
public partial class WorldWorkspace : Control, IEditorWorkspace
{
    internal EditorSession Session = null!;
    internal WorldCatalog? Catalog;
    internal WorldDraft? Draft;
    internal WorldKey? Selected;
    internal WorldTable Table = WorldTable.ZoneInfos;
    internal bool Active;
    internal int ViewGeneration;
    internal readonly HashSet<string> InvalidFields = [];
    internal readonly List<Action> PendingInputs = [];
    internal readonly List<WorldRowSnapshot> Documents = [];
    readonly List<WorldRowSnapshot> baseline = [];
    WorldMembership[] memberships = [];
    internal VBoxContainer Form = null!, PreviewHost = null!;
    internal ItemList Library = null!;
    internal WorldRelatedRows Related = null!;
    internal WorldRowEditor? RowEditor;
    internal WorldViewport MapView = null!;
    internal Task PreviewReady = Task.CompletedTask;
    LineEdit search = null!;
    OptionButton tables = null!, kindFilter = null!;
    Label caption = null!, notice = null!, title = null!;
    VBoxContainer previewDetails = null!;
    int page, listSerial;
    bool rebuilding;
    public string Id => "world";
    public Control View => this;
    void Flush() { foreach (var apply in PendingInputs.ToArray()) apply(); }
    public bool HasUnsavedChanges { get { Flush(); return Draft?.IsDirty == true || InvalidFields.Count > 0 || Session.Sources.HasUnsavedChanges; } }
    public bool CanSave => Session.Sources.HasUnsavedChanges || Session.Connected && HasUnsavedChanges && InvalidFields.Count == 0 && Draft!=null && Draft.Build(Guid.Empty).Rows.All(r => Session.WorldCompatibility?.Tables.SingleOrDefault(t => t.Table == r.Key.Table)?.CanSave == true);
    public string SaveLabel=>Session.Sources.HasUnsavedChanges?"Review changes":"Save to database";
    public IReadOnlyList<string> PendingDestinations=>new[]{Draft?.IsDirty==true||InvalidFields.Count>0?"Database":null,Session.Sources.HasUnsavedChanges?"Selected source files":null}.OfType<string>().ToArray();
    public Task InitializeAsync(EditorSession session)
    { Session = session; BuildUi(); session.Shell.Assets.EnsureLoaded(); return Task.CompletedTask; }
    internal async Task RunAsync(Func<Task> action)
    { if (Session.Operations.CanMutate) await Session.RunAsync(action); else await action(); }
    public async Task RefreshAsync()
    {
        if(sourceMode&&sourceViewGeneration!=Session.Sources.Generation)ShowSources();
        if (HasUnsavedChanges) return;
        if (!Session.Connected) { RenderDocument(); return; }
        await RunAsync(async () =>
        {
            Catalog ??= await Session.Client.SendAsync<WorldCatalog>("world.catalog", new { });
            if (Session.WorldCompatibility == null || Session.WorldReportStale) await Session.RefreshWorldCompatibilityAsync();
            await LoadListAsync();
            if (Selected != null) await LoadSelectionAsync(Selected);
            else if (Documents.Count > 0) await LoadSelectionAsync(Documents[0].Key);
            else RenderDocument();
        });
    }
    public async Task SelectAsync(WorldKey key)
    {
        Session.Shell.RememberContext();
        if ((baseline.Count > 1 || HasUnsavedChanges) && Draft?.Rows.Any(r => r.Key == key) == true)
        { Selected = key; Table = key.Table; SyncTable(); RenderDocument(); await FocusOpenedSpawnAsync(key);Session.Shell.RecordLocation();return; }
        if (HasUnsavedChanges && !await Session.Shell.CanLeaveAsync()) return;
        await RunAsync(async () =>
        {
            Catalog ??= await Session.Client.SendAsync<WorldCatalog>("world.catalog", new { });
            Table = key.Table; SyncTable(); await LoadListAsync(); await LoadSelectionAsync(key);
        });
        await FocusOpenedSpawnAsync(key);
    }
    internal async Task SelectTableAsync(WorldTable table)
    {
        Session.Shell.RememberContext();
        if (HasUnsavedChanges && !await Session.Shell.CanLeaveAsync()) { SyncTable(); return; }
        Table = table; Selected = null; Draft = null; baseline.Clear(); memberships = []; InvalidFields.Clear(); page = 0; search.Text = ""; SyncTable();
        await RefreshAsync();
    }
    internal async Task SearchAsync(string text, bool? monster = null)
    {
        search.Text = text; kindFilter.Select(monster == null ? 0 : monster.Value ? 2 : 1); page = 0; await RunAsync(LoadListAsync);
    }
    async Task LoadSelectionAsync(WorldKey key)
    {
        var generation = Session.Generation; var serial = ++ViewGeneration;
        var row = await Session.Client.SendAsync<WorldRowSnapshot?>("world.get", key);
        if (generation != Session.Generation || serial != ViewGeneration) return;
        Selected = row?.Key != null ? key : null; baseline.Clear(); memberships = [];
        if (row?.Key != null)
        {
            baseline.Add(row); var sets = new List<WorldMembership>();
            foreach (var relation in WorldRules.Relations(key.Table))
                try { sets.Add(await Session.Client.SendAsync<WorldMembership>("world.membership", new { owner = key, relation })); }
                catch { /* Related tables can be unsupported while this mapped record remains usable. */ }
            if (generation != Session.Generation || serial != ViewGeneration) return;
            memberships = sets.ToArray(); Draft = new(baseline, memberships);
        }
        else Draft = null;
        InvalidFields.Clear(); RenderDocument(); Session.Shell.RecordLocation();
    }
    public async Task<bool> SaveAsync()
    {
        MapView.FlushManipulations();
        var files=Session.Sources.CaptureDrafts();
        if(files!=null)
        {
            Flush();var database=InvalidFields.Count==0?Draft?.Build(Guid.NewGuid()):null;
            await SourceChangeReview.Show(Session,database,files);return !HasUnsavedChanges;
        }
        Flush(); if (Draft == null || !Session.Connected || InvalidFields.Count > 0) return false;
        var change = Draft.Build(Guid.NewGuid()); if (change.Rows.Count == 0) return true;
        bool saved = false;
        await Session.RunAsync(async () =>
        {
            var result = await Session.Client.SendAsync<WorldSaveResult>("world.save", change);
            Session.Shell.SetStatus(result.Message ?? string.Join("; ", result.Issues?.Select(i => i.Field + ": " + i.Message) ?? []));
            if (result.Status == SaveStatus.Saved)
            {
                saved = true; Session.NotifyDatabaseChange(); Draft = null; baseline.Clear(); InvalidFields.Clear();
                await LoadListAsync(); if (Selected != null) await LoadSelectionAsync(Selected); else RenderDocument();
            }
            else if (result.Status == SaveStatus.UnknownCommit) { Session.Connected = false; Session.Shell.SetStatus(result.Message + " Reconnect and open World history."); }
        });
        return saved;
    }
    internal async Task AcceptSavedAsync(){Draft=null;baseline.Clear();InvalidFields.Clear();await LoadListAsync();if(Selected!=null)await LoadSelectionAsync(Selected);else RenderDocument();}
    public void Discard() { Session.Sources.Drafts?.Discard();Draft = baseline.Count == 0 ? null : new(baseline, memberships); InvalidFields.Clear(); ViewGeneration++; RenderDocument(); }
    public void Undo(bool redo) { if (!Session.Operations.CanMutate) return;if(sourceMode){SourceView?.Undo(redo);return;}if(UndoContentsSource(redo))return;if(Draft==null)return; Draft.Undo(redo); InvalidFields.Clear(); ViewGeneration++; RenderDocument(); }
    internal void SetValue(WorldKey key, string field, JsonNode? value)
    { if (!Active || !Session.Operations.CanMutate || Draft == null) return; Draft.Set(key, field, value); RowEditor?.RefreshReferences(); UpdateNotice(); MapView.Sync(Draft.Rows); if(field is "ItemGroup" or "SellingGroup" or "NpcType")Contents.Show(Selected);else Contents.RefreshBoard();if(field is "ModelId" or "Size" or "WeaponType1" or "WeaponType2" or "NpcType") PreviewReady=RefreshMapPreviewAsync(Selected,ViewGeneration); }
    internal void ApplyBatch(IReadOnlyList<WorldRowChange> changes)
    { if (!Active || !Session.Operations.CanMutate || Draft == null) return; Draft.Apply(changes); ViewGeneration++; RenderDocument(); }
    internal Task IncludeAsync(WorldRowSnapshot row)
    {IncludeRow(row);return Task.CompletedTask;}
    internal void IncludeRow(WorldRowSnapshot row)
    { Draft ??= new([]); Draft.Include([row]); if (baseline.All(r => r.Key != row.Key)) baseline.Add(row); UpdateNotice(); }
    public void FocusSearch() => search.GrabFocus();
    public void SetActive(bool active) { if(!active)ExitMapExpansion();Active = active; Visible = active; MapView?.SetActive(active && !sourceMode && MapView.Visible);SourceView?.SetActive(active&&sourceMode); if (!active) ViewGeneration++; }
    public void InvalidateDatabaseState() { ViewGeneration++; Session.WorldReportStale = true; }
    public void ShowHistory() => _ = new HistoryRouter(Session).ShowAsync("world");
    public void ShowCompatibility() => CompatibilityView.Show(Session, "world");
}
