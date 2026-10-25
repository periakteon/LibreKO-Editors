using System.Data;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.DataHost.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Editors.DataHost.World;
public sealed partial class WorldDatabase
{
    WorldJournal History => new(HistoryRoot);
    public Task<IReadOnlyList<OperationLink>> HistoryAsync() => Task.FromResult(History.List());
    public Task<WorldHistoryEntry> HistoryEntryAsync(Guid id)
    { var entry = History.Read(id); return Task.FromResult(new WorldHistoryEntry(entry.Change, entry.After, Path.GetFullPath(HistoryRoot))); }
    public async Task<RecoveryDisposition> ReconcileAsync(Guid id)
    {
        var history = History; using var lease = history.Acquire();
        var pending = history.Pending().SingleOrDefault(c => c.OperationId == id);
        if (pending == null) { _ = history.Read(id); return RecoveryDisposition.Applied; }
        Require(await CheckCompatibilityAsync(), pending.Rows.Select(r => r.Key.Table));
        await using var db = Open(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var current = new List<WorldRowSnapshot>();
        foreach (var row in pending.Rows.OrderBy(r => WorldKeys.Order(r.Key), StringComparer.Ordinal))
            if (await ReadKeyAsync(db, row.Key, true) is { } snapshot) current.Add(snapshot);
        var disposition = WorldRecovery.Resolve(pending, current);
        if (disposition == RecoveryDisposition.Applied) history.Commit(pending, current);
        else if (disposition == RecoveryDisposition.NotApplied) history.Abort(id);
        await tx.CommitAsync(); return disposition;
    }
    public async Task<WorldSaveResult> ReplayAsync(Guid id, bool reverse)
    {
        WorldChange change;
        try
        {
            var entry = History.Read(id);
            await using var db = Open();
            if (reverse) await MappedSchemaGuard.RequireAsync(connectionString, db, entry.Change.Rows.Where(r => r.After == null).Select(r => (MappedTable.World(r.Key.Table), false, true)));
            var current = new List<WorldRowSnapshot>();
            foreach (var row in entry.Change.Rows) if (await GetAsync(row.Key) is { } live) current.Add(live);
            change = WorldReplay.Prepare(entry.Change, current, reverse);
            var memberships = new List<WorldMembership>();
            foreach (var membership in entry.Change.Memberships) memberships.Add(await MembershipAsync(membership.Owner, membership.Relation));
            change = change with { Memberships = memberships };
        }
        catch (InvalidOperationException e) { return new(SaveStatus.Conflict, [], Message: e.Message); }
        catch (Exception e) when (e is IOException or InvalidDataException) { return new(SaveStatus.Invalid, [], Message: "World history is unavailable in this database scope: " + e.Message); }
        return await SaveAsync(change);
    }
}
