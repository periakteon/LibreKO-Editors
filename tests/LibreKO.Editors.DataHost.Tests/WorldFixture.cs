using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.DataHost.World;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

[CollectionDefinition("World database", DisableParallelization = true)]
public class WorldDatabaseCollection { }

internal static class WorldFixture
{
    internal const string Cs = EventFixture.Cs;
    internal const int NpcId = 880019, SpawnId = 885001, PoolId = 881400;
    internal const short DropId = 8819;
    internal static string Scratch() => Directory.CreateTempSubdirectory("libreko-world-" + Guid.NewGuid().ToString("N")).FullName;
    internal static async Task Guard()
    {
        await using var db = EventFixture.Open();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "SELECT @@datadir";
        var data = (string)(await command.ExecuteScalarAsync())!;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "scripts/build.ps1"))) root = root.Parent;
        if (root == null || Path.TrimEndingDirectorySeparator(Path.GetFullPath(data)) != Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root.FullName, ".superpowers/mysql-data"))))
            throw new InvalidOperationException("World fixtures require the editor-owned MySQL instance.");
    }

    internal static async Task Ensure()
    {
        await Guard(); await EventFixture.Ensure();
        await using var db = EventFixture.Open();
        async Task Add<T>(T entity, params object[] keys) where T : class
        { if (await db.Set<T>().FindAsync(keys) == null) db.Set<T>().Add(entity); }
        await Add(new ZoneInfoData { ZoneNo = 212, MapName = "World fixture zone", SmdName = "fixture.smd" }, (short)212);
        await Add(new StartPositionData { ZoneId = 210, KarusX = 20, KarusZ = 30, ElmoradX = 50, ElmoradZ = 60, RangeX = 5, RangeZ = 5 }, (short)210);
        await Add(new HomeData { Nation = 1, KarusZoneX = 20, KarusZoneZ = 30 }, (byte)1);
        await Add(new NpcData { Id = NpcId, IsMonster = true, Name = "Monster fixture", ItemGroup = DropId, ModelId = 100, Size = 100 }, NpcId, true);
        await Add(new NpcData { Id = NpcId, IsMonster = false, Name = "NPC fixture", ItemGroup = DropId, ModelId = 100, Size = 100 }, NpcId, false);
        await Add(new NpcData { Id = DropId, IsMonster = true, Name = "Summon fixture", ModelId = 100, Size = 100 }, (int)DropId, true);
        await Add(new NpcPosData { Index = SpawnId, ZoneId = 210, NpcId = NpcId, ActType = 0, LeftX = 20, TopZ = 30, NumNPC = 1, RegTime = 10 }, SpawnId);
        await Add(new NpcPosData { Index = SpawnId + 1, ZoneId = 212, NpcId = NpcId, ActType = 100, LeftX = 50, TopZ = 60, NumNPC = 1, RegTime = 10 }, SpawnId + 1);
        for (var i = 0; i < 105; i++) await Add(new NpcPosData { Index = 886000 + i, ZoneId = 210, NpcId = NpcId, ActType = 0, LeftX = 25 + i, TopZ = 35, NumNPC = 1, RegTime = 10 }, 886000 + i);
        foreach (var monster in new[] { false, true }) await Add(new NpcItemData { Index = DropId, IsMonster = monster, Item1 = 123450001, Percent1 = 5000, Item2 = PoolId, Percent2 = 10000 }, DropId, monster);
        await Add(new MakeItemGroupData { GroupNum = PoolId, Items = "123450001,123450001" }, PoolId);
        await Add(new MonsterSummonData { Index = 885003, Sid = DropId, Name = "World summon", Probability = 100 }, 885003);
        await Add(new ItemExchangeData { Index = 885004, NpcId = DropId, OriginItem1 = 123450001, OriginCount1 = 1 }, 885004);
        await Add(new PetLevelData { Level = 201, Exp = 9007199254740993L, MaxHp = 1000 }, (byte)201);
        await Add(new PetTransformData { Id = 885008, Material = 123450001, Result = 123450001, ModelId = 100, Size = 100 }, 885008);
        await Add(new GameEventData { ZoneNum = 210, EventNum = 7, Type = 1, Exec1 = 210 }, (byte)210, (short)7);
        await Add(new GameEventData { ZoneNum = 212, EventNum = 7, Type = 3 }, (byte)212, (short)7);
        await Add(new MapObjectData { Id = 885005, ZoneId = 210, Index = 30100, Type = 0, X = 20, Z = 30 }, 885005);
        await Add(new EventTriggerData { Index = 885006, NpcType = 0, NpcId = 42, TriggerNum = -1 }, 885006);
        await Add(new WarpData { WarpId = 885007, Name = "Legacy fixture warp", ZoneId = 210, X = 20, Z = 30, Fee = 100 }, 885007);
        await db.SaveChangesAsync();
    }

    internal static async Task<string> Schema(string suffix, bool allWorldTables)
    {
        await Guard();
        var name = "libreko_editor_test_" + suffix + "_" + Guid.NewGuid().ToString("N")[..12];
        if (name.Length > 64 || !Regex.IsMatch(name, "^libreko_editor_test_[a-z0-9_]+$")) throw new InvalidOperationException("Invalid fixture schema.");
        await using var db = EventFixture.Open();
        await db.Database.ExecuteSqlRawAsync("CREATE DATABASE `" + name + "`");
        var tables = allWorldTables ? Enum.GetValues<WorldTable>() : [WorldTable.ZoneInfos];
        foreach (var table in tables)
        {
            var mapped = db.Model.FindEntityType(WorldSchema.EntityType(table))!.GetTableName()!.Replace("`", "``");
            await db.Database.ExecuteSqlRawAsync($"CREATE TABLE `{name}`.`{mapped}` LIKE libreko_editor_test_native.`{mapped}`");
        }
        if (!allWorldTables)
        {
            await db.Database.ExecuteSqlRawAsync($"CREATE TABLE `{name}`.Npcs(Id int NOT NULL,IsMonster tinyint(1) NOT NULL,Name longtext NOT NULL,PRIMARY KEY(Id,IsMonster)) ENGINE=InnoDB");
            await db.Database.ExecuteSqlRawAsync($"INSERT INTO `{name}`.Npcs VALUES (880019,1,'Minimal lookup fixture')");
            await db.Database.ExecuteSqlRawAsync($"CREATE TABLE `{name}`.Items(Num int NOT NULL PRIMARY KEY,Name longtext NOT NULL) ENGINE=InnoDB");
            await db.Database.ExecuteSqlRawAsync($"INSERT INTO `{name}`.Items VALUES (123450001,'Minimal item name')");
        }
        return new MySqlConnectionStringBuilder(Cs) { Database = name }.ConnectionString;
    }

    internal static async Task<WorldKey> FreeKey(WorldDatabase service, WorldTable table, int? zone = null, bool? monster = null)
    {
        if (table is WorldTable.Npcs or WorldTable.NpcItems) monster ??= true;
        if (table == WorldTable.GameEvents) zone ??= 210;
        var start = table switch { WorldTable.Homes => 2, WorldTable.ZoneInfos or WorldTable.StartPositions => 213,
            WorldTable.PetLevels => 202, WorldTable.GameEvents or WorldTable.NpcItems => 10000, _ => EventFixture.Id() };
        for (var id = start; ; id++)
        { var key = new WorldKey(table, id, zone, monster); if (await service.GetAsync(key) == null) return key; }
    }

    internal static JsonObject Values(WorldKey key)
    {
        var result = (JsonObject)WorldSchema.Catalog().Tables.Single(t => t.Table == key.Table).Defaults.DeepClone();
        result[LibreKO.Editors.Core.World.WorldKeys.Fields(key.Table)[0]] = key.Table == WorldTable.GameEvents ? key.Zone!.Value : key.Id;
        if (key.Table == WorldTable.GameEvents) result["EventNum"] = key.Id;
        if (key.IsMonster is bool monster) result["IsMonster"] = monster;
        switch (key.Table)
        {
            case WorldTable.ZoneInfos: result["MapName"] = "Created fixture"; result["SmdName"] = "fixture.smd"; break;
            case WorldTable.Npcs: result["Name"] = "Created fixture"; break;
            case WorldTable.NpcPositions: result["ZoneId"] = 210; result["NpcId"] = NpcId; result["NumNPC"] = 1; break;
            case WorldTable.StartPositions: result["ZoneId"] = key.Id; break;
            case WorldTable.MakeItemGroups: result["Items"] = "123450001,123450001"; break;
            case WorldTable.MonsterSummons: result["Name"] = "Created fixture"; result["Sid"] = DropId; break;
            case WorldTable.ItemExchanges: result["NpcId"] = DropId; break;
            case WorldTable.GameEvents: result["Type"] = 3; break;
            case WorldTable.MapObjects: case WorldTable.Warps: result["ZoneId"] = 210; break;
        }
        return result;
    }
}
