using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Events;
public static class EventKeys
{
    public static string[] Fields(EventTable table) => table switch
    {
        EventTable.GameEvents => ["ZoneNum", "EventNum"],
        EventTable.EventTriggers => ["Index"],
        EventTable.SiegeWarfare => ["CastleIndex"],
        _ => ["Id"]
    };
    public static EventRowKey From(EventTable table, JsonObject values) => new(table, FieldValueParser.ReadInt32(values[table switch
    {
        EventTable.GameEvents => "EventNum",
        EventTable.EventTriggers => "Index",
        EventTable.SiegeWarfare => "CastleIndex",
        _ => "Id"
    }]), table == EventTable.GameEvents ? FieldValueParser.ReadInt32(values["ZoneNum"]) : null);
    public static string Order(EventRowKey key) => $"{(int)key.Table:D2}:{key.Zone ?? 0:D10}:{key.Id:D10}";
}

public sealed class EventDraft
{
    readonly Dictionary<EventRowKey, EventRowSnapshot> initial;
    Dictionary<EventRowKey, JsonObject> values;
    readonly Stack<Dictionary<EventRowKey, JsonObject>> undo = new(), redo = new();
    public EventDocumentSnapshot Original { get; }

    static Dictionary<EventRowKey, JsonObject> Clone(Dictionary<EventRowKey, JsonObject> source) => source.ToDictionary(p => p.Key, p => (JsonObject)p.Value.DeepClone());
    public EventDraft(EventDocumentSnapshot snapshot)
    {
        Original = snapshot with
        {
            Rows = snapshot.Rows.Select(r => r with { Values = (JsonObject)r.Values.DeepClone() }).ToArray()
        };
        initial = Original.Rows.ToDictionary(r => r.Key);
        values = initial.ToDictionary(p => p.Key, p => (JsonObject)p.Value.Values.DeepClone());
    }

    public IReadOnlyList<EventRowSnapshot> Rows => values.OrderBy(p => EventKeys.Order(p.Key), StringComparer.Ordinal).Select(p => new EventRowSnapshot(p.Key, p.Value, initial.GetValueOrDefault(p.Key)?.Version ?? "missing")).ToArray();

    void Checkpoint()
    {
        undo.Push(Clone(values));
        redo.Clear();
    }

    public void Set(EventRowKey key, string field, JsonNode? value)
    {
        if (!values.TryGetValue(key, out var row))
            throw new InvalidOperationException("Row no longer exists in this draft.");
        if (JsonNode.DeepEquals(row[field], value))
            return;
        if (EventKeys.Fields(key.Table).Contains(field))
            throw new InvalidOperationException("Use a new record to change its primary key.");
        Checkpoint();
        values[key][field] = value?.DeepClone();
    }

    public void Add(EventRowKey key, JsonObject row)
    {
        if (values.ContainsKey(key) || EventKeys.From(key.Table, row) != key)
            throw new InvalidOperationException("Duplicate or inconsistent row key.");
        Checkpoint();
        values.Add(key, (JsonObject)row.DeepClone());
    }

    public void Remove(EventRowKey key)
    {
        if (!values.ContainsKey(key))
            return;
        Checkpoint();
        values.Remove(key);
    }

    public void Apply(IReadOnlyList<EventRowChange> rows)
    {
        if (rows.Count == 0)
            return;
        var next = Clone(values);
        foreach (var row in rows)
        {
            if (row.After == null)
                next.Remove(row.Key);
            else
            {
                if (EventKeys.From(row.Key.Table, row.After) != row.Key)
                    throw new InvalidOperationException("Inconsistent row key.");
                next[row.Key] = (JsonObject)row.After.DeepClone();
            }
        }

        Checkpoint();
        values = next;
    }

    public void Undo()
    {
        if (undo.TryPop(out var previous))
        {
            redo.Push(Clone(values));
            values = previous;
        }
    }

    public void Redo()
    {
        if (redo.TryPop(out var next))
        {
            undo.Push(Clone(values));
            values = next;
        }
    }

    public EventChange BuildChange(Guid id)
    {
        var changes = new List<EventRowChange>();
        foreach (var key in initial.Keys.Union(values.Keys).OrderBy(EventKeys.Order, StringComparer.Ordinal))
        {
            var before = initial.GetValueOrDefault(key);
            var after = values.GetValueOrDefault(key);
            if (before != null && after != null && JsonNode.DeepEquals(before.Values, after))
                continue;
            changes.Add(new(key, before, after == null ? null : (JsonObject)after.DeepClone()));
        }

        return new(id, Original, changes);
    }

    public bool IsDirty => BuildChange(Guid.Empty).Rows.Count > 0;
}
