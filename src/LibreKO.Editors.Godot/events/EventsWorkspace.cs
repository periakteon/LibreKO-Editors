using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Events;

namespace LibreKO.Editors.Desktop;
public partial class EventsWorkspace : Control, IEditorWorkspace
{
    internal EditorSession Session = null !;
    internal EventCatalog? Catalog;
    internal EventDraft? Draft;
    internal readonly List<EventDocumentSummary> Documents = [];
    internal EventFamily Family = EventFamily.Temple;
    internal bool Active;
    internal readonly HashSet<string> InvalidFields = [];
    internal int ViewGeneration;
    internal EventDocumentKey? Selected;
    internal EventRowKey? SelectedRow;
    internal EventTable? SelectedTable;
    EventScheduleView? scheduleView;
    readonly Dictionary<EventTable, ItemList> rowLists = [];
    LineEdit search = null !;
    ItemList list = null !;
    VBoxContainer details = null !;
    Label pageCaption = null !, notice = null !;
    int page;
    readonly Dictionary<EventFamily, Button> familyButtons = [];
    public string Id => "events";
    public Control View => this;

    internal readonly List<Action> PendingInputs = [];
    internal void FlushFocusedInput()
    {
        foreach (var flush in PendingInputs.ToArray())
            flush();
    }

    public bool HasUnsavedChanges
    {
        get
        {
            FlushFocusedInput();
            return Draft?.IsDirty == true || InvalidFields.Count > 0;
        }
    }

    public bool CanSave => Session.Connected && HasUnsavedChanges && InvalidFields.Count == 0 && Draft!.BuildChange(Guid.Empty).Rows.All(r => Session.EventCompatibility?.Tables.SingleOrDefault(t => t.Table == r.Key.Table)?.CanSave == true);

    public Task InitializeAsync(EditorSession session)
    {
        Session = session;
        BuildUi();
        return Task.CompletedTask;
    }

    public async Task RefreshAsync()
    {
        if (HasUnsavedChanges)
            return;
        if (!Session.Connected)
        {
            RenderDocument();
            return;
        }

        async Task Load()
        {
            Catalog ??= await Session.Client.SendAsync<EventCatalog>("events.catalog", new { });
            await LoadListAsync();
            if (Selected != null)
                await SelectAsync(Selected);
            else if (Documents.Count > 0)
                await SelectAsync(Documents[0].Key);
        }

        if (Session.Operations.CanMutate)
            await Session.RunAsync(Load);
        else
            await Load();
    }

    internal async Task SelectFamilyAsync(EventFamily family)
    {
        Session.Shell.RememberContext();
        if (Family != family && !await Session.Shell.CanLeaveAsync())
            return;
        Family = family;
        Selected = null;
        Draft = null;
        SelectedRow = null;
        SelectedTable = null;
        InvalidFields.Clear();
        page = 0;
        search.Text = "";
        foreach (var pair in familyButtons)
            Ui.Navigation(pair.Value, pair.Key == family);
        Catalog ??= await Session.Client.SendAsync<EventCatalog>("events.catalog", new { });
        await LoadListAsync();
        if (Documents.Count > 0)
            await SelectAsync(Documents[0].Key);
        else
            RenderDocument();
    }

    async Task LoadListAsync()
    {
        list.Clear();
        Documents.Clear();
        if (!Session.Connected)
            return;
        var generation = Session.Generation;
        var family = Family;
        try
        {
            var result = await Session.Client.SendAsync<Page<EventDocumentSummary>>("events.list", new { family, query = new RecordQuery(search.Text, page, 50) });
            if (generation != Session.Generation || family != Family)
                return;
            Documents.AddRange(result.Rows);
            foreach (var row in Documents)
                list.AddItem(KeyLabel(row.Key) + "  ·  " + row.Label);
            pageCaption.Text = $"{result.Total:N0} events · page {page + 1}";
            _ = EventRewardView.NameDocumentsAsync(this, list, Documents.ToArray());
        }
        catch (Exception e)
        {
            pageCaption.Text = "Unavailable";
            Session.Shell.SetStatus(e.Message);
        }
    }

