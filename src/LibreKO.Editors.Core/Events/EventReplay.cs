using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Events;
public static class EventReplay
{
    public static EventChange Prepare(EventHistoryEntry entry, EventDocumentSnapshot? current, bool reverse)
    {
        var original = entry.Change;
        var live = (current?.Rows ?? []).ToDictionary(r => r.Key);
        var changes = new List<EventRowChange>();
        if (reverse && original.Before.Version == "missing" && !live.Keys.ToHashSet().SetEquals(original.Rows.Select(r => r.Key)))
            throw new InvalidOperationException("Later rows were added to this event. Reversal cannot remove them.");
        foreach (var mutation in original.Rows)
        {
            var expected = reverse ? mutation.After : mutation.Before?.Values;
            var target = reverse ? mutation.Before?.Values : mutation.After;
            var now = live.GetValueOrDefault(mutation.Key);
            var fields = mutation.Before != null && mutation.After != null ? EventRecovery.ChangedFields(mutation).ToArray() : null;
            if (expected == null ? now != null : now == null || !EventRecovery.Matches(now.Values, expected, fields))
                throw new InvalidOperationException("Affected fields or rows changed since this history operation. Reload and review.");
            JsonObject? after = target == null ? null : (JsonObject)(now?.Values ?? target).DeepClone();
            if (after != null && fields != null)
                foreach (var field in fields)
                    after[field] = target![field]?.DeepClone();
            changes.Add(new(mutation.Key, now, after));
        }

        return new(Guid.NewGuid(), current ?? new(original.Before.Key, [], "missing"), changes);
    }
}
