using System.Globalization;
using System.Text.Json.Nodes;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.References;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.DataHost.Persistence;
using LibreKO.Editors.DataHost.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LibreKO.Editors.DataHost.World;
public sealed partial class WorldDatabase
{
    static async Task<List<ValidationIssue>> ValidateReferencesAsync(AppDbContext db, WorldChange change)
    {
        var issues = new List<ValidationIssue>();
        foreach (var row in change.Rows.Where(r => r.After != null))
        {
            foreach (var field in row.After!)
            {
                var reference = WorldRules.ReferenceFor(row.Key.Table, field.Key, row.After);
                if (reference == null || row.Before != null && reference == WorldRules.ReferenceFor(row.Key.Table, field.Key, row.Before.Values)) continue;
                if (reference.Kind is "models" or "shops") continue; // File-backed diagnostics are independent of DB referential validity.
                var required = reference.Kind == "zones";
                if (reference.Id == 0 && !required) continue;
                if (reference.Kind == "items" && ReferenceRules.Special(reference) != null) continue;
                if (!await ReferenceExistsAsync(db, change, reference)) issues.Add(new(row.Key.Table + "." + field.Key, "Unresolved " + reference.Kind + " ID " + reference.Id + (reference.IsMonster is bool monster ? monster ? " (monster)" : " (NPC)" : "") + ". Choose an existing reference."));
            }
            if (row.Key.Table == WorldTable.MakeItemGroups && (row.Before == null || !JsonNode.DeepEquals(row.Before.Values["Items"], row.After["Items"])))
            {
                foreach (var token in (row.After["Items"]?.ToString() ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                    if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) || id <= 0 || !await ReferenceExistsAsync(db, change, new("items", id)))
                        issues.Add(new("MakeItemGroups.Items", "Unresolved item in ordered pool: " + token));
            }
        }
        return issues;
    }

    static async Task<bool> ReferenceExistsAsync(AppDbContext db, WorldChange change, ReferenceIdentity reference)
    {
        var table = reference.Kind switch { "zones" => WorldTable.ZoneInfos, "npcs" => WorldTable.Npcs, "drop-groups" => WorldTable.NpcItems, "drop-pools" => WorldTable.MakeItemGroups, _ => (WorldTable?)null };
        if (table != null)
        {
            var scopes = table == WorldTable.Npcs && reference.IsMonster == null ? new bool?[] { true, false } : new[] { reference.IsMonster };
            foreach (var scope in scopes)
            {
                var key = new WorldKey(table.Value, reference.Id, IsMonster: scope);
                var mutation = change.Rows.SingleOrDefault(r => r.Key == key);
                if (mutation != null) { if (mutation.After != null) return true; continue; }
                if (await MappedRowStore.ReadAsync(db, MappedTable.World(table.Value), KeyValues(key), true) != null) return true;
            }
            return false;
        }
        var (type, field) = reference.Kind switch { "items" => (typeof(ItemData), "Num"), "magic" => (typeof(MagicData), "Id"), _ => throw new InvalidOperationException("Unsupported world DB reference: " + reference.Kind) };
        var entity = db.Model.FindEntityType(type)!; var name = entity.GetTableName()!;
        var column = entity.FindProperty(field)!.GetColumnName(StoreObjectIdentifier.Table(name, null))!;
        await using var cmd = MappedRowStore.Command(db, "SELECT " + Quote(column) + " FROM " + Quote(name) + " WHERE " + Quote(column) + "=@p0 FOR UPDATE", [reference.Id]);
        return await cmd.ExecuteScalarAsync() != null;
    }