    internal async Task SelectAsync(EventDocumentKey key)
    {
        Session.Shell.RememberContext();
        if (key != Selected && !await Session.Shell.CanLeaveAsync())
            return;
        var generation = Session.Generation;
        var result = await Session.Client.SendAsync<EventDocumentSnapshot?>("events.get", key);
        if (generation != Session.Generation)
            return;
        if(result?.Rows==null)throw new InvalidOperationException("This event no longer exists.");
        Selected = key;
        Draft = result?.Rows != null ? new(result) : null;
        InvalidFields.Clear();
        ViewGeneration++;
        RenderDocument();
        Session.Shell.RecordLocation();
        var i = Documents.FindIndex(d => d.Key == key);
        if (i >= 0)
            list.Select(i);
        var selection = Selected;
        var warnings = await Session.Client.SendAsync<string[]>("events.diagnostics", key);
        if (generation == Session.Generation && Selected == selection)
            Session.Shell.SetStatus(warnings.Length > 0 ? string.Join(" · ", warnings) : "Event loaded · changes remain in the draft until saved");
    }

    public async Task<bool> SaveAsync()
    {
        FlushFocusedInput();
        if (Draft == null || !Session.Connected || InvalidFields.Count > 0)
            return false;
        bool saved = false;
        await Session.RunAsync(async () =>
        {
            var result = await Session.Client.SendAsync<EventSaveResult>("events.save", Draft.BuildChange(Guid.NewGuid()));
            Session.Shell.SetStatus(result.Message ?? string.Join("; ", result.Issues?.Select(i => i.Field + ": " + i.Message) ?? []));
            if (result.Status == SaveStatus.Saved)
            {
                saved = true;
                Session.NotifyDatabaseChange();
                Selected = result.Document?.Key;
                Draft = result.Document == null ? null : new(result.Document);
                InvalidFields.Clear();
                ViewGeneration++;
                await LoadListAsync();
                RenderDocument();
            }
            else if (result.Status == SaveStatus.UnknownCommit)
            {
                Session.Connected = false;
                Session.Shell.SetStatus(result.Message + " Reconnect, then open Event history.");
            }
        });
        return saved;
    }

    public void Discard()
    {
        if (Draft != null)
            Draft = Draft.Original.Version == "missing" ? null : new(Draft.Original);
        InvalidFields.Clear();
        ViewGeneration++;
        RenderDocument();
    }

    public void Undo(bool redo)
    {
        if (Draft == null)
            return;
        if (redo)
            Draft.Redo();
        else
            Draft.Undo();
        InvalidFields.Clear();
        ViewGeneration++;
        RenderDocument();
    }

    public void FocusSearch() => search.GrabFocus();
    public void SetActive(bool active)
    {
        Active = active;
        Visible = active;
        if (!active)
            ViewGeneration++;
    }

    public void InvalidateDatabaseState()
    {
        Catalog = null;
        Draft = null;
        Selected = null;
        SelectedRow = null;
        InvalidFields.Clear();
        ViewGeneration++;
        if (list != null)
        {
            Documents.Clear();
            list.Clear();
            RenderDocument();
        }
    }

    public void ShowHistory() => _ = EventHistoryView.OpenAsync(Session);
    public void ShowCompatibility() => Session.Shell.ShowEventCompatibility();
    internal static string KeyLabel(EventDocumentKey key) => key.Zone is { } z ? $"{z} / {key.Id}" : key.Id.ToString();
    internal EventDocumentSnapshot CurrentSnapshot() => new(Draft!.Original.Key, Draft.Rows, Draft.Original.Version);
    internal void SetValue(EventRowKey key, string field, System.Text.Json.Nodes.JsonNode? value)
    {
        if (System.Text.Json.Nodes.JsonNode.DeepEquals(Draft!.Rows.Single(r => r.Key == key).Values[field], value))
            return;
        Draft.Set(key, field, value);
        if (scheduleView != null && IsInstanceValid(scheduleView))
        {
            Ui.Clear(scheduleView);
            scheduleView.Show(CurrentSnapshot(), Session.Profile.ServerScheduleTimeZone);
        }

        if (rowLists.TryGetValue(key.Table, out var list) && IsInstanceValid(list))
        {
            var rows = Draft.Rows.Where(r => r.Key.Table == key.Table).ToArray();
            for (var i = 0; i < rows.Length; i++)
            {
                list.SetItemText(i, RowLabel(rows[i]));
                list.SetItemIcon(i, RewardIcon(rows[i]));
            }

            if (rows.Any(r => r.Values.ContainsKey("ItemId")))
                _ = EventRewardView.NameRowsAsync(this, list, rows);
        }

        notice.Text = Draft.IsDirty ? "Unsaved changes · Save to database to apply" : "Database snapshot";
        if (field is "Kind" or "Type")
        {
            ViewGeneration++;
            RenderDocument();
        }
    }
}
