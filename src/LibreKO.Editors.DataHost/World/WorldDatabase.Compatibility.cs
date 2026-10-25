using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;

namespace LibreKO.Editors.DataHost.World;

public sealed partial class WorldDatabase
{
    public async Task<WorldCompatibilityReport> CheckCompatibilityAsync()
    {
        await using var db = Open();
        var expected = Enum.GetValues<WorldTable>().ToDictionary(t => t, t => SchemaInspection.Expected(db.Model.FindEntityType(WorldSchema.EntityType(t))!));
        var lookups = new[] { (typeof(ItemData), new[] { "Num", "Name" }), (typeof(NpcData), new[] { "Id", "IsMonster", "Name" }),
            (typeof(ZoneInfoData), new[] { "ZoneNo", "MapName" }) }.Select(x => SchemaInspection.Expected(db.Model.FindEntityType(x.Item1)!, x.Item2)
                .Select(c => c with { PrimaryKey = false }).ToArray()).ToArray();
        var inspected = await SchemaInspection.ReadAsync(connectionString, expected.Values.Concat(lookups).SelectMany(x => x).Select(x => x.Table));
        var warnings = new List<CompatibilityIssue>();
        foreach (var columns in lookups)
        {
            var actual = inspected.Columns.Where(c => c.Table == columns[0].Table && columns.Any(e => e.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase)))
                .Select(c => c with { PrimaryKey = false }).ToArray();
            if (CompatibilityRules.CompareColumns(columns, actual).Count > 0)
                warnings.Add(new("schema.lookup", CompatibilitySeverity.Warning, "World references", columns[0].Table, "This name/reference source is unavailable; unrelated supported sections remain usable."));
        }
        var tables = expected.Select(pair =>
        {
            var table = pair.Value[0].Table;
            var issues = CompatibilityRules.CompareColumns(pair.Value, inspected.Columns.Where(c => c.Table == table).ToArray()).ToList();
            if (inspected.Engines.TryGetValue(table, out var engine) && !engine.Equals("InnoDB", StringComparison.OrdinalIgnoreCase))
                issues.Add(new("schema.engine", CompatibilitySeverity.Blocking, "World", table, "Engine " + engine + " does not provide the required transactional writes."));
            var extra = inspected.Columns.Where(c => c.Table == table && !pair.Value.Any(e => e.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
            if (extra.Length > 0) issues.Add(new("schema.opaque-extra", CompatibilitySeverity.Warning, "World", table,
                "Mapped updates preserve custom columns. Destructive history/removal requires exact preservation and is disabled for: " + string.Join(", ", extra.Select(x => x.Name))));
            var blocked = issues.Where(x => x.Severity == CompatibilitySeverity.Blocking).ToArray();
            var browse = !blocked.Any(x => x.Code is not ("schema.engine" or "schema.required-extra"));
            issues.AddRange(warnings);
            var save = !blocked.Any(x => x.Code != "schema.required-extra");
            return new WorldTableSupport(pair.Key, browse, save, save ? "Mapped world updates supported; insert/remove guards apply" :
                string.Join("; ", blocked.Take(3).Select(x => x.Target + ": " + x.Message)), issues);
        }).ToArray();
        return new(inspected.Database, inspected.ServerVersion, tables);
    }
}
