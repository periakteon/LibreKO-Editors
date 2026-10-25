using System.Data;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.DataHost.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace LibreKO.Editors.DataHost.World;
public sealed partial class WorldDatabase
{
    public async Task<WorldSaveResult> SaveAsync(WorldChange change)
    {
        // Reject the complete reviewed request; splitting it would break its atomic promise.
        if (JsonSerializer.SerializeToUtf8Bytes(change, JsonConfig.Options).Length > 4 * 1024 * 1024)
            return new(SaveStatus.Invalid, [], Message: "The reviewed world batch exceeds the 4 MiB protocol limit. Reduce the batch; it cannot be split automatically.");
        var issues = WorldRules.Validate(change, Catalog()).ToList();
        if (change.OperationId == Guid.Empty) issues.Add(new("Operation", "An operation identity is required."));
        if (change.Rows.Count == 0) issues.Add(new("Rows", "No world changes to save."));
        if (issues.Count > 0) return new(SaveStatus.Invalid, [], issues);
        bool prepared = false, retain = false, committing = false;
        FileStream? lease = null;
        try
        {
            if (worldJournal is WorldJournal durable)
            {
                lease = durable.Acquire();
                if (durable.Pending().Count > 0) return new(SaveStatus.Invalid, [], Message: "Reconcile pending world history before another save.");
            }
            await using var db = Open();
            await MappedSchemaGuard.RequireAsync(connectionString, db, change.Rows.Select(r => (MappedTable.World(r.Key.Table), r.Before == null, r.After == null)));
            change = change with { Rows = change.Rows.Select(r => r.After == null ? r : r with { After = MappedTable.World(r.Key.Table).Normalize(r.After) }).ToArray() };
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var current = new Dictionary<WorldKey, WorldRowSnapshot>();
            foreach (var row in change.Rows.OrderBy(r => WorldKeys.Order(r.Key), StringComparer.Ordinal))
            {
                var values = await MappedRowStore.ReadAsync(db, MappedTable.World(row.Key.Table), KeyValues(row.Key), true);
                if (values != null) current[row.Key] = new(row.Key, values, RecordFingerprint.Compute(values));
                var live = current.GetValueOrDefault(row.Key);
                if (row.Before == null ? live != null : live == null || live.Version != row.Before.Version || RecordFingerprint.Compute(row.Before.Values) != live.Version)
                    return new(SaveStatus.Conflict, current.Values.ToArray(), Message: "A world row changed or its original snapshot is inconsistent. Reload before saving.");
            }
            foreach (var membership in change.Memberships.OrderBy(x => WorldKeys.Order(x.Owner) + x.Relation, StringComparer.Ordinal))
            {
                var live = await WorldDependencies.CaptureAsync(db, membership.Owner, membership.Relation, true);
                if (!live.BeforeKeys.ToHashSet().SetEquals(membership.BeforeKeys))
                    return new(SaveStatus.Conflict, current.Values.ToArray(), Message: "Related world membership changed. Reload and review the complete dependency set.");
            }
            issues.AddRange(await ValidateReferencesAsync(db, change));
            if (issues.Count > 0) return new(SaveStatus.Invalid, [], issues);
            if (!await DeletionReferencesSafeAsync(db, change))
                return new(SaveStatus.Conflict, [], Message: "Other records reference rows being removed. Review the affected records; no implicit cascade is performed.");
            worldJournal.Prepare(change); prepared = true;
            // Dependencies, rather than numeric table IDs, determine insertion/removal order.
            foreach (var row in change.Rows.Where(r => r.After != null).OrderBy(r => WriteOrder(r.Key.Table)).ThenBy(r => WorldKeys.Order(r.Key), StringComparer.Ordinal))
                await MappedRowStore.ApplyAsync(db, MappedTable.World(row.Key.Table), row);
            foreach (var row in change.Rows.Where(r => r.After == null).OrderByDescending(r => WriteOrder(r.Key.Table)).ThenBy(r => WorldKeys.Order(r.Key), StringComparer.Ordinal))
                await MappedRowStore.ApplyAsync(db, MappedTable.World(row.Key.Table), row);
            var after = new List<WorldRowSnapshot>();
            foreach (var row in change.Rows.Where(r => r.After != null))
                after.Add((await ReadKeyAsync(db, row.Key)) ?? throw new InvalidOperationException("Saved world row is missing."));
            committing = true; await tx.CommitAsync(); retain = true;
            try { worldJournal.Commit(change, after); }
            catch { return new(SaveStatus.UnknownCommit, after, Message: "Database committed; local history finalization was interrupted. Reconcile before retrying."); }
            return new(SaveStatus.Saved, after, Message: "Saved to database. Server caches require the appropriate maintenance reload or restart; forced reseeding can replace these edits.");
        }
        catch (Exception e) when (committing)
        { retain = true; return new(SaveStatus.UnknownCommit, [], Message: "The database commit outcome is uncertain. Reconnect and reconcile pending world history. " + e.GetType().Name); }
        catch (MySqlException e) when (e.Number is 1062 or 1205 or 1213 or 1451 or 1452)
        { return new(SaveStatus.Conflict, [], Message: "A concurrent edit or related record rejected the world transaction. Reload and review before retrying."); }
        catch (Exception e) { return new(SaveStatus.Invalid, [], Message: "World save was rolled back: " + e.Message); }
        finally
        {
            try { if (prepared && !retain) worldJournal.Abort(change.OperationId); }
            catch (IOException) { /* The retained pending receipt can be reconciled against rolled-back rows. */ }
            lease?.Dispose();
        }
    }
    static int WriteOrder(WorldTable table) => table switch
    { WorldTable.ZoneInfos => 0, WorldTable.MakeItemGroups => 1, WorldTable.NpcItems => 2, WorldTable.Npcs => 3,
        WorldTable.StartPositions => 4, WorldTable.NpcPositions or WorldTable.ItemExchanges or WorldTable.MonsterSummons => 5, _ => 6 };
}
