using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.DataHost.Events;
using LibreKO.Editors.DataHost.World;

namespace LibreKO.Editors.DataHost.Persistence;

// Only the two audited schema catalogs can produce an executable table mapping.
internal sealed record MappedTable
{
    public Type EntityType { get; }
    public IReadOnlyList<string> KeyFields { get; }
    public IReadOnlySet<string> WritableFields { get; }
    public PropertyInfo[] Properties { get; }
    MappedTable(Type type, IReadOnlyList<string> keys, IReadOnlySet<string> writable, PropertyInfo[] properties)
    { EntityType = type; KeyFields = keys; WritableFields = writable; Properties = properties; }
    public static MappedTable World(WorldTable table)
    { var schema = WorldSchema.Catalog().Tables.Single(t => t.Table == table); return new(WorldSchema.EntityType(table), schema.KeyFields, schema.Fields.Where(f => f.Field.Writable).Select(f => f.Field.Name).ToHashSet(), WorldSchema.Properties(table)); }
    public static MappedTable Event(EventTable table)
    { var schema = EventSchema.Catalog().Tables.Single(t => t.Table == table); return new(EventSchema.EntityType(table), schema.KeyFields, schema.Fields.Where(f => f.Field.Writable).Select(f => f.Field.Name).ToHashSet(), EventSchema.Properties(table)); }
    public object? TypedValue(string field, JsonNode? value)
    {
        if (value == null) return null;
        var type = Properties.Single(p => p.Name == field).PropertyType; type = Nullable.GetUnderlyingType(type) ?? type;
        return type.IsEnum ? Convert.ChangeType(value.ToString(), Enum.GetUnderlyingType(type), CultureInfo.InvariantCulture) : JsonSerializer.Deserialize(value.ToJsonString(), type);
    }
    public JsonObject Normalize(JsonObject values) => new(values.Select(p => new KeyValuePair<string, JsonNode?>(p.Key, JsonSerializer.SerializeToNode(TypedValue(p.Key, p.Value)))));
}
