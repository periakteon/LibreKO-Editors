using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Editors.DataHost.Persistence;
internal static class MappedSchemaGuard
{
    internal static async Task RequireAsync(string connectionString, AppDbContext db, IEnumerable<(MappedTable Table, bool Insert, bool Delete)> changes)
    {
        var requested = changes.ToArray();
        var expected = requested.Select(x => SchemaInspection.Expected(db.Model.FindEntityType(x.Table.EntityType)!)).ToArray();
        var inspected = await SchemaInspection.ReadAsync(connectionString, expected.SelectMany(x => x).Select(x => x.Table));
        for (var i = 0; i < requested.Length; i++)
        {
            var columns = expected[i]; var name = columns[0].Table; var actual = inspected.Columns.Where(c => c.Table == name).ToArray();
            var blocking = CompatibilityRules.CompareColumns(columns, actual).Where(x => x.Code != "schema.required-extra" || requested[i].Insert).ToArray();
            if (blocking.Length > 0) throw new InvalidOperationException(string.Join("; ", blocking.Select(x => x.Target + ": " + x.Message)));
            if (!inspected.Engines.TryGetValue(name, out var engine) || !engine.Equals("InnoDB", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Mapped writes require InnoDB: " + name);
            if (requested[i].Delete && actual.Any(c => !columns.Any(e => e.Name.Equals(c.Name, StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("Removal/destructive history is disabled because custom columns cannot be preserved exactly: " + name + ". Mapped updates remain supported.");
        }
    }
}
