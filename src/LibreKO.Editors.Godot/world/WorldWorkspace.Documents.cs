using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.World;

namespace LibreKO.Editors.Desktop;
public partial class WorldWorkspace
{
    async Task LoadListAsync()
    {
        Documents.Clear(); Library.Clear(); if (!Session.Connected) return;
        var table = Table; var serial = ++listSerial; var generation = Session.Generation;
        var support = Session.WorldCompatibility?.Tables.SingleOrDefault(t => t.Table == table);
        if (support?.CanBrowse == false) { caption.Text = "Unsupported section · " + support.Reason; return; }
        var monster = kindFilter.Visible && kindFilter.Selected > 0 ? kindFilter.Selected == 2 : (bool?)null;
        try
        {
            var result = await Session.Client.SendAsync<Page<WorldRowSnapshot>>("world.list", new WorldQuery(table, search.Text, page, 50, IsMonster: monster));
            if (serial != listSerial || generation != Session.Generation || table != Table) return;
            Documents.AddRange(result.Rows); foreach (var row in Documents) Library.AddItem(RowLabel(row)); caption.Text = $"{result.Total:N0} records · page {page + 1}";
            _ = NameListAsync(result.Rows.ToArray(), serial);
        }
        catch (Exception e) { if (serial == listSerial) { caption.Text = "Section unavailable"; Session.Shell.SetStatus(e.Message); } }
    }
    internal static string RowLabel(WorldRowSnapshot row)
    {
        var name = row.Values["Name"]?.ToString() ?? row.Values["MapName"]?.ToString() ?? TableLabel(row.Key.Table);
        return row.Key.Id + " · " + name + (row.Key.IsMonster is bool monster ? monster ? " · Monster" : " · NPC" : "") + (row.Key.Zone is int zone ? " · zone " + zone : "");
    }
    async Task NameListAsync(WorldRowSnapshot[] rows, int serial)
    {
        var scope = Session.References.Scope;
        var refs = rows.SelectMany(r => r.Values.Select(v => WorldRules.ReferenceFor(r.Key.Table, v.Key, r.Values))).Where(x => x != null).Select(x => x!).Distinct().ToArray();
        try
        {
            var names = await Session.References.ResolveBatchAsync(refs);
            if (!IsInstanceValid(Library) || !Active || serial != listSerial || scope != Session.References.Scope || !rows.Select(r => r.Key).SequenceEqual(Documents.Select(r => r.Key))) return;
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i]; var relevant = row.Values.Select(v => WorldRules.ReferenceFor(row.Key.Table, v.Key, row.Values)).Where(x => x != null).Select(x => x!).Distinct().Take(2).ToArray();
                var text = RowLabel(row) + (relevant.Length == 0 ? "" : " · " + string.Join(" · ", relevant.Select(r => EventRewardView.Describe(r, names))));
                Library.SetItemText(i, text); Library.SetItemTooltip(i, text + "\nDatabase · " + row.Key.Table);
            }
        }
        catch { /* Raw IDs and record names remain usable if a lookup is unavailable. */ }
    }
    public async Task AddAsync(WorldTable table, JsonObject values)
    {
        if (!Active || !Session.Operations.CanMutate) return;
        Catalog ??= await Session.Client.SendAsync<WorldCatalog>("world.catalog", new { });
        var key = WorldKeys.From(table, values);
        if (Table != table && HasUnsavedChanges && !await Session.Shell.CanLeaveAsync()) return;
        if (Table != table) { baseline.Clear(); memberships = []; Draft = null; }
        Table = table; SyncTable(); Draft ??= new([]); Draft.Apply([new(key, null, values)]); Selected = key; InvalidFields.Clear(); ViewGeneration++; RenderDocument();
    }
    internal async Task DuplicateAsync(int? chosenId = null)
    {
        if (!Active || !Session.Operations.CanMutate || Draft == null || Selected == null) return;
        var original = Draft.Rows.Single(r => r.Key == Selected); var reserved = Draft.Rows.Where(r => r.Key.Table == Selected.Table).Select(r => r.Key.Id).ToArray();
        var id = chosenId ?? await Session.Client.SendAsync<int>("world.next-id", new { table = Selected.Table, zone = Selected.Zone, isMonster = Selected.IsMonster, reserved });
        var values = (JsonObject)original.Values.DeepClone(); values[Selected.Table == WorldTable.GameEvents ? "EventNum" : WorldKeys.Fields(Selected.Table)[0]] = id;
        if (values.ContainsKey("Name")) values["Name"] = values["Name"] + " (copy)";
        await AddAsync(Selected.Table, values);
    }
    async Task ShowNewAsync()
    {
        if (!Session.Connected || !Session.Operations.CanMutate) return;
        Catalog ??= await Session.Client.SendAsync<WorldCatalog>("world.catalog", new { });
        var table = Table; var schema = Catalog.Tables.Single(t => t.Table == table); var values = (JsonObject)schema.Defaults.DeepClone();
        var monster = table is WorldTable.Npcs or WorldTable.NpcItems ? kindFilter.Selected != 1 : (bool?)null; var zone = table == WorldTable.GameEvents ? 210 : (int?)null;
        var id = await Session.Client.SendAsync<int>("world.next-id", new { table, zone, isMonster = monster, reserved = Draft?.Rows.Where(r => r.Key.Table == table).Select(r => r.Key.Id).ToArray() });
        values[table == WorldTable.GameEvents ? "EventNum" : schema.KeyFields[0]] = id;
        if (monster != null) values["IsMonster"] = monster.Value; if (zone != null) values["ZoneNum"] = zone.Value;
        if (values.ContainsKey("Name")) values["Name"] = "New " + TableLabel(table); if (table == WorldTable.GameEvents) values["Type"] = 1;
        var d = Ui.Add(Session.Shell, new ConfirmationDialog { Title = "New " + TableLabel(table), MinSize = new(570, 320), OkButtonText = "Create draft", DialogHideOnOk = false });
        var body = Ui.Column(d); body.Position = new(16, 40); body.CustomMinimumSize = new(530, 230);
        Ui.Label(body, "Review the complete new key. Shared groups are referenced independently. Nothing is written until Save to database.", 12, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var inputs = new Dictionary<string, LineEdit>();
        foreach (var field in schema.KeyFields) inputs[field] = Ui.Field(body, field, "Stored key", values[field]?.ToString() ?? "");
        var error = Ui.Label(body, "", 12, Ui.Accent); var generation = Session.Generation;
        d.Confirmed += async () =>
        {
            try
            {
                if (generation != Session.Generation) throw new InvalidOperationException("The selected database changed.");
                foreach (var field in schema.KeyFields) values[field] = Core.FieldValueParser.Parse(schema.Fields.Single(f => f.Field.Name == field).Field, inputs[field].Text);
                await AddAsync(table, values); d.QueueFree();
            }
            catch (Exception e) { error.Text = e.Message; }
        };
        d.Canceled += () => d.QueueFree(); d.PopupCentered();
    }
    Task ShowDuplicateAsync()
    {
        if (Selected == null) return Task.CompletedTask;
        var key = Selected; var d = Ui.Add(Session.Shell, new ConfirmationDialog { Title = "Duplicate world record", DialogText = "Duplicate " + key.Table + " · " + key.Id + (key.IsMonster is bool monster ? monster ? " · Monster" : " · NPC" : "") + "?\n\nA new unused key is assigned. Shared drop/shop groups remain references to the same groups. Review the new draft before saving.", OkButtonText = "Duplicate into draft", MinSize = new(620, 230) });
        var generation = Session.Generation;
        d.Confirmed += async () => { d.QueueFree(); if (Selected == key && generation == Session.Generation) await DuplicateAsync(); }; d.Canceled += () => d.QueueFree(); d.PopupCentered(); return Task.CompletedTask;
    }
    async Task ShowRemoveAsync()
    {
        if (Selected == null || Draft == null || !Session.Operations.CanMutate) return;
        var key = Selected; var snapshot = Draft.Rows.Single(r => r.Key == key); var deps = new List<WorldMembership>();
        if (snapshot.Version != "missing") foreach (var relation in WorldRules.Relations(key.Table)) deps.Add(await Session.Client.SendAsync<WorldMembership>("world.membership", new { owner = key, relation }));
        var d = Ui.Add(Session.Shell, new ConfirmationDialog { Title = "Remove world record", MinSize = new(740, 540), OkButtonText = "Stage selected removal" });
        var body = Ui.Column(d); body.Position = new(16, 40); body.CustomMinimumSize = new(700, 430);
        Ui.Label(body, RowLabel(snapshot), 16, Ui.Accent); Ui.Label(body, "Only this record is staged for removal. Dependent records must be reviewed explicitly. Save rechecks references and refuses implicit cascades.", 12, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var text = Ui.Add(body, new TextEdit { Editable = false, CustomMinimumSize = new(690, 300), WrapMode = TextEdit.LineWrappingMode.Boundary });
        text.Text = string.Join("\n", deps.SelectMany(m => m.BeforeKeys.Select(k => m.Relation + " · " + k.Table + " " + k.Id + (k.IsMonster is bool monster ? monster ? " · Monster" : " · NPC" : "")))) + "\n\nOriginal mapped row:\n" + snapshot.Values.ToJsonString();
        var generation = Session.Generation; var draft = Draft;
        d.Confirmed += () => { d.QueueFree(); if (generation == Session.Generation && ReferenceEquals(draft, Draft)) ApplyBatch([new(key, snapshot, null)]); }; d.Canceled += () => d.QueueFree(); d.PopupCentered();
    }
}
