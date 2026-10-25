using System.Text.Json.Nodes;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Events;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Editors.DataHost.Events;
public sealed partial class EventDatabase
{
    public async Task<EventDocumentSnapshot?> GetAsync(EventDocumentKey key)
    {
        Require(await CheckCompatibilityAsync(), EventSchema.TablesFor(key));
        await using var db = Open();
        return await LoadDocumentAsync(db, key);
    }

    async Task<EventDocumentSnapshot?> LoadDocumentAsync(AppDbContext db, EventDocumentKey key, bool locking = false)
    {
        if (key.Family == EventFamily.MapEvent && key.Zone is not (>= 0 and <= 255) || key.Family != EventFamily.MapEvent && key.Zone != null)
            throw new InvalidOperationException("Provide the complete event key.");
        var rows = new List<EventRowSnapshot>();
        foreach (var table in EventSchema.TablesFor(key))
        {
            var filter = EventTableStore.DocumentFilter(db, table, key);
            var found = await EventTableStore.ReadAsync(db, table, filter.Sql + (locking ? " FOR UPDATE" : ""), filter.Args, locking);
            rows.AddRange(found.Select(row => EventSchema.Snapshot(table, row)));
        }

        if (key.Family != EventFamily.Temple && rows.All(r => r.Key.Table != EventSchema.TablesFor(key)[0]))
            return null;
        if (key.Family == EventFamily.Temple && key.Id is not (>= 1 and <= 5) && rows.Count == 0)
            return null;
        var ordered = rows.OrderBy(r => EventKeys.Order(r.Key), StringComparer.Ordinal).ToArray();
        return new(key, ordered, DocumentVersion(ordered));
    }

    public async Task<Page<EventDocumentSummary>> ListAsync(EventFamily family, RecordQuery query)
    {
        var tables = EventSchema.TablesFor(new(family, 1));
        Require(await CheckCompatibilityAsync(), tables);
        await using var db = Open();
        var size = Math.Clamp(query.Size, 1, 100);
        var page = Math.Clamp(query.Page, 0, 1000000);
        if (family == EventFamily.Temple)
        {
            var types = new HashSet<int>(Enumerable.Range(1, 5));
            foreach (var table in tables)
                foreach (var entity in await EventTableStore.ReadAsync(db, table))
                    types.Add(FieldValueParser.ReadInt32(EventSchema.Values(table, entity)["Event"]));
            var labels = new[]
            {
                "",
                "Chaos Dungeon",
                "Border Defense War",
                "Juraid Mountain",
                "Under the Castle",
                "Forgotten Temple"
            };
            var rows = types.Order().Select(id => new EventDocumentSummary(new(family, id), id is >= 1 and <= 5 ? labels[id] : "Unknown activity " + id, new())).Where(r => string.IsNullOrWhiteSpace(query.Text) || r.Key.Id.ToString() == query.Text.Trim() || r.Label.Contains(query.Text, StringComparison.OrdinalIgnoreCase)).ToArray();
            return new(rows.Skip(page * size).Take(size).ToArray(), rows.Length, page, size);
        }

        var root = tables[0];
        var where = "";
        object[] args = [];
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            if (int.TryParse(text, out var id))
            {
                where = "WHERE " + EventTableStore.Column(db, root, EventKeys.Fields(root)[^1]) + " = {0}";
                args = [id];
                if (family == EventFamily.MapEvent)
                    where += " OR " + EventTableStore.Column(db, root, "ZoneNum") + " = {0}";
            }
            else if (EventSchema.Properties(root).Any(p => p.Name == "Name"))
            {
                where = "WHERE LOCATE({0}," + EventTableStore.Column(db, root, "Name") + ") > 0";
                args = [text];
            }
            else
            {
                return new([], 0, page, size);
            }
        }

