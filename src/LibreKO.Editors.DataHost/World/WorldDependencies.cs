using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.World;

namespace LibreKO.Editors.DataHost.World;

public static class WorldDependencies
{
    public static async Task<WorldMembership> CaptureAsync(AppDbContext db, WorldKey owner, string relation, bool forUpdate = false)
    {
        WorldKeys.Validate(owner);
        if (!WorldRules.Relations(owner.Table).Contains(relation)) throw new InvalidOperationException("Unsupported typed world relation.");
        var child = relation switch { "zone-spawns" or "npc-spawns" => WorldTable.NpcPositions, "zone-objects" => WorldTable.MapObjects,
            "zone-events" => WorldTable.GameEvents, "npc-exchanges" => WorldTable.ItemExchanges, "drop-users" => WorldTable.Npcs, "pool-users" => WorldTable.NpcItems,
            _ => throw new InvalidOperationException("Unsupported world relation.") };
        string C(string property) => "w." + WorldDatabase.Column(db, child, property);
        IReadOnlyList<object?> args = [owner.Id];
        var filter = relation switch
        {
            "zone-spawns" or "zone-objects" => C("ZoneId") + "=@p0", "zone-events" => C("ZoneNum") + "=@p0",
            "npc-spawns" => C("NpcId") + "=@p0 AND (" + C("ActType") + "<100)=@p1",
            "npc-exchanges" => C("NpcId") + "=@p0", "drop-users" => C("ItemGroup") + "=@p0 AND " + C("IsMonster") + "=@p1",
            "pool-users" when owner.Id is >= 100 and < 100_000_000 => "(" + string.Join(" OR ", Enumerable.Range(1, 7).Select(i => C("Item" + i) + "=@p0")) + ")",
            "pool-users" => "1=0", _ => throw new InvalidOperationException("Unsupported world relation.")
        };
        if (relation is "npc-spawns" or "drop-users") args = [owner.Id, owner.IsMonster!.Value];
        var order = string.Join(",", WorldKeys.Fields(child).Select(C));
        var rows = await WorldDatabase.ReadAsync(db, child, "WHERE " + filter + " ORDER BY " + order + (forUpdate ? " FOR UPDATE" : ""), args);
        return new(owner, relation, rows.Select(x => x.Key).ToArray());
    }
}
