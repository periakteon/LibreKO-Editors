using System.Data;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Events;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.DataHost.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace LibreKO.Editors.DataHost.Events;
public sealed partial class EventDatabase
{
    public async Task<EventSaveResult> SaveAsync(EventChange change)
    {
        var issues = EventRules.Validate(change, Catalog()).ToList();
        if (change.OperationId == Guid.Empty)
            issues.Add(new("Operation", "An operation identity is required."));
        if (change.Rows.Count == 0)
            issues.Add(new("Rows", "No changes to save."));
        if (change.Before.Key.Family == EventFamily.Temple && change.Before.Version == "missing")
            issues.Add(new("Identity", "Temple gameplay modes cannot be created."));
        if (issues.Count > 0)
            return new(SaveStatus.Invalid, Issues: issues);
        try
        {
            Require(await CheckCompatibilityAsync(), EventSchema.TablesFor(change.Before.Key).Concat(change.Rows.Select(r => r.Key.Table)).Distinct(), true);
            await using var schemaDb = Open();
            await MappedSchemaGuard.RequireAsync(connectionString, schemaDb, change.Rows.Select(r => (MappedTable.Event(r.Key.Table), r.Before == null, r.After == null)));
            var opaque = await OpaqueColumnsAsync(change.Rows.Where(r => r.After == null).Select(r => r.Key.Table));
            if (opaque.Count > 0)
                return new(SaveStatus.Invalid, Message: "Removal is disabled because custom columns cannot be preserved exactly in history: " + string.Join("; ", opaque) + ". Mapped-field updates remain supported.");
            change = Normalize(change);
        }
        catch (Exception e)
        {
            return new(SaveStatus.Invalid, Message: e.Message);
        }

        if (eventJournal is EventJournal durable && durable.Pending().Count > 0)
            return new(SaveStatus.Invalid, Message: "Reconcile pending event history before another save.");
        bool prepared = false, retain = false, committing = false;
        try
        {
            await using var db = Open();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            var current = await LoadDocumentAsync(db, change.Before.Key, true);
            if ((current?.Version ?? "missing") != change.Before.Version)
                return new(SaveStatus.Conflict, current, Message: "The event or its related rows changed. Reload before saving.");
            var currentRows = (current?.Rows ?? []).ToDictionary(r => r.Key);
            if (change.Before.Rows.Count != currentRows.Count || change.Before.Rows.Any(r => !currentRows.TryGetValue(r.Key, out var live) || r.Version != live.Version || RecordFingerprint.Compute(EventSchema.VersionValues(r.Key.Table, r.Values)) != live.Version))
                return new(SaveStatus.Conflict, current, Message: "The original document snapshot is incomplete or inconsistent.");
            foreach (var row in change.Rows)
            {
                if (row.Before == null)
                {
                    var filter = RowFilter(db, row.Key);
                    var keys = EventKeys.Fields(row.Key.Table).Select((f, i) => (f, filter.Args[i])).ToDictionary(x => x.f, x => (object?)x.Item2);
                    if (await MappedRowStore.ReadAsync(db, MappedTable.Event(row.Key.Table), keys, true) != null)
                        return new(SaveStatus.Conflict, current, Message: "The proposed row ID is already in use.");
                }
                else if (!currentRows.TryGetValue(row.Key, out var live) || row.Before.Version != live.Version || RecordFingerprint.Compute(EventSchema.VersionValues(row.Key.Table, row.Before.Values)) != live.Version)
                    return new(SaveStatus.Conflict, current, Message: "A row snapshot changed or does not belong to the original document.");
            }

            var proposed = currentRows.ToDictionary(p => p.Key, p => (JsonObject)p.Value.Values.DeepClone());
            foreach (var row in change.Rows)
            {
                if (row.After == null)
                    proposed.Remove(row.Key);
                else
                    proposed[row.Key] = row.After;
            }

            issues.AddRange(ValidateDocument(change.Before.Key, proposed));
            issues.AddRange(await ValidateReferencesAsync(db, change, proposed));
            if (issues.Count > 0)
                return new(SaveStatus.Invalid, Issues: issues);
            if (!await DeletionReferencesSafeAsync(db, change))
                return new(SaveStatus.Conflict, Message: "Other records reference rows being removed. Review their removal first.");
            // A failed prepare must never permit database writes.
            eventJournal.Prepare(change);
            prepared = true;
            foreach (var row in change.Rows.Where(r => r.After == null).OrderByDescending(r => (int)r.Key.Table))
                await ApplyRowAsync(db, row);
            await ApplyObjectiveOrderAsync(db, change, currentRows);
            // Roots precede children. Each UPDATE assigns changed configuration only.
            foreach (var row in change.Rows.Where(r => r.After != null).OrderBy(r => (int)r.Key.Table))
                await ApplyRowAsync(db, row);
            var after = await LoadDocumentAsync(db, change.Before.Key);
            committing = true;
            await tx.CommitAsync();
            retain = true;
            try
            {
                eventJournal.Commit(change, after);
            }
            catch
            {
                return new(SaveStatus.UnknownCommit, after, Message: "Database committed; local history finalization was interrupted. Reconcile the pending operation before retrying.");
            }

            return new(SaveStatus.Saved, after, Message: "Saved to database. Server caches require the appropriate maintenance reload or restart; forced reseeding can replace these edits.");
        }
        catch (Exception e)when (committing)
        {
            retain = true;
            return new(SaveStatus.UnknownCommit, Message: "The commit outcome is uncertain. Reconnect and reconcile pending event history before retrying. " + e.GetType().Name);
        }
        catch (MySqlException e)when (e.Number is 1062 or 1205 or 1213 or 1451 or 1452)
        {
            return new(SaveStatus.Conflict, Message: "A concurrent edit or related record rejected the transaction. Reload and review before retrying.");
        }
        catch (Exception e)
        {
            return new(SaveStatus.Invalid, Message: "Event save was rolled back: " + e.Message);
        }
        finally
        {
            if (prepared && !retain)
                eventJournal.Abort(change.OperationId);
        }
    }

