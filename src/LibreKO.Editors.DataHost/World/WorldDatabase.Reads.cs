using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.DataHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace LibreKO.Editors.DataHost.World;

public sealed partial class WorldDatabase
{
    internal static string Quote(string name) => MappedRowStore.Quote(name);
    internal static string Table(AppDbContext db, WorldTable table) => MappedRowStore.Table(db, MappedTable.World(table));
    internal static string Column(AppDbContext db, WorldTable table, string property)
    {
        return MappedRowStore.Column(db, MappedTable.World(table), property);
    }
    internal static DbCommand Command(AppDbContext db, string sql, IReadOnlyList<object?> args)
    {
        return MappedRowStore.Command(db, sql, args);
    }
    internal static Dictionary<string, object?> KeyValues(WorldKey key)
    {
        WorldKeys.Validate(key);
        var fields = WorldKeys.Fields(key.Table);
        var values = new Dictionary<string, object?> { [key.Table == WorldTable.GameEvents ? "EventNum" : fields[0]] = key.Id };
        if (key.Zone != null) values["ZoneNum"] = key.Zone.Value;
        if (key.IsMonster != null) values["IsMonster"] = key.IsMonster.Value;
        var schema = WorldSchema.Catalog().Tables.Single(x => x.Table == key.Table);
        foreach (var pair in values.Where(x => x.Value is int))
        {
            var field = schema.Fields.Single(x => x.Field.Name == pair.Key).Field; var value = (int)pair.Value!;
            if (field.Minimum is decimal min && value < min || field.Maximum is decimal max && value > max) throw new InvalidOperationException("World primary key is outside its mapped range.");
        }
        return values;
    }

    internal static async Task<WorldRowSnapshot[]> ReadAsync(AppDbContext db, WorldTable table, string suffix = "", IReadOnlyList<object?>? args = null)
    {
        await db.Database.OpenConnectionAsync();
        var properties = WorldSchema.Properties(table);
        var select = string.Join(",", properties.Select(p => "w." + Column(db, table, p.Name)));
        await using var command = Command(db, "SELECT " + select + " FROM " + Table(db, table) + " w " + suffix, args ?? []);
        await using var reader = await command.ExecuteReaderAsync(); var rows = new List<WorldRowSnapshot>();
        while (await reader.ReadAsync())
        {
            var values = new JsonObject();
            for (var i = 0; i < properties.Length; i++)
            {
                if (reader.IsDBNull(i)) { values[properties[i].Name] = null; continue; }
                var type = Nullable.GetUnderlyingType(properties[i].PropertyType) ?? properties[i].PropertyType;
                var raw = reader.GetValue(i);
                if (type == typeof(bool) && raw is not bool && Convert.ToInt64(raw, CultureInfo.InvariantCulture) is not (0 or 1))
                    throw new InvalidDataException("A mapped Boolean/key contains an unsupported stored value.");
                var value = Convert.ChangeType(raw, type.IsEnum ? Enum.GetUnderlyingType(type) : type, CultureInfo.InvariantCulture);
                values[properties[i].Name] = JsonSerializer.SerializeToNode(value, type.IsEnum ? Enum.GetUnderlyingType(type) : type);
            }
            rows.Add(new(WorldKeys.From(table, values), values, RecordFingerprint.Compute(values)));
        }
        if (rows.Select(x => x.Key).Distinct().Count() != rows.Count) throw new InvalidDataException("Stored rows collapse to duplicate typed world keys.");
        return rows.ToArray();
    }

    internal static async Task<WorldRowSnapshot?> ReadKeyAsync(AppDbContext db, WorldKey key, bool forUpdate = false)
    {
        var values = await MappedRowStore.ReadAsync(db, MappedTable.World(key.Table), KeyValues(key), forUpdate);
        return values == null ? null : new(key, values, RecordFingerprint.Compute(values));
    }

