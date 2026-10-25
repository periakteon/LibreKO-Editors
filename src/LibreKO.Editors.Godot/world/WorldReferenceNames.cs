using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.World;

namespace LibreKO.Editors.Desktop;
public static class WorldReferenceNames
{
    public static async Task<string> DescribeAsync(EditorSession session, IReadOnlyList<WorldRowChange> rows)
    {
        var references = rows.SelectMany(r => new[] { r.Before?.Values, r.After }.Where(v => v != null)
            .SelectMany(v => v!.Select(p => (Field: r.Key.Table + "." + p.Key, Key: WorldRules.ReferenceFor(r.Key.Table, p.Key, v!)))))
            .Where(x => x.Key != null).Distinct().ToArray();
        var names = await session.References.ResolveBatchAsync(references.Select(x => x.Key!).ToArray());
        return string.Join("\n", references.Select(x => x.Field + ": " + EventRewardView.Describe(x.Key!, names)));
    }
}
