using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.World;
public static class WorldRecovery
{
    public static IEnumerable<string> ChangedFields(WorldRowChange row) => row.Before == null || row.After == null ? [] : row.After.Where(p => !JsonNode.DeepEquals(row.Before.Values[p.Key], p.Value)).Select(p => p.Key);
    public static bool Matches(JsonObject live, JsonObject expected, IEnumerable<string>? fields = null) => (fields ?? expected.Select(p => p.Key)).All(f => JsonNode.DeepEquals(live[f], expected[f]));
    public static RecoveryDisposition Resolve(WorldChange change, IReadOnlyList<WorldRowSnapshot> current)
    {
        var rows = current.ToDictionary(r => r.Key); bool before = true, after = true;
        foreach (var mutation in change.Rows)
        {
            var live = rows.GetValueOrDefault(mutation.Key);
            var fields = mutation.Before != null && mutation.After != null ? ChangedFields(mutation) : null;
            before &= mutation.Before == null ? live == null : live != null && Matches(live.Values, mutation.Before.Values, fields);
            after &= mutation.After == null ? live == null : live != null && Matches(live.Values, mutation.After, fields);
        }
        return after ? RecoveryDisposition.Applied : before ? RecoveryDisposition.NotApplied : RecoveryDisposition.Conflict;
    }
}
