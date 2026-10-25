using Godot;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Events;

namespace LibreKO.Editors.Desktop;
public partial class EventsWorkspace
{
    internal async Task CreateAsync(int id, int? zone = null, bool duplicate = false)
    {
        if (!await Session.Shell.CanLeaveAsync())
            return;
        Catalog ??= await Session.Client.SendAsync<EventCatalog>("events.catalog", new { });
        if (Family is EventFamily.Temple or EventFamily.CastleSiege)
            throw new InvalidOperationException("Only existing definitions are supported for this family.");
        EventChange? copy = null;
        if (duplicate && Selected != null)
            copy = await Session.Client.SendAsync<EventChange>("events.duplicate", new { source = Selected, newId = id, zone });
        var key = new EventDocumentKey(Family, id, zone);
        Draft = new(new(key, [], "missing"));
        Selected = key;
        SelectedRow = null;
        SelectedTable = null;
        if (copy != null)
            Draft.Apply(copy.Rows);
        else
        {
            var table = Catalog.Families.Single(f => f.Family == Family).Tables[0];
            var values = (JsonObject)Catalog.Tables.Single(t => t.Table == table).Defaults.DeepClone();
            values[EventKeys.Fields(table)[^1]] = id;
            if (zone != null)
                values["ZoneNum"] = zone.Value;
            if (values.ContainsKey("Name"))
                values["Name"] = "New " + (Family == EventFamily.CollectionRace ? "Collection Race" : "Lottery");
            if (table == EventTable.GameEvents)
                values["Type"] = 1;
            Draft.Add(EventKeys.From(table, values), values);
        }

        InvalidFields.Clear();
        ViewGeneration++;
        RenderDocument();
    }

    internal async Task AddRowAsync(EventTable table)
    {
        if (Draft == null)
            return;
        var draft = Draft;
        var generation = Session.Generation;
        var id = await Session.Client.SendAsync<int>("events.next-id", new { table, reserved = Draft.Rows.Where(r => r.Key.Table == table).Select(r => r.Key.Id).ToArray() });
        if (generation != Session.Generation || Draft != draft)
            return;
        while (Draft.Rows.Any(r => r.Key.Table == table && r.Key.Id == id))
            id++;
        var values = (JsonObject)Catalog!.Tables.Single(t => t.Table == table).Defaults.DeepClone();
        values[EventKeys.Fields(table)[^1]] = id;
        if (values.ContainsKey("Event"))
            values["Event"] = Draft.Original.Key.Id;
        if (values.ContainsKey("RaceId"))
            values["RaceId"] = Draft.Original.Key.Id;
        if (values.ContainsKey("LotteryId"))
            values["LotteryId"] = Draft.Original.Key.Id;
        if (table == EventTable.ForgottenTempleWaves)
        {
            values["Tier"] = 1;
            values["Wave"] = 1;
        }

        if (values.ContainsKey("Count"))
            values["Count"] = 1;
        if (values.ContainsKey("ItemCount"))
            values["ItemCount"] = 1;
        if (values.ContainsKey("Place"))
            values["Place"] = 1;
        if (values.ContainsKey("Ordinal"))
            values["Ordinal"] = Draft.Rows.Count(r => r.Key.Table == table);
        var key = EventKeys.From(table, values);
        Draft.Add(key, values);
        SelectedRow = key;
        SelectedTable = table;
        InvalidFields.Clear();
        ViewGeneration++;
        RenderDocument();
    }

    bool HasOpaqueColumns(EventTable table) => Session.EventCompatibility?.Tables.SingleOrDefault(t => t.Table == table)?.Issues.Any(i => i.Code == "schema.opaque-extra") == true;
    internal async Task DuplicateRowAsync(EventRowKey key)
    {
        if (Draft == null)
            return;
        var draft = Draft;
        var generation = Session.Generation;
        var source = Draft.Rows.Single(r => r.Key == key);
        var id = await Session.Client.SendAsync<int>("events.next-id", new { table = key.Table, reserved = Draft.Rows.Where(r => r.Key.Table == key.Table).Select(r => r.Key.Id).ToArray() });
        if (generation != Session.Generation || Draft != draft)
            return;
        var values = (JsonObject)source.Values.DeepClone();
        values[EventKeys.Fields(key.Table)[^1]] = id;
        if (values.ContainsKey("Ordinal"))
        {
            var used = Draft.Rows.Where(r => r.Key.Table == key.Table).Select(r => Core.FieldValueParser.ReadInt32(r.Values["Ordinal"])).ToHashSet();
            values["Ordinal"] = Enumerable.Range(0, 256).First(n => !used.Contains(n));
        }

        if (key.Table == EventTable.LotteryRewards)
        {
            var used = Draft.Rows.Where(r => r.Key.Table == key.Table).Select(r => Core.FieldValueParser.ReadInt32(r.Values["Place"])).ToHashSet();
            var place = 1;
            while (used.Contains(place))
                place++;
            values["Place"] = place;
        }

        var target = EventKeys.From(key.Table, values);
        Draft.Add(target, values);
        SelectedRow = target;
        SelectedTable = key.Table;
        ViewGeneration++;
        RenderDocument();
    }

