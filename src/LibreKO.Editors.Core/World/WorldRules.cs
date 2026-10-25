using System.Globalization;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Events;
using LibreKO.Editors.Core.Maps;
using LibreKO.Editors.Core.References;
using LibreKO.Editors.Core.Sources;

namespace LibreKO.Editors.Core.World;

public static class WorldRules
{
    public static ReferenceIdentity? ReferenceFor(WorldTable table, string field, JsonObject values)
    {
        if (!int.TryParse(values[field]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) return null;
        if (table is WorldTable.GameEvents or WorldTable.MapObjects or WorldTable.EventTriggers)
        {
            var shared = table switch { WorldTable.GameEvents => EventTable.GameEvents, WorldTable.MapObjects => EventTable.MapObjects, _ => EventTable.EventTriggers };
            var kind = EventRules.ReferenceFor(shared, field, values);
            return kind == null ? null : new(kind, id);
        }
        return table switch
        {
            WorldTable.StartPositions when field == "ZoneId" => new("zones", id),
            WorldTable.NpcPositions when field == "ZoneId" => new("zones", id),
            WorldTable.NpcPositions when field == "NpcId" => ReferenceRules.ForSpawn(id, byte.Parse(values["ActType"]!.ToString(), CultureInfo.InvariantCulture)),
            WorldTable.Npcs when field == "ItemGroup" => new("drop-groups", id, IsMonster: values["IsMonster"]!.GetValue<bool>()),
            WorldTable.Npcs when field == "SellingGroup" => new("shops", id),
            WorldTable.Npcs when field == "ModelId" => new("models", id),
            WorldTable.Npcs when field is "WeaponType1" or "WeaponType2" => new("items", id),
            WorldTable.Npcs when field is "Magic1" or "Magic2" or "Magic3" => new("magic", id),
            WorldTable.NpcItems when field.StartsWith("Item", StringComparison.Ordinal) && int.TryParse(field.AsSpan(4), out _) => ReferenceRules.ForDrop(id),
            WorldTable.MonsterSummons when field == "Sid" => new("npcs", id),
            WorldTable.ItemExchanges when field == "NpcId" => new("npcs", id),
            WorldTable.ItemExchanges when field.StartsWith("OriginItem", StringComparison.Ordinal) || field.StartsWith("ExchangeItem", StringComparison.Ordinal) => new("items", id),
            WorldTable.PetTransforms when field is "Material" or "Result" => new("items", id),
            WorldTable.PetTransforms when field == "ModelId" => new("models", id),
            WorldTable.Warps when field == "ZoneId" => new("zones", id),
            _ => null
        };
    }

    public static IReadOnlyList<string> Relations(WorldTable table) => table switch
    {
        WorldTable.ZoneInfos => ["zone-spawns", "zone-objects", "zone-events"],
        WorldTable.Npcs => ["npc-spawns", "npc-exchanges"], WorldTable.NpcItems => ["drop-users"],
        WorldTable.MakeItemGroups => ["pool-users"], _ => []
    };

    public static IReadOnlyList<ValidationIssue> Validate(WorldChange change, WorldCatalog catalog)
    {
        var issues = new List<ValidationIssue>();
        void Error(string field, string message) => issues.Add(new(field, message));
        if (change.Rows.Select(x => x.Key).Distinct().Count() != change.Rows.Count) Error("Rows", "Duplicate world row mutations.");
        foreach (var row in change.Rows)
        {
            var prefix = row.Key.Table + " " + row.Key.Id;
            var schema = catalog.Tables.SingleOrDefault(x => x.Table == row.Key.Table);
            if (schema == null) { Error(prefix, "Unsupported world table."); continue; }
            try
            {
                WorldKeys.Validate(row.Key);
                if (row.Before == null && row.After == null) Error(prefix, "Empty world mutation.");
                if (row.Before != null && (row.Before.Key != row.Key || WorldKeys.From(row.Key.Table, row.Before.Values) != row.Key)) Error(prefix, "Inconsistent original row key.");
                if (row.After != null && WorldKeys.From(row.Key.Table, row.After) != row.Key) Error(prefix, "World row keys are immutable.");
            }
            catch { Error(prefix, "Provide exactly the complete world row key."); }
            if (row.After == null) continue;
            if (row.Before == null && row.Key.Id <= 0) Error(prefix, "New world row IDs must be positive.");
            foreach (var descriptor in schema.Fields)
                if (!row.After.ContainsKey(descriptor.Field.Name)) Error(prefix + "." + descriptor.Field.Name, "A complete proposed mapped row is required.");
            foreach (var value in row.After)
            {
                var descriptor = schema.Fields.SingleOrDefault(x => x.Field.Name == value.Key);
                var path = prefix + "." + value.Key;
                if (descriptor == null) { Error(path, "Unknown mapped field."); continue; }
                if (row.Before != null && JsonNode.DeepEquals(row.Before.Values[value.Key], value.Value)) continue;
                var field = descriptor.Field;
                if (!field.Writable && !(row.Before == null && schema.KeyFields.Contains(field.Name))) { Error(path, "This field is read-only."); continue; }
                try
                {
                    if (value.Value == null) { if (!descriptor.Nullable) throw new FormatException(); continue; }
                    var text = value.Value.ToString();
                    if (field.Kind == FieldKind.Text)
                    {
                        if (descriptor.MaxLength is int length && text.Length > length) throw new FormatException();
                        if (row.Key.Table == WorldTable.ZoneInfos && field.Name == "SmdName") SourcePaths.ValidateRelative(text);
                        continue;
                    }
                    if (field.Kind == FieldKind.Boolean) { _ = value.Value.GetValue<bool>(); continue; }
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var finite) || !double.IsFinite(finite)) throw new FormatException();
                    if (field.Kind != FieldKind.Decimal || field.Minimum != null || field.Maximum != null)
                    {
                        var number = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                        if (field.Kind != FieldKind.Decimal && number != decimal.Truncate(number) ||
                            field.Minimum is decimal min && number < min || field.Maximum is decimal max && number > max) throw new FormatException();
                    }
                    if (field.Choices != null && !field.Choices.ContainsKey(text)) throw new FormatException();
                }
                catch { Error(path, "Invalid value or supported range."); }
            }
            if (row.Key.Table == WorldTable.NpcPositions && (row.Before == null || !JsonNode.DeepEquals(row.Before.Values["Path"], row.After["Path"]) || !JsonNode.DeepEquals(row.Before.Values["DotCnt"], row.After["DotCnt"])))
                try { PatrolPath.Decode(byte.Parse(row.After["DotCnt"]!.ToString(), CultureInfo.InvariantCulture), row.After["Path"]?.ToString()); }
                catch (Exception e) { Error(prefix + ".Path", e.Message); }
        }
        foreach (var membership in change.Memberships)
        {
            try
            {
                WorldKeys.Validate(membership.Owner);
                if (!Relations(membership.Owner.Table).Contains(membership.Relation)) Error("Membership", "Unsupported world membership relation.");
                foreach (var key in membership.BeforeKeys) WorldKeys.Validate(key);
                if (membership.BeforeKeys.Distinct().Count() != membership.BeforeKeys.Count) Error("Membership", "Duplicate membership keys.");
            }
            catch { Error("Membership", "Invalid complete membership identity."); }
        }
        return issues;
    }
}