    static async Task ApplyObjectiveOrderAsync(AppDbContext db, EventChange change, Dictionary<EventRowKey, EventRowSnapshot> current)
    {
        if (change.Before.Key.Family != EventFamily.CollectionRace)
            return;
        var deleted = change.Rows.Where(r => r.After == null).Select(r => r.Key).ToHashSet();
        var positions = current.Values.Where(r => r.Key.Table == EventTable.CollectionRaceObjectives && !deleted.Contains(r.Key)).ToDictionary(r => r.Key, r => FieldValueParser.ReadInt32(r.Values["Ordinal"]));
        var pending = change.Rows.Where(r => r.Key.Table == EventTable.CollectionRaceObjectives && r.Before != null && r.After != null && !JsonNode.DeepEquals(r.Before.Values["Ordinal"], r.After["Ordinal"])).ToDictionary(r => r.Key, r => FieldValueParser.ReadInt32(r.After!["Ordinal"]));
        while (pending.Count > 0)
        {
            var move = pending.FirstOrDefault(p => !positions.Values.Contains(p.Value));
            int destination;
            if (move.Key != null)
                destination = move.Value;
            else
            {
                var spare = Enumerable.Range(0, 256).Where(n => !positions.Values.Contains(n)).Cast<int?>().FirstOrDefault();
                if (spare == null)
                    throw new InvalidOperationException("Every objective order value is occupied. Free an order value before rearranging this full race.");
                move = pending.First();
                destination = spare.Value;
            }

            await using var command = EventTableStore.Command(db, "UPDATE " + EventTableStore.Table(db, EventTable.CollectionRaceObjectives) + " SET " + EventTableStore.Column(db, EventTable.CollectionRaceObjectives, "Ordinal") + "={0} WHERE " + EventTableStore.Column(db, EventTable.CollectionRaceObjectives, "Id") + "={1} AND " + EventTableStore.Column(db, EventTable.CollectionRaceObjectives, "RaceId") + "={2}", [destination, move.Key.Id, change.Before.Key.Id]);
            if (await command.ExecuteNonQueryAsync() != 1)
                throw new InvalidOperationException("An objective changed during reorder.");
            positions[move.Key] = destination;
            if (destination == move.Value)
                pending.Remove(move.Key);
        }
    }

    static (string Sql, object[] Args) RowFilter(AppDbContext db, EventRowKey key)
    {
        var fields = EventKeys.Fields(key.Table);
        return ("WHERE " + string.Join(" AND ", fields.Select((f, i) => EventTableStore.Column(db, key.Table, f) + " = {" + i + "}")), key.Zone is { } zone ? [zone, key.Id] : [key.Id]);
    }

    static async Task ApplyRowAsync(AppDbContext db, EventRowChange row)
    {
        var fields = EventKeys.Fields(row.Key.Table);
        var filter = RowFilter(db, row.Key);
        var keys = fields.Select((f, i) => (f, filter.Args[i])).ToDictionary(x => x.f, x => (object?)x.Item2);
        await MappedRowStore.ApplyAsync(db, MappedTable.Event(row.Key.Table), keys, row.Before?.Values, row.After);
    }

    public async Task<EventChange> PlanDuplicateAsync(EventDocumentKey source, int newId, int? zone = null)
    {
        if (source.Family is EventFamily.Temple or EventFamily.CastleSiege)
            throw new InvalidOperationException("This family supports existing gameplay definitions only.");
        var original = await GetAsync(source) ?? throw new InvalidOperationException("Source event no longer exists.");
        var key = new EventDocumentKey(source.Family, newId, zone);
        if (newId <= 0 || key.Family == EventFamily.MapEvent && zone is not (>= 0 and <= 255) || key.Family != EventFamily.MapEvent && zone != null)
            throw new InvalidOperationException("Choose a complete unused positive key.");
        if (await GetAsync(key) != null)
            throw new InvalidOperationException("Target event ID is already used.");
        var rows = new List<EventRowChange>();
        var reserved = new Dictionary<EventTable, HashSet<int>>();
        foreach (var row in original.Rows)
        {
            var values = (JsonObject)row.Values.DeepClone();
            var root = row.Key.Table == EventSchema.TablesFor(source)[0];
            if (!reserved.TryGetValue(row.Key.Table, out var used))
                reserved[row.Key.Table] = used = [];
            var id = root ? newId : await SuggestIdAsync(row.Key.Table, null, used);
            used.Add(id);
            values[EventKeys.Fields(row.Key.Table)[^1]] = id;
            if (key.Family == EventFamily.MapEvent)
                values["ZoneNum"] = zone!.Value;
            if (values.ContainsKey("RaceId"))
                values["RaceId"] = newId;
            if (values.ContainsKey("LotteryId"))
                values["LotteryId"] = newId;
            rows.Add(new(EventKeys.From(row.Key.Table, values), null, values));
        }

        var change = new EventChange(Guid.NewGuid(), new(key, [], "missing"), rows);
        var issues = EventRules.Validate(change, Catalog());
        if (issues.Count > 0)
            throw new InvalidOperationException(string.Join("; ", issues.Select(i => i.Field + ": " + i.Message)));
        return change;
    }
}