    void ShowCreateDialog() => ShowCreateDialog(false);
    async void ShowCreateDialog(bool duplicate)
    {
        if (!Session.Connected || !await Session.Shell.CanLeaveAsync())
            return;
        var table = Catalog!.Families.Single(f => f.Family == Family).Tables[0];
        var d = Ui.Add(Session.Shell, new ConfirmationDialog { Title = duplicate ? "Duplicate event" : "New event definition", DialogHideOnOk = false, MinSize = new(520, 270), OkButtonText = "Create draft" });
        var body = Ui.Column(d);
        body.Position = new(14, 38);
        body.CustomMinimumSize = new(480, 0);
        Ui.Label(body, "IDs are provisional. The database is changed only when you save.", 12, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var zone = Family == EventFamily.MapEvent ? Ui.Field(body, "Zone ID", "Zone", Selected?.Zone?.ToString() ?? "1") : null;
        var id = Ui.Field(body, "New unused ID", "ID");
        if (zone != null)
        {
            var zoneName = Ui.Label(body, "", 12, Ui.Accent);
            var generation = Session.Generation;
            bool Current() => IsInstanceValid(d) && !Session.IsClosing && generation == Session.Generation;
            void NameZone() => Session.ReferencePresenter.Bind(zoneName, () => int.TryParse(zone.Text, out var value) ? new("zones", value) : null,
                Current, named => { zoneName.Text = Core.References.ReferenceLabels.Display(named); zoneName.TooltipText = named.Source; });
            zone.TextChanged += text => NameZone(); NameZone();
            Ui.Button(body, "Browse zones…", () => ReferencePicker.Show(d, Session, new("zones"), named => { zone.Text = named.Key.Id.ToString(); }, Current));
        }
        await Session.RunAsync(async () =>
        {
            id.Text = (await Session.Client.SendAsync<int>("events.next-id", new { table, zone = zone == null ? (int? )null : int.Parse(zone.Text) })).ToString();
        });
        var error = Ui.Label(body, "", 12, Ui.Accent);
        d.Confirmed += async () =>
        {
            if (!int.TryParse(id.Text, out var target) || target <= 0)
            {
                error.Text = "Choose a positive unused ID.";
                return;
            }

            var ok = await Session.RunAsync(() => CreateAsync(target, zone == null ? null : int.Parse(zone.Text), duplicate));
            if (ok)
                d.QueueFree();
        };
        d.Canceled += () => d.QueueFree();
        d.PopupCentered();
    }

    void ConfirmRemoval(EventRowKey? key)
    {
        if (Draft == null)
            return;
        var affected = key == null ? Draft.Rows : Draft.Rows.Where(r => r.Key == key).ToArray();
        var d = Ui.Add(Session.Shell, new ConfirmationDialog { Title = "Review removal", OkButtonText = "Remove from draft", DialogText = "These exact rows will be removed when you save:\n\n" + string.Join("\n", affected.Select(r => r.Key.Table + " · " + r.Key.Id + (r.Key.Zone is { } z ? " · zone " + z : ""))), MinSize = new(540, 250) });
        var ownedDraft = Draft;
        async Task NameRemoval()
        {
            try
            {
                var names = await EventRewardView.DescribeChangesAsync(Session, affected.Select(r => new EventRowChange(r.Key, r, null)).ToArray());
                if (IsInstanceValid(d) && ReferenceEquals(Draft, ownedDraft)) d.DialogText += "\n\n" + names;
            }
            catch { }
        }
        _ = NameRemoval();
        d.Confirmed += () =>
        {
            Draft.Apply(affected.Select(r => new EventRowChange(r.Key, r, null)).ToArray());
            InvalidFields.Clear();
            ViewGeneration++;
            RenderDocument();
            d.QueueFree();
        };
        d.Canceled += () => d.QueueFree();
        d.PopupCentered();
    }
}
