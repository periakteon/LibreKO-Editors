using System.Reflection;
using System.Data.Common;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Events;
using LibreKO.Editors.DataHost.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace LibreKO.Editors.DataHost.Events;
internal static class EventTableStore
{
    static readonly MethodInfo read = typeof(EventTableStore).GetMethod(nameof(ReadTyped), BindingFlags.Static | BindingFlags.NonPublic)!;
    public static string Quote(string name) => MappedRowStore.Quote(name);
    public static string Table(AppDbContext db, EventTable table) => MappedRowStore.Table(db, MappedTable.Event(table));
    public static string Column(AppDbContext db, EventTable table, string property)
    {
        return MappedRowStore.Column(db, MappedTable.Event(table), property);
    }

    public static Task<object[]> ReadAsync(AppDbContext db, EventTable table, string suffix = "", object[]? args = null, bool tracking = false) => (Task<object[]>)read.MakeGenericMethod(EventSchema.EntityType(table)).Invoke(null, [db, "SELECT * FROM " + Table(db, table) + " " + suffix, args ?? [], tracking])!;
    static async Task<object[]> ReadTyped<T>(AppDbContext db, string sql, object[] args, bool tracking)
        where T : class
    {
        var query = db.Set<T>().FromSqlRaw(sql, args);
        return (await (tracking ? query.AsTracking() : query.AsNoTracking()).ToArrayAsync()).Cast<object>().ToArray();
    }

    public static DbCommand Command(AppDbContext db, string sql, object[] args)
    {
        for (var i = 0; i < args.Length; i++) sql = sql.Replace("{" + i + "}", "@p" + i);
        return MappedRowStore.Command(db, sql, args);
    }

    public static async Task<long> CountAsync(AppDbContext db, EventTable table, string where, object[] args)
    {
        await db.Database.OpenConnectionAsync();
        await using var command = Command(db, "SELECT COUNT(*) FROM " + Table(db, table) + " " + where, args);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public static (string Sql, object[] Args) DocumentFilter(AppDbContext db, EventTable table, EventDocumentKey key)
    {
        var field = key.Family switch
        {
            EventFamily.Temple when table != EventTable.ForgottenTempleWaves => "Event",
            EventFamily.CollectionRace when table != EventTable.CollectionRaces => "RaceId",
            EventFamily.Lottery when table != EventTable.LotteryEvents => "LotteryId",
            _ => EventKeys.Fields(table)[^1]
        };
        if (table == EventTable.ForgottenTempleWaves)
            return ("", []);
        var where = "WHERE " + Column(db, table, field) + " = {0}";
        return key.Family == EventFamily.MapEvent ? (where + " AND " + Column(db, table, "ZoneNum") + " = {1}", [key.Id, key.Zone!.Value]) : (where, [key.Id]);
    }
}
