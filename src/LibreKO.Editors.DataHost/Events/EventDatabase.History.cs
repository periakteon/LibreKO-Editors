using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Events;

namespace LibreKO.Editors.DataHost.Events;
public sealed partial class EventDatabase
{
    EventJournal History => new(HistoryRoot);

    public Task<IReadOnlyList<EventHistorySummary>> HistoryAsync() => Task.FromResult(History.List());
    public Task<EventHistoryEntry> HistoryEntryAsync(Guid id) => Task.FromResult(History.Read(id));
    public async Task<RecoveryDisposition> ReconcileAsync(Guid id)
    {
        var history = History;
        var change = history.Pending().SingleOrDefault(c => c.OperationId == id);
        if (change == null)
        {
            _ = history.Read(id);
            return RecoveryDisposition.Applied;
        }

        var current = await GetAsync(change.Before.Key);
        var disposition = EventRecovery.Resolve(change, current);
        if (disposition == RecoveryDisposition.Applied)
            history.Commit(change, current);
        else if (disposition == RecoveryDisposition.NotApplied)
            history.Abort(id);
        return disposition;
    }

    public async Task<EventSaveResult> ReplayAsync(Guid id, bool reverse)
    {
        var entry = History.Read(id);
        if (reverse && entry.Change.Rows.Any(r => r.After == null) && await OpaqueColumnsAsync(entry.Change.Rows.Where(r => r.After == null).Select(r => r.Key.Table))is { Count: > 0 })
            return new(SaveStatus.Invalid, Message: "Restoring deleted rows is disabled when custom columns cannot be preserved exactly.");
        var current = await GetAsync(entry.Change.Before.Key);
        EventChange change;
        try
        {
            change = EventReplay.Prepare(entry, current, reverse);
        }
        catch (InvalidOperationException e)
        {
            return new(SaveStatus.Conflict, current, Message: e.Message);
        }

        return await SaveAsync(change);
    }
}