    static bool RefersTo(WorldKey owner, WorldTable child, JsonObject values)
    {
        foreach (var field in values)
        {
            var reference = WorldRules.ReferenceFor(child, field.Key, values);
            if (reference?.Id != owner.Id) continue;
            if (owner.Table == WorldTable.ZoneInfos && reference.Kind == "zones" || owner.Table == WorldTable.Npcs && reference.Kind == "npcs" && (reference.IsMonster == null || reference.IsMonster == owner.IsMonster) ||
                owner.Table == WorldTable.NpcItems && reference.Kind == "drop-groups" && reference.IsMonster == owner.IsMonster || owner.Table == WorldTable.MakeItemGroups && reference.Kind == "drop-pools") return true;
        }
        return false;
    }
    static async Task<bool> DeletionReferencesSafeAsync(AppDbContext db, WorldChange change)
    {
        foreach (var deleted in change.Rows.Where(r => r.After == null))
        {
            foreach (var relation in WorldRules.Relations(deleted.Key.Table))
            {
                var dependencies = await WorldDependencies.CaptureAsync(db, deleted.Key, relation, true);
                foreach (var key in dependencies.BeforeKeys)
                {
                    var planned = change.Rows.SingleOrDefault(r => r.Key == key);
                    if (planned == null || planned.After != null && RefersTo(deleted.Key, key.Table, planned.After)) return false;
                }
            }
            var additional = deleted.Key.Table switch
            {
                WorldTable.ZoneInfos => new[] { (WorldTable.StartPositions, "ZoneId"), (WorldTable.Warps, "ZoneId") },
                WorldTable.Npcs => new[] { (WorldTable.MonsterSummons, "Sid") }, _ => []
            };
            foreach (var (table, field) in additional)
                foreach (var dependency in await ReadAsync(db, table, "WHERE w." + Column(db, table, field) + "=@p0 FOR UPDATE", [deleted.Key.Id]))
                {
                    var planned = change.Rows.SingleOrDefault(r => r.Key == dependency.Key);
                    if (planned == null || planned.After != null && RefersTo(deleted.Key, table, planned.After)) return false;
                }
            if (deleted.Key.Table == WorldTable.ZoneInfos)
                foreach (var dependency in await ReadAsync(db, WorldTable.GameEvents, "WHERE w." + Column(db, WorldTable.GameEvents, "Type") + "=1 AND w." + Column(db, WorldTable.GameEvents, "Exec1") + "=@p0 FOR UPDATE", [deleted.Key.Id]))
                {
                    var planned = change.Rows.SingleOrDefault(r => r.Key == dependency.Key);
                    if (planned == null || planned.After != null && RefersTo(deleted.Key, dependency.Key.Table, planned.After)) return false;
                }
            // These event families are outside a WorldChange: never cascade or fabricate a partial EventChange.
            var eventLinks = deleted.Key.Table switch
            {
                WorldTable.Npcs => new[] { (EventTable.ForgottenTempleWaves, "NpcId", false), (EventTable.CollectionRaceObjectives, "TargetId", true) },
                WorldTable.ZoneInfos => new[] { (EventTable.CollectionRaces, "ZoneId", false) }, _ => []
            };
            foreach (var (table, field, objective) in eventLinks)
            {
                var mapped = MappedTable.Event(table); var name = db.Model.FindEntityType(mapped.EntityType)!.GetTableName()!;
                await using var exists = Command(db, "SELECT 1 FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@p0", [name]);
                if (await exists.ExecuteScalarAsync() == null) continue;
                var where = MappedRowStore.Column(db, mapped, field) + "=@p0" + (objective ? " AND " + MappedRowStore.Column(db, mapped, "Kind") + "=0" : "");
                await using var cmd = Command(db, "SELECT 1 FROM " + MappedRowStore.Table(db, mapped) + " WHERE " + where + " FOR UPDATE", [deleted.Key.Id]);
                if (await cmd.ExecuteScalarAsync() != null) return false;
            }
        }
        return await MappedForeignKeys.SafeAsync(db, change.Rows.Select(r => new MappedMutation(MappedTable.World(r.Key.Table), KeyValues(r.Key), r.Before?.Values, r.After)).ToArray());
    }
}
