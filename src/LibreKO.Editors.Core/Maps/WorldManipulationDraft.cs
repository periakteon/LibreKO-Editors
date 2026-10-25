using System.Globalization;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Maps;
public static class WorldManipulationDraft
{
    public static WorldRowChange Merge(WorldRowSnapshot current,WorldRowChange gesture)
    {
        if(gesture.Key!=current.Key||gesture.Before==null||gesture.After==null)throw new InvalidOperationException("A spatial gesture requires a complete existing row.");
        var after=(JsonObject)current.Values.DeepClone();
        foreach(var field in gesture.After.Where(p=>!JsonNode.DeepEquals(gesture.Before.Values[p.Key],p.Value)))
        {
            if(!JsonNode.DeepEquals(current.Values[field.Key],gesture.Before.Values[field.Key])&&!JsonNode.DeepEquals(current.Values[field.Key],field.Value))throw new InvalidOperationException("The same spatial field changed during this gesture. Cancel and review it again.");
            after[field.Key]=field.Value?.DeepClone();
        }
        return new(current.Key,current,after);
    }
    public static IReadOnlyList<WorldRowChange> Move(IReadOnlyList<WorldRowSnapshot> rows, MapPoint delta)
    {
        if (!float.IsFinite(delta.X) || !float.IsFinite(delta.Y) || !float.IsFinite(delta.Z)) throw new InvalidOperationException("Enter a finite map displacement.");
        if (rows.Select(r => r.Key).Distinct().Count() != rows.Count) throw new InvalidOperationException("Duplicate spatial selection.");
        return rows.Select(row =>
        {
            var fields = row.Key.Table switch { WorldTable.NpcPositions => new[] { "LeftX", "", "TopZ" }, WorldTable.MapObjects or WorldTable.Warps => new[] { "X", "Y", "Z" }, _ => throw new InvalidOperationException("This record has no supported movable map transform.") };
            var next = (JsonObject)row.Values.DeepClone(); var offsets = new[] { delta.X, delta.Y, delta.Z };
            for (var i = 0; i < fields.Length; i++)
            {
                if (fields[i] == "") continue;
                var value = decimal.Parse(next[fields[i]]!.ToString(), CultureInfo.InvariantCulture) + (decimal)offsets[i];
                if (row.Key.Table != WorldTable.MapObjects && value != decimal.Truncate(value)) throw new InvalidOperationException("This stored position requires whole map units. Enable grid snapping.");
                if (row.Key.Table == WorldTable.NpcPositions && (value < int.MinValue || value > int.MaxValue) || row.Key.Table == WorldTable.Warps && (value < short.MinValue || value > short.MaxValue)) throw new InvalidOperationException("Position exceeds its stored coordinate range.");
                next[fields[i]] = JsonValue.Create(value);
            }
            return new WorldRowChange(row.Key, row, next);
        }).ToArray();
    }
}
