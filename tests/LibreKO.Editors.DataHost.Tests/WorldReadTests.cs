using System.Text.Json;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.DataHost;
using LibreKO.Editors.DataHost.World;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;

[Collection("World database")]
public class WorldReadTests
{
    [Fact] public async Task BatchedPreviewReadsRetainCompleteTypedIdentitiesAndMissingRows()
    {
        await WorldFixture.Ensure();var service=new WorldDatabase(WorldFixture.Cs,WorldFixture.Scratch());
        WorldKey[] keys=[new(WorldTable.Npcs,880019,IsMonster:true),new(WorldTable.Npcs,880019,IsMonster:false),new(WorldTable.Npcs,1999999999,IsMonster:false),new(WorldTable.ZoneInfos,210)];
        var found=await service.GetBatchAsync(keys);Assert.Equal(3,found.Count);Assert.Equal(2,found.Count(r=>r.Key.Table==WorldTable.Npcs));
        Assert.Contains(found,r=>r.Key.IsMonster==true&&r.Values["Name"]!.ToString()=="Monster fixture");
        Assert.Contains(found,r=>r.Key.IsMonster==false&&r.Values["Name"]!.ToString()=="NPC fixture");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.GetBatchAsync(Enumerable.Repeat(keys[0],101).ToArray()));
    }
    [Fact]
    public async Task PagesAllWorldTablesAndScopesDuplicateNpcReferences()
    {
        await WorldFixture.Ensure(); var history = WorldFixture.Scratch(); var service = new WorldDatabase(WorldFixture.Cs, history);
        Assert.Equal(15, service.Catalog().Tables.Count);
        var monsterKey = new WorldKey(WorldTable.Npcs, WorldFixture.NpcId, IsMonster: true);
        Assert.Equal("Monster fixture", (await service.GetAsync(monsterKey))!.Values["Name"]!.ToString());
        Assert.Equal("NPC fixture", (await service.GetAsync(monsterKey with { IsMonster = false }))!.Values["Name"]!.ToString());
        var zoneSpawns = await service.ListAsync(new(WorldTable.NpcPositions, Zone: 210));
        Assert.All(zoneSpawns.Rows, r => Assert.Equal(210, FieldValueParser.ReadInt32(r.Values["ZoneId"])));
        Assert.True(zoneSpawns.Total >= 106);
        var first = await service.ListAsync(new(WorldTable.NpcPositions, "Monster fixture", Size: 1, Zone: 210));
        var second = await service.ListAsync(new(WorldTable.NpcPositions, "Monster fixture", Page: 1, Size: 1, Zone: 210));
        Assert.True(first.Total >= 106); Assert.NotEqual(Assert.Single(first.Rows).Key, Assert.Single(second.Rows).Key);
        var typed = await service.ListAsync(new(WorldTable.Npcs, WorldFixture.NpcId.ToString())); Assert.Equal(2, typed.Total);
        Assert.Single((await service.ListAsync(new(WorldTable.Npcs, WorldFixture.NpcId.ToString(), IsMonster: false))).Rows);
        var owned = await service.ListAsync(new(WorldTable.NpcPositions, IsMonster: true, ParentId: WorldFixture.NpcId));
        Assert.True(owned.Total >= 106); Assert.All(owned.Rows, row => Assert.True(FieldValueParser.ReadInt32(row.Values["ActType"]) < 100));
        Assert.Contains((await service.ListAsync(new(WorldTable.Npcs, IsMonster: true, ParentId: WorldFixture.DropId))).Rows, row => row.Key == monsterKey);
        var a = await service.GetAsync(new(WorldTable.GameEvents, 7, 210)); var b = await service.GetAsync(new(WorldTable.GameEvents, 7, 212));
        Assert.NotEqual(a!.Version, b!.Version);
        var pet = await service.GetAsync(new(WorldTable.PetLevels, 201)); Assert.Equal(9007199254740993L, pet!.Values["Exp"]!.GetValue<long>());
        foreach (var table in Enum.GetValues<WorldTable>()) Assert.NotEmpty((await service.ListAsync(new(table, Size: 1))).Rows);
        Assert.Empty(Directory.GetFiles(history, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task DependentMembershipsAreCompleteAndUseTheOwnerType()
    {
        await WorldFixture.Ensure(); await using var db = EventFixture.Open();
        var monsters = await WorldDependencies.CaptureAsync(db, new(WorldTable.Npcs, WorldFixture.NpcId, IsMonster: true), "npc-spawns");
        var npcs = await WorldDependencies.CaptureAsync(db, new(WorldTable.Npcs, WorldFixture.NpcId, IsMonster: false), "npc-spawns");
        var expectedMonsterSpawns = await db.NpcPositions.Where(x => x.NpcId == WorldFixture.NpcId && x.ActType < 100).Select(x => x.Index).ToArrayAsync();
        Assert.True(expectedMonsterSpawns.Length >= 106);
        Assert.Equal(expectedMonsterSpawns.Order(), monsters.BeforeKeys.Select(x => x.Id).Order()); Assert.Single(npcs.BeforeKeys);
        Assert.DoesNotContain(monsters.BeforeKeys, key => npcs.BeforeKeys.Contains(key));
        var pool = await WorldDependencies.CaptureAsync(db, new(WorldTable.MakeItemGroups, WorldFixture.PoolId), "pool-users");
        Assert.Equal(2, pool.BeforeKeys.Count); Assert.All(pool.BeforeKeys, key => Assert.NotNull(key.IsMonster));
        var events = await WorldDependencies.CaptureAsync(db, new(WorldTable.ZoneInfos, 210), "zone-events");
        Assert.All(events.BeforeKeys, key => Assert.Equal(210, key.Zone));
        await Assert.ThrowsAsync<InvalidOperationException>(() => WorldDependencies.CaptureAsync(db, new(WorldTable.ZoneInfos, 210), "ZoneId; DROP TABLE Npcs"));
    }

    [Fact]
    public async Task SchemaFailuresAndNamesAreScopedIndependently()
    {
        await WorldFixture.Ensure();
        var cs = await WorldFixture.Schema("worldonly", true);
        var service = new WorldDatabase(cs, WorldFixture.Scratch());
        var report = await service.CheckCompatibilityAsync(); Assert.All(report.Tables, table => Assert.True(table.CanBrowse, table.Reason));
        Assert.Contains(report.Tables.SelectMany(x => x.Issues), issue => issue.Code == "schema.lookup");
        Assert.Empty((await service.ListAsync(new(WorldTable.ZoneInfos))).Rows);
        var broken = await WorldFixture.Schema("worldpartial", false);
        var partial = new WorldDatabase(broken, WorldFixture.Scratch()); var state = await partial.CheckCompatibilityAsync();
        Assert.False(state.Tables.Single(x => x.Table == WorldTable.Npcs).CanBrowse);
        Assert.True(state.Tables.Single(x => x.Table == WorldTable.ZoneInfos).CanBrowse);
        Assert.Empty((await partial.ListAsync(new(WorldTable.ZoneInfos))).Rows);
        var names = await new EditorDatabase(broken, WorldFixture.Scratch()).ResolveNamedAsync(new([new("npcs", WorldFixture.NpcId, IsMonster: true)]));
        Assert.Equal("Minimal lookup fixture", Assert.Single(names).Label);
        var items = await new EditorDatabase(broken, WorldFixture.Scratch()).ResolveNamedAsync(new([new("items", 123450001)]));
        Assert.Equal("Minimal item name", Assert.Single(items).Label); Assert.Null(items[0].Details);
    }

    [Fact]
    public async Task WorldCatalogIsAvailableThroughTheDisconnectedHelper()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "scripts/build.ps1"))) root = root.Parent;
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(root!.FullName, "src/LibreKO.Editors.DataHost/bin/Debug/net10.0/LibreKO.Editors.DataHost.dll"));
        using var process = Process.Start(start)!; using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var id = Guid.NewGuid();
        try
        {
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new RpcRequest(1, id, "world.catalog", JsonSerializer.SerializeToElement(new { })), JsonConfig.Options));
            var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
            var reply = JsonSerializer.Deserialize<RpcResponse>(line!, JsonConfig.Options)!;
            Assert.Equal(id, reply.Id); Assert.Null(reply.Error); Assert.Equal(15, reply.Result!.Value.Deserialize<WorldCatalog>(JsonConfig.Options)!.Tables.Count);
        }
        finally { process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token); }
    }

    [Fact]
    public async Task UnusedIdsRespectTypedScopesAndByteShortBoundsWithoutWrites()
    {
        await WorldFixture.Ensure(); var service = new WorldDatabase(WorldFixture.Cs, WorldFixture.Scratch());
        var next = await service.NextIdAsync(WorldTable.NpcItems, isMonster: true, reserved: [1, 2, 3]);
        Assert.InRange(next, 4, short.MaxValue); Assert.Null(await service.GetAsync(new(WorldTable.NpcItems, next, IsMonster: true)));
        var level = await service.NextIdAsync(WorldTable.PetLevels, reserved: [1, 2, 3]); Assert.InRange(level, 4, 255);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.NextIdAsync(WorldTable.PetLevels, reserved: Enumerable.Range(1, 255).ToArray()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.NextIdAsync(WorldTable.Npcs));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync(new(WorldTable.Npcs, WorldFixture.NpcId, Zone: 210, IsMonster: true)));
    }
}
