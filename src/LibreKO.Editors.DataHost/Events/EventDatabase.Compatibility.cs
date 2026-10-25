using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;

namespace LibreKO.Editors.DataHost.Events;
public sealed partial class EventDatabase
{
    public async Task<EventCompatibilityReport> CheckCompatibilityAsync()
    {
        await using var db = Open();
        var expected = Enum.GetValues<EventTable>().ToDictionary(t => t, t => SchemaInspection.Expected(db.Model.FindEntityType(EventSchema.EntityType(t))!));
        var lookups = new[]
        {
            (typeof(ItemData), new[] { "Num", "Name" }),
            (typeof(NpcData), new[] { "Id", "Name" }),
            (typeof(ZoneInfoData), new[] { "ZoneNo", "MapName" }),
            (typeof(KnightsEntity), new[] { "Id", "Name" })
        }.Select(p => SchemaInspection.Expected(db.Model.FindEntityType(p.Item1)!, p.Item2).Select(c => c with { PrimaryKey = false }).ToArray()).ToArray();
        var inspected = await SchemaInspection.ReadAsync(connectionString, expected.Values.Concat(lookups).SelectMany(c => c).Select(c => c.Table));
        var warnings = new List<CompatibilityIssue>();
        foreach (var columns in lookups)
        {
            var actual = inspected.Columns.Where(c => c.Table == columns[0].Table && columns.Any(e => e.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))).Select(c => c with { PrimaryKey = false }).ToArray();
            if (CompatibilityRules.CompareColumns(columns, actual).Count > 0)
                warnings.Add(new("schema.lookup", CompatibilitySeverity.Warning, "Event references", columns[0].Table, "Names or changed-reference validation may be unavailable for this table."));
        }

        var tables = expected.Select(pair =>
        {
            var table = pair.Value[0].Table;
            var issues = CompatibilityRules.CompareColumns(pair.Value, inspected.Columns.Where(c => c.Table == table).ToArray()).ToList();
            if (inspected.Engines.TryGetValue(table, out var engine) && !engine.Equals("InnoDB", StringComparison.OrdinalIgnoreCase))
                issues.Add(new("schema.engine", CompatibilitySeverity.Blocking, "Events", table, "Engine " + engine + " cannot provide the required transactional writes."));
            var extra = inspected.Columns.Where(c => c.Table == table && !pair.Value.Any(e => e.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (extra.Length > 0)
                issues.Add(new("schema.opaque-extra", CompatibilitySeverity.Warning, "Events", table, "Custom columns are preserved by mapped-field updates. Removal and destructive history are disabled because exact preservation is unavailable: " + string.Join(", ", extra.Select(c => c.Name))));
            var blocked = issues.Where(i => i.Severity == CompatibilitySeverity.Blocking).ToArray();
            var browse = !blocked.Any(i => i.Code is not ("schema.engine" or "schema.required-extra"));
            issues.AddRange(warnings);
            var save = !blocked.Any(i => i.Code != "schema.required-extra");
            return new EventTableSupport(pair.Key, browse, save, save ? "Mapped event updates supported; insert/remove guards apply" : string.Join("; ", blocked.Take(3).Select(i => i.Target + ": " + i.Message)), issues);
        }).ToArray();
        return new(inspected.Database, inspected.ServerVersion, tables);
    }

    async Task<IReadOnlyList<string>> OpaqueColumnsAsync(IEnumerable<EventTable> tables)
    {
        var requested = tables.Distinct().ToArray();
        if (requested.Length == 0)
            return[];
        await using var db = Open();
        var expected = requested.Select(t => SchemaInspection.Expected(db.Model.FindEntityType(EventSchema.EntityType(t))!)).ToArray();
        var inspected = await SchemaInspection.ReadAsync(connectionString, expected.SelectMany(c => c).Select(c => c.Table));
        return expected.SelectMany(columns => inspected.Columns.Where(c => c.Table == columns[0].Table && !columns.Any(e => e.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))).Select(c => c.Table + "." + c.Name)).ToArray();
    }
}
