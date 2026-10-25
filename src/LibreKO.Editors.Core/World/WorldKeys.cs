using System.Globalization;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.World;

public static class WorldKeys
{
    public static string[] Fields(WorldTable table) => table switch
    {
        WorldTable.ZoneInfos => ["ZoneNo"], WorldTable.StartPositions => ["ZoneId"], WorldTable.Homes => ["Nation"],
        WorldTable.Npcs => ["Id", "IsMonster"], WorldTable.NpcPositions => ["Index"], WorldTable.NpcItems => ["Index", "IsMonster"],
        WorldTable.MakeItemGroups => ["GroupNum"], WorldTable.MonsterSummons or WorldTable.ItemExchanges or WorldTable.EventTriggers => ["Index"],
        WorldTable.PetLevels => ["Level"], WorldTable.PetTransforms or WorldTable.MapObjects => ["Id"],
        WorldTable.GameEvents => ["ZoneNum", "EventNum"], WorldTable.Warps => ["WarpId"],
        _ => throw new InvalidOperationException("Unsupported world table.")
    };

    public static void Validate(WorldKey key)
    {
        _ = Fields(key.Table);
        if ((key.Table == WorldTable.GameEvents) != (key.Zone != null) ||
            (key.Table is WorldTable.Npcs or WorldTable.NpcItems) != (key.IsMonster != null))
            throw new InvalidOperationException("Provide exactly the complete primary key, including the required zone or NPC/monster discriminator.");
    }

    public static WorldKey From(WorldTable table, JsonObject values)
    {
        var fields = Fields(table);
        if (fields.Any(f => !values.TryGetPropertyValue(f, out var value) || value == null))
            throw new InvalidOperationException("World row is missing part of its primary key.");
        var idField = table == WorldTable.GameEvents ? "EventNum" : fields[0];
        var key = new WorldKey(table, int.Parse(values[idField]!.ToString(), CultureInfo.InvariantCulture),
            table == WorldTable.GameEvents ? int.Parse(values["ZoneNum"]!.ToString(), CultureInfo.InvariantCulture) : null,
            table is WorldTable.Npcs or WorldTable.NpcItems ? values["IsMonster"]!.GetValue<bool>() : null);
        Validate(key); return key;
    }

    public static string Order(WorldKey key) => $"{(int)key.Table:D2}:{key.Zone ?? -1:D10}:{(key.IsMonster == null ? -1 : key.IsMonster.Value ? 1 : 0)}:{key.Id:D10}";
}
