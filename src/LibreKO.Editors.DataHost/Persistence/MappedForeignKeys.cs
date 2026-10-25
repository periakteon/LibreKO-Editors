using System.Text.Json.Nodes;
using LibreKO.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace LibreKO.Editors.DataHost.Persistence;
internal record MappedMutation(MappedTable Table, IReadOnlyDictionary<string, object?> Keys, JsonObject? Before, JsonObject? After);
internal static class MappedForeignKeys
{
    internal static async Task<bool> SafeAsync(AppDbContext db, IReadOnlyList<MappedMutation> changes)
    {
        await using var caseCommand = MappedRowStore.Command(db, "SELECT @@lower_case_table_names", []);
        var comparison = Convert.ToInt32(await caseCommand.ExecuteScalarAsync()) == 0 ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        foreach (var deleted in changes.Where(r => r.After == null))
        {
            var entity = db.Model.FindEntityType(deleted.Table.EntityType)!; var name = entity.GetTableName()!;
            var links = new List<(string Schema, string Table, string Field, string Target, string Constraint)>();
            await using (var cmd = MappedRowStore.Command(db, "SELECT TABLE_SCHEMA,TABLE_NAME,COLUMN_NAME,REFERENCED_COLUMN_NAME,CONSTRAINT_NAME FROM information_schema.KEY_COLUMN_USAGE WHERE REFERENCED_TABLE_SCHEMA=DATABASE() AND REFERENCED_TABLE_NAME=@p0", [name]))
            { await using var reader = await cmd.ExecuteReaderAsync(); while (await reader.ReadAsync()) links.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4))); }
            foreach (var group in links.GroupBy(l => (l.Schema, l.Table, l.Constraint)))
            {
                var args = new List<object?>(); var predicates = new List<string>();
                foreach (var link in group)
                {
                    var property = entity.GetProperties().SingleOrDefault(p => p.GetColumnName(StoreObjectIdentifier.Table(name, null)) == link.Target);
                    if (property == null) return false;
                    predicates.Add(MappedRowStore.Quote(link.Field) + "=@p" + args.Count); args.Add(deleted.Table.TypedValue(property.Name, deleted.Before![property.Name]));
                }
                var known = changes.Where(r => group.Key.Schema.Equals(db.Database.GetDbConnection().Database, comparison) && MappedRowStore.Table(db, r.Table).Trim('`').Equals(group.Key.Table, comparison)).ToArray();
                var table = known.FirstOrDefault()?.Table;
                var select = table == null ? "1" : string.Join(",", table.KeyFields.Select(f => MappedRowStore.Column(db, table, f)));
                await using var cmd = MappedRowStore.Command(db, "SELECT " + select + " FROM " + MappedRowStore.Quote(group.Key.Schema) + "." + MappedRowStore.Quote(group.Key.Table) + " WHERE " + string.Join(" AND ", predicates) + " FOR UPDATE", args);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (table == null) return false;
                    var key = new JsonObject(); for (var i = 0; i < table.KeyFields.Count; i++) key[table.KeyFields[i]] = System.Text.Json.JsonSerializer.SerializeToNode(reader.GetValue(i));
                    var planned = known.SingleOrDefault(r => table.KeyFields.All(f => Equals(table.TypedValue(f, key[f]), Convert.ChangeType(r.Keys[f], table.TypedValue(f, key[f])!.GetType()))));
                    if (planned == null) return false;
                    if (planned.After != null && group.All(link =>
                    {
                        var child = db.Model.FindEntityType(table.EntityType)!;
                        var property = child.GetProperties().SingleOrDefault(p => p.GetColumnName(StoreObjectIdentifier.Table(child.GetTableName()!, null)) == link.Field);
                        return property == null || Equals(table.TypedValue(property.Name, planned.After[property.Name]), args[group.ToList().IndexOf(link)]);
                    })) return false;
                }
            }
        }
        return true;
    }
}
