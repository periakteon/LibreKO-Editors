using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Events;
using LibreKO.Editors.DataHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LibreKO.Editors.DataHost.Events;
public sealed partial class EventDatabase
{
    static object? TypedValue(EventTable table, string field, JsonNode? value)
    {
        return MappedTable.Event(table).TypedValue(field, value);
    }

    static EventChange Normalize(EventChange change) => change with
    {
        Rows = change.Rows.Select(row => row.After == null ? row : row with { After = new JsonObject(row.After.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, JsonSerializer.SerializeToNode(TypedValue(row.Key.Table, p.Key, p.Value))))) }).ToArray()
    };
    static List<ValidationIssue> ValidateDocument(EventDocumentKey key, Dictionary<EventRowKey, JsonObject> proposed)
    {
        var issues = new List<ValidationIssue>();
        var root = EventSchema.TablesFor(key)[0];
        if (key.Family is EventFamily.CollectionRace or EventFamily.Lottery && proposed.Count > 0 && !proposed.Keys.Any(k => k.Table == root))
            issues.Add(new("Rows", "Remove every reviewed child together with its definition."));
        foreach (var group in proposed.Where(p => p.Key.Table == EventTable.CollectionRaceObjectives).GroupBy(p => p.Value["Ordinal"]?.ToString()))
            if (group.Count() > 1)
                issues.Add(new("Ordinal", "Collection objective order must be unique within the race."));
        return issues;
    }

    static async Task<List<ValidationIssue>> ValidateReferencesAsync(AppDbContext db, EventChange change, Dictionary<EventRowKey, JsonObject> proposed)
    {
        var issues = new List<ValidationIssue>();
        foreach (var row in change.Rows.Where(r => r.After != null))
            foreach (var field in row.After!)
            {
                var kind = EventRules.ReferenceFor(row.Key.Table, field.Key, row.After!);
                if (kind == null || field.Value == null)
                    continue;
                var oldKind = row.Before == null ? null : EventRules.ReferenceFor(row.Key.Table, field.Key, row.Before.Values);
                if (row.Before != null && oldKind == kind && JsonNode.DeepEquals(field.Value, row.Before.Values[field.Key]))
                    continue;
                var id = FieldValueParser.ReadInt32(field.Value);
                if (id == 0 && kind != "zones")
                    continue;
                if (kind == "event-races" && proposed.Keys.Any(k => k.Table == EventTable.CollectionRaces && k.Id == id) || kind == "event-lotteries" && proposed.Keys.Any(k => k.Table == EventTable.LotteryEvents && k.Id == id))
                    continue;
                if (kind == "items" && (id == InventoryConstants.ItemGold && (row.Key.Table == EventTable.LotteryEvents || field.Key == "ItemId") || field.Key == "ItemId" && row.Key.Table is EventTable.LotteryRewards or EventTable.CollectionRaceRewards && id is InventoryConstants.ItemExperience or InventoryConstants.ItemLadderPoint))
                    continue;
                var(type, keyField) = kind switch
                {
                    "items" => (typeof(ItemData), "Num"),
                    "npcs" => (typeof(NpcData), "Id"),
                    "zones" => (typeof(ZoneInfoData), "ZoneNo"),
                    "clans" => (typeof(KnightsEntity), "Id"),
                    "event-races" => (typeof(CollectionRaceData), "Id"),
                    "event-lotteries" => (typeof(LotteryEventData), "Id"),
                    _ => throw new InvalidOperationException("Unsupported event reference.")};
                var entity = db.Model.FindEntityType(type)!;
                var store = StoreObjectIdentifier.Table(entity.GetTableName()!, null);
                var column = entity.FindProperty(keyField)!.GetColumnName(store)!;
                await using var cmd = EventTableStore.Command(db, "SELECT " + EventTableStore.Quote(column) + " FROM " + EventTableStore.Quote(entity.GetTableName()!) + " WHERE " + EventTableStore.Quote(column) + " = {0} FOR UPDATE", [id]);
                if (await cmd.ExecuteScalarAsync() == null)
                    issues.Add(new(row.Key.Table + "." + field.Key, $"Unresolved {kind} ID {id}. Choose an existing reference."));
            }

        return issues;
    }

    static Task<bool> DeletionReferencesSafeAsync(AppDbContext db, EventChange change) =>
        MappedForeignKeys.SafeAsync(db, change.Rows.Select(r =>
        {
            var fields = EventKeys.Fields(r.Key.Table);
            var filter = RowFilter(db, r.Key);
            var keys = fields.Select((f, i) => (f, filter.Args[i])).ToDictionary(x => x.f, x => (object?)x.Item2);
            return new MappedMutation(MappedTable.Event(r.Key.Table), keys, r.Before?.Values, r.After);
        }).ToArray());
}