    public async Task<WorldRowSnapshot?> GetAsync(WorldKey key)
    {
        _ = KeyValues(key); Require(await CheckCompatibilityAsync(), [key.Table]);
        await using var db = Open(); return await ReadKeyAsync(db, key);
    }
    public async Task<IReadOnlyList<WorldRowSnapshot>> GetBatchAsync(IReadOnlyList<WorldKey> keys)
    {
        if(keys.Count>100)throw new InvalidOperationException("Read up to 100 complete world keys per preview batch.");
        foreach(var key in keys)_=KeyValues(key);
        Require(await CheckCompatibilityAsync(),keys.Select(k=>k.Table));await using var db=Open();var rows=new List<WorldRowSnapshot>();
        foreach(var group in keys.Distinct().GroupBy(k=>k.Table).OrderBy(g=>g.Key))
        {
            var args=new List<object?>();var conditions=new List<string>();
            foreach(var key in group.OrderBy(WorldKeys.Order,StringComparer.Ordinal))
            {
                var predicate=new List<string>();foreach(var field in KeyValues(key)){predicate.Add("w."+Column(db,group.Key,field.Key)+"=@p"+args.Count);args.Add(field.Value);}conditions.Add("("+string.Join(" AND ",predicate)+")");
            }
            rows.AddRange(await ReadAsync(db,group.Key,"WHERE "+string.Join(" OR ",conditions),args));
        }
        return rows.OrderBy(r=>WorldKeys.Order(r.Key),StringComparer.Ordinal).ToArray();
    }

