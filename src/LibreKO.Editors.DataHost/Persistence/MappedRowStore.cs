using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.DataHost.World;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace LibreKO.Editors.DataHost.Persistence;
internal static class MappedRowStore
{
    internal static string Quote(string name) => "`" + name.Replace("`", "``") + "`";
    internal static string Table(AppDbContext db, MappedTable table) => Quote(db.Model.FindEntityType(table.EntityType)!.GetTableName()!);
    internal static string Column(AppDbContext db, MappedTable table, string name)
    {
        var entity = db.Model.FindEntityType(table.EntityType)!;
        return Quote(entity.FindProperty(name)?.GetColumnName(StoreObjectIdentifier.Table(entity.GetTableName()!, null)) ?? throw new InvalidOperationException("Unknown mapped field."));
    }
    internal static DbCommand Command(AppDbContext db, string sql, IReadOnlyList<object?> args)
    {
        var command = db.Database.GetDbConnection().CreateCommand(); command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction(); command.CommandText = sql;
        for (var i = 0; i < args.Count; i++) { var p = command.CreateParameter(); p.ParameterName = "@p" + i; p.Value = args[i] ?? DBNull.Value; command.Parameters.Add(p); }
        return command;
    }
    static void CheckKeys(MappedTable table, IReadOnlyDictionary<string, object?> keys)
    { if (keys.Count != table.KeyFields.Count || table.KeyFields.Any(f => !keys.ContainsKey(f))) throw new InvalidOperationException("Provide the complete mapped primary key."); }
    internal static async Task<JsonObject?> ReadAsync(AppDbContext db, MappedTable table, IReadOnlyDictionary<string, object?> keys, bool forUpdate)
    {
        CheckKeys(table, keys); await db.Database.OpenConnectionAsync();
        var columns = table.Properties.Select(p => Column(db, table, p.Name));
        var where = string.Join(" AND ", table.KeyFields.Select((f, i) => Column(db, table, f) + "=@p" + i));
        await using var cmd = Command(db, "SELECT " + string.Join(",", columns) + " FROM " + Table(db, table) + " WHERE " + where + (forUpdate ? " FOR UPDATE" : ""), table.KeyFields.Select(f => keys[f]).ToArray());
        await using var reader = await cmd.ExecuteReaderAsync(); if (!await reader.ReadAsync()) return null;
        var result = new JsonObject();
        for (var i = 0; i < table.Properties.Length; i++)
        {
            var p = table.Properties[i]; if (reader.IsDBNull(i)) { result[p.Name] = null; continue; }
            var type = Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType; if (type.IsEnum) type = Enum.GetUnderlyingType(type);
            var raw = reader.GetValue(i);
            if (type == typeof(bool) && raw is not bool && Convert.ToInt64(raw, CultureInfo.InvariantCulture) is not (0 or 1)) throw new InvalidDataException("Unsupported stored Boolean key/value.");
            result[p.Name] = JsonSerializer.SerializeToNode(Convert.ChangeType(raw, type, CultureInfo.InvariantCulture), type);
        }
        if (await reader.ReadAsync()) throw new InvalidDataException("Mapped key matches more than one stored row.");
        return result;
    }
    internal static Task ApplyAsync(AppDbContext db, MappedTable table, WorldRowChange row) => ApplyAsync(db, table, WorldDatabase.KeyValues(row.Key), row.Before?.Values, row.After);
    internal static async Task ApplyAsync(AppDbContext db, MappedTable table, IReadOnlyDictionary<string, object?> keys, JsonObject? before, JsonObject? after)
    {
        CheckKeys(table, keys);
        var fields = after == null ? [] : before == null ? table.Properties.Where(p => table.WritableFields.Contains(p.Name) || table.KeyFields.Contains(p.Name)).Select(p => p.Name).ToArray()
            : table.WritableFields.Where(f => !JsonNode.DeepEquals(before[f], after[f])).Order(StringComparer.Ordinal).ToArray();
        if (before != null && after != null && fields.Length == 0) return;
        var args = new List<object?>(); string Arg(object? value) { args.Add(value); return "@p" + (args.Count - 1); }
        var assignments = fields.Select(f => Column(db, table, f) + "=" + Arg(table.TypedValue(f, after![f]))).ToArray();
        string sql;
        if (before == null && after != null)
            sql = "INSERT INTO " + Table(db, table) + " (" + string.Join(",", fields.Select(f => Column(db, table, f))) + ") VALUES (" + string.Join(",", Enumerable.Range(0, args.Count).Select(i => "@p" + i)) + ")";
        else
        {
            var where = " WHERE " + string.Join(" AND ", table.KeyFields.Select(f => Column(db, table, f) + "=" + Arg(keys[f])));
            sql = after == null ? "DELETE FROM " + Table(db, table) + where : "UPDATE " + Table(db, table) + " SET " + string.Join(",", assignments) + where;
        }
        await using var cmd = Command(db, sql, args);
        if (await cmd.ExecuteNonQueryAsync() != 1) throw new InvalidOperationException("A mapped row changed during the transaction.");
    }
}