        var total = await EventTableStore.CountAsync(db, root, where, args);
        var order = string.Join(",", EventKeys.Fields(root).Select(f => EventTableStore.Column(db, root, f)));
        var foundRows = await EventTableStore.ReadAsync(db, root, where + " ORDER BY " + order + " LIMIT " + size + " OFFSET " + page * size, args);
        var summaries = foundRows.Select(entity =>
        {
            var row = EventSchema.Snapshot(root, entity);
            var key = new EventDocumentKey(family, row.Key.Id, row.Key.Zone);
            var label = row.Values["Name"]?.ToString() ?? family switch
            {
                EventFamily.MapEvent => $"Zone {key.Zone} · Event {key.Id} · type {row.Values["Type"]}",
                EventFamily.MapObject => $"Zone {row.Values["ZoneId"]} · object {row.Values["Index"]}",
                EventFamily.NpcTrigger => $"NPC type {row.Values["NpcType"]} · trap {row.Values["NpcId"]} → {row.Values["TriggerNum"]}",
                _ => "Castle " + key.Id
            };
            return new EventDocumentSummary(key, label, row.Values);
        }).ToArray();
        return new(summaries, checked((int)total), page, size);
    }

    public Task<int> NextIdAsync(EventTable table, int? zone = null, IReadOnlyList<int>? reserved = null) => SuggestIdAsync(table, zone, reserved?.ToHashSet());
    async Task<int> SuggestIdAsync(EventTable table, int? zone, HashSet<int>? reserved)
    {
        Require(await CheckCompatibilityAsync(), [table]);
        if (table == EventTable.SiegeWarfare)
            throw new InvalidOperationException("Creating siege instances is not supported.");
        if (table == EventTable.GameEvents && zone is not (>= 0 and <= 255) || table != EventTable.GameEvents && zone != null)
            throw new InvalidOperationException("Provide the correct key context.");
        await using var db = Open();
        await db.Database.OpenConnectionAsync();
        var field = EventKeys.Fields(table)[^1];
        var where = zone != null ? " WHERE " + EventTableStore.Column(db, table, "ZoneNum") + " = {0}" : "";
        await using var command = EventTableStore.Command(db, "SELECT " + EventTableStore.Column(db, table, field) + " FROM " + EventTableStore.Table(db, table) + where + " ORDER BY " + EventTableStore.Column(db, table, field), zone != null ? [zone.Value] : []);
        await using var reader = await command.ExecuteReaderAsync();
        long next = table == EventTable.GameEvents ? 2 : 1;
        while (next <= int.MaxValue && reserved?.Contains((int)next) == true)
            next++;
        var max = EventSchema.Catalog().Tables.Single(t => t.Table == table).Fields.Single(f => f.Field.Name == field).Field.Maximum ?? int.MaxValue;
        while (await reader.ReadAsync())
        {
            var id = Convert.ToInt64(reader.GetValue(0));
            if (id < next)
                continue;
            if (id > next)
                break;
            next++;
            while (next <= int.MaxValue && reserved?.Contains((int)next) == true)
                next++;
        }

        if (next > max)
            throw new InvalidOperationException("No unused IDs remain in this key range.");
        return (int)next;
    }

    public async Task<IReadOnlyList<string>> DiagnosticsAsync(EventDocumentKey key)
    {
        var document = await GetAsync(key);
        if (document == null)
            return[];
        var warnings = new List<string>();
        var schedules = document.Rows.Where(r => r.Values.ContainsKey("Hour")).ToArray();
        for (var i = 0; i < schedules.Length; i++)
            for (var j = i + 1; j < schedules.Length; j++)
            {
                var a = schedules[i];
                var b = schedules[j];
                if (a.Values["Hour"]?.ToString() == b.Values["Hour"]?.ToString() && a.Values["Minute"]?.ToString() == b.Values["Minute"]?.ToString() && (a.Values["Day"] == null || b.Values["Day"] == null || a.Values["Day"]!.ToString() == b.Values["Day"]!.ToString()))
                    warnings.Add($"Overlapping schedules {a.Key.Id} and {b.Key.Id}. Server matching / active-event rules may prevent separate runs.");
            }

        await using var db = Open();
        if (key.Family == EventFamily.NpcTrigger)
        {
            var row = document.Rows[0];
            var type = FieldValueParser.ReadInt32(row.Values["NpcType"]);
            var trap = FieldValueParser.ReadInt32(row.Values["NpcId"]);
            if (await db.EventTriggers.CountAsync(r => r.NpcType == type && r.NpcId == trap) > 1)
                warnings.Add("Duplicate NPC type / trap-number matches. The server selects the first matching trigger; rows are not independent actions.");
        }

        if (key.Family == EventFamily.MapObject)
        {
            var row = document.Rows[0];
            var zone = FieldValueParser.ReadInt32(row.Values["ZoneId"]);
            var index = FieldValueParser.ReadInt32(row.Values["Index"]);
            if (await db.MapObjects.CountAsync(r => r.ZoneId == zone && r.Index == index) > 1)
                warnings.Add("Duplicate zone / object indices. The map loader keeps the first object at that index.");
        }

        return warnings.Take(12).ToArray();
    }
}
