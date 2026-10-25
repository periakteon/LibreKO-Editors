using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.World;
public static class WorldReplay
{
    public static WorldChange Prepare(WorldChange original, IReadOnlyList<WorldRowSnapshot> current, bool reverse)
    {
        var live = current.ToDictionary(r => r.Key); var changes = new List<WorldRowChange>();
        foreach (var mutation in original.Rows)
        {
            var expected = reverse ? mutation.After : mutation.Before?.Values;
            var target = reverse ? mutation.Before?.Values : mutation.After;
            var now = live.GetValueOrDefault(mutation.Key);
            var fields = mutation.Before != null && mutation.After != null ? WorldRecovery.ChangedFields(mutation).ToArray() : null;
            if (expected == null ? now != null : now == null || !WorldRecovery.Matches(now.Values, expected, fields))
                throw new InvalidOperationException("Affected fields or rows changed since this world operation. Reload and review.");
            JsonObject? after = target == null ? null : (JsonObject)(now?.Values ?? target).DeepClone();
            if (after != null && fields != null) foreach (var field in fields) after[field] = target![field]?.DeepClone();
            changes.Add(new(mutation.Key, now, after));
        }
        return new(Guid.NewGuid(), changes, []);
    }
}
