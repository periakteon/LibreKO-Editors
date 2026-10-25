using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.World;

public sealed class WorldDraft
{
    readonly Dictionary<WorldKey, WorldRowSnapshot> initial;
    readonly WorldMembership[] memberships;
    Dictionary<WorldKey, JsonObject> values;
    readonly Stack<Dictionary<WorldKey, JsonObject>> undo = [], redo = [];

    public WorldDraft(IReadOnlyList<WorldRowSnapshot> rows, IReadOnlyList<WorldMembership>? memberships = null)
    {
        foreach (var row in rows)
            if (WorldKeys.From(row.Key.Table, row.Values) != row.Key) throw new InvalidOperationException("Inconsistent original world row identity.");
        initial = rows.ToDictionary(r => r.Key, r => r with { Values = (JsonObject)r.Values.DeepClone() });
        values = initial.ToDictionary(p => p.Key, p => p.Value.Values);
        this.memberships = (memberships ?? []).Select(x => x with { BeforeKeys = x.BeforeKeys.ToArray() }).ToArray();
    }

    public IReadOnlyList<WorldRowSnapshot> Rows => values.OrderBy(x => WorldKeys.Order(x.Key), StringComparer.Ordinal)
        .Select(x => new WorldRowSnapshot(x.Key, (JsonObject)x.Value.DeepClone(), initial.GetValueOrDefault(x.Key)?.Version ?? "missing")).ToArray();
    public bool IsDirty => values.Count != initial.Count || values.Any(x => !initial.TryGetValue(x.Key, out var before) || !JsonNode.DeepEquals(before.Values, x.Value));

    // Loading a related original is not an edit. It must be present in every undo state
    // so undoing earlier edits cannot turn that existing database row into an insert.
    public void Include(IReadOnlyList<WorldRowSnapshot> rows)
    {
        var additions = rows.Where(r => !initial.ContainsKey(r.Key)).ToArray();
        foreach (var row in rows)
        {
            if (WorldKeys.From(row.Key.Table, row.Values) != row.Key || initial.TryGetValue(row.Key, out var existing) && existing.Version != row.Version || !initial.ContainsKey(row.Key) && values.ContainsKey(row.Key))
                throw new InvalidOperationException("Related row conflicts with an existing original or provisional key.");
        }
        if (additions.Length == 0) return;
        values = new(values);
        foreach (var row in additions)
        {
            var snapshot = row with { Values = (JsonObject)row.Values.DeepClone() }; initial.Add(row.Key, snapshot); values[row.Key] = snapshot.Values;
            foreach (var state in undo.Concat(redo)) state[row.Key] = snapshot.Values;
        }
    }

    public void Set(WorldKey key, string field, JsonNode? value)
    {
        if (!values.TryGetValue(key, out var row)) throw new InvalidOperationException("World row no longer exists in this draft.");
        if (WorldKeys.Fields(key.Table).Contains(field)) throw new InvalidOperationException("Use a new record to change its primary key.");
        if (JsonNode.DeepEquals(row[field], value)) return;
        var next = (JsonObject)row.DeepClone(); next[field] = value?.DeepClone();
        Apply([new(key, new(key, row, "draft"), next)]);
    }

    public void Apply(IReadOnlyList<WorldRowChange> changes)
    {
        if (changes.Select(x => x.Key).Distinct().Count() != changes.Count) throw new InvalidOperationException("Duplicate world draft mutations.");
        var next = new Dictionary<WorldKey, JsonObject>(values);
        foreach (var change in changes)
        {
            WorldKeys.Validate(change.Key);
            if (change.Before == null && change.After == null || change.Before != null && change.Before.Key != change.Key ||
                change.Before == null && next.ContainsKey(change.Key) || change.Before != null && !next.ContainsKey(change.Key))
                throw new InvalidOperationException("Inconsistent, duplicate or missing world draft row.");
            if (change.After == null) next.Remove(change.Key);
            else
            {
                if (WorldKeys.From(change.Key.Table, change.After) != change.Key) throw new InvalidOperationException("World row keys are immutable.");
                next[change.Key] = (JsonObject)change.After.DeepClone();
            }
        }
        if (next.Count == values.Count && next.All(x => values.TryGetValue(x.Key, out var prior) && JsonNode.DeepEquals(prior, x.Value))) return;
        undo.Push(values); redo.Clear(); values = next;
    }

    public void Undo(bool redo)
    {
        var source = redo ? this.redo : undo; var target = redo ? undo : this.redo;
        if (source.Count == 0) return;
        target.Push(values); values = source.Pop();
    }

    public WorldChange Build(Guid id)
    {
        var changes = new List<WorldRowChange>();
        foreach (var key in initial.Keys.Union(values.Keys).OrderBy(WorldKeys.Order, StringComparer.Ordinal))
        {
            var before = initial.GetValueOrDefault(key); var after = values.GetValueOrDefault(key);
            if (before != null && after != null && JsonNode.DeepEquals(before.Values, after)) continue;
            changes.Add(new(key, before == null ? null : before with { Values = (JsonObject)before.Values.DeepClone() }, after == null ? null : (JsonObject)after.DeepClone()));
        }
        return new(id, changes, memberships.Select(x => x with { BeforeKeys = x.BeforeKeys.ToArray() }).ToArray());
    }
}