    public async Task<Page<WorldRowSnapshot>> ListAsync(WorldQuery query)
    {
        _ = WorldKeys.Fields(query.Table); Require(await CheckCompatibilityAsync(), [query.Table]);
        await using var db = Open();
        var page = Math.Clamp(query.Page, 0, 1_000_000); var size = Math.Clamp(query.Size, 1, 100);
        var args = new List<object?>(); var filters = new List<string>();
        string Arg(object? value) { args.Add(value); return "@p" + (args.Count - 1); }
        string C(string field) => "w." + Column(db, query.Table, field);
        if (query.IsMonster is bool monster)
            filters.Add(query.Table switch
            {
                WorldTable.Npcs or WorldTable.NpcItems => C("IsMonster") + "=" + Arg(monster),
                WorldTable.NpcPositions => "(" + C("ActType") + "<100)=" + Arg(monster),
                _ => throw new InvalidOperationException("This table has no NPC/monster scope filter.")
            });
        if (query.Zone is int zone)
        {
            var field = query.Table switch { WorldTable.ZoneInfos => "ZoneNo", WorldTable.StartPositions or WorldTable.NpcPositions or WorldTable.MapObjects or WorldTable.Warps => "ZoneId", WorldTable.GameEvents => "ZoneNum", _ => null };
            if (field != null) filters.Add(C(field) + "=" + Arg(zone));
            else if (query.Table == WorldTable.Npcs)
                filters.Add("EXISTS(SELECT 1 FROM " + Table(db, WorldTable.NpcPositions) + " s WHERE s." + Column(db, WorldTable.NpcPositions, "NpcId") + "=" + C("Id") +
                    " AND (s." + Column(db, WorldTable.NpcPositions, "ActType") + "<100)=" + C("IsMonster") + " AND s." + Column(db, WorldTable.NpcPositions, "ZoneId") + "=" + Arg(zone) + ")");
            else throw new InvalidOperationException("This table has no zone filter.");
        }
        if (query.ParentId is int parent)
        {
            var field = WorldSchema.ParentField(query.Table) ?? throw new InvalidOperationException("No supported parent filter for this table.");
            filters.Add(C(field) + "=" + Arg(parent));
        }
        var text = query.Text.Trim();
        if(query.ShopGroup is int shop)
        {if(query.Table!=WorldTable.Npcs)throw new InvalidOperationException("Only NPC prototypes have merchant groups.");filters.Add(C("SellingGroup")+"="+Arg(shop));}
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) filters.Add(C(query.Table == WorldTable.GameEvents ? "EventNum" : WorldKeys.Fields(query.Table)[0]) + "=" + Arg(id));
        else if (text != "")
        {
            var name = query.Table == WorldTable.ZoneInfos ? "MapName" : WorldSchema.Properties(query.Table).Any(p => p.Name == "Name") ? "Name" : null;
            if (name != null) filters.Add("LOCATE(" + Arg(text) + "," + C(name) + ")>0");
            else if (query.Table is WorldTable.NpcPositions or WorldTable.NpcItems)
            {
                var foreign = query.Table == WorldTable.NpcPositions ? "NpcId" : "Index";
                var npcField = query.Table == WorldTable.NpcPositions ? "Id" : "ItemGroup";
                var kind = query.Table == WorldTable.NpcPositions ? "(" + C("ActType") + "<100)" : C("IsMonster");
                filters.Add("EXISTS(SELECT 1 FROM " + Table(db, WorldTable.Npcs) + " n WHERE n." + Column(db, WorldTable.Npcs, npcField) + "=" + C(foreign) +
                    " AND n." + Column(db, WorldTable.Npcs, "IsMonster") + "=" + kind + " AND LOCATE(" + Arg(text) + ",n." + Column(db, WorldTable.Npcs, "Name") + ")>0)");
            }
            else
            {
                var key = WorldSchema.Catalog().Tables.Single(x => x.Table == query.Table).Fields.Single(x => x.Field.Name == WorldKeys.Fields(query.Table)[0]).Field;
                var codes = key.Choices?.Where(x => x.Value.Contains(text, StringComparison.OrdinalIgnoreCase)).Select(x => int.Parse(x.Key)).ToArray() ?? [];
                filters.Add(codes.Length == 0 ? "1=0" : C(key.Name) + " IN (" + string.Join(",", codes.Select(code => Arg(code))) + ")");
            }
        }
        var where = filters.Count == 0 ? "" : "WHERE " + string.Join(" AND ", filters);
        await db.Database.OpenConnectionAsync();
        await using var count = Command(db, "SELECT COUNT(*) FROM " + Table(db, query.Table) + " w " + where, args);
        var total = Convert.ToInt64(await count.ExecuteScalarAsync());
        if (total > int.MaxValue) throw new InvalidOperationException("Result count exceeds this editor's supported page range.");
        var order = string.Join(",", WorldKeys.Fields(query.Table).Select(C));
        var rows = await ReadAsync(db, query.Table, where + " ORDER BY " + order + " LIMIT " + size + " OFFSET " + page * size, args);
        return new(rows, (int)total, page, size);
    }

    public async Task<int> NextIdAsync(WorldTable table, int? zone = null, bool? isMonster = null, IReadOnlyList<int>? reserved = null)
    {
        var key = new WorldKey(table, 1, zone, isMonster); var values = KeyValues(key);
        Require(await CheckCompatibilityAsync(), [table]);
        if (reserved?.Count > 10000) throw new InvalidOperationException("Reserve up to 10000 provisional IDs at once.");
        var idField = table == WorldTable.GameEvents ? "EventNum" : WorldKeys.Fields(table)[0];
        var descriptor = Catalog().Tables.Single(x => x.Table == table).Fields.Single(x => x.Field.Name == idField).Field;
        var min = Math.Max(1, descriptor.Minimum ?? 1); var max = Math.Min(int.MaxValue, descriptor.Maximum ?? int.MaxValue);
        var used = reserved?.ToHashSet() ?? [];
        if (descriptor.Choices != null)
        {
            await using var typed = Open();
            foreach (var candidate in descriptor.Choices.Keys.Select(int.Parse).Where(x => x >= min && x <= max).Order())
                if (!used.Contains(candidate) && await ReadKeyAsync(typed, key with { Id = candidate }) == null) return candidate;
            throw new InvalidOperationException("No unused supported key remains.");
        }
        await using var db = Open(); await db.Database.OpenConnectionAsync();
        var scope = values.Where(x => x.Key != idField).ToArray();
        var where = scope.Length == 0 ? "" : " WHERE " + string.Join(" AND ", scope.Select((x, i) => Column(db, table, x.Key) + "=@p" + i));
        await using var command = Command(db, "SELECT " + Column(db, table, idField) + " FROM " + Table(db, table) + where + " ORDER BY " + Column(db, table, idField), scope.Select(x => x.Value).ToArray());
        await using var reader = await command.ExecuteReaderAsync();
        long next = (long)min;
        void SkipReserved() { while (next <= max && used.Contains((int)next)) next++; }
        SkipReserved();
        while (next <= max && await reader.ReadAsync())
        { var current = Convert.ToInt64(reader.GetValue(0)); if (current < next) continue; if (current > next) break; next++; SkipReserved(); }
        if (next > max) throw new InvalidOperationException("No unused positive ID remains within the mapped key range.");
        return (int)next;
    }
}
