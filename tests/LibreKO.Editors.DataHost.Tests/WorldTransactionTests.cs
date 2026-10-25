using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Events;
using LibreKO.Editors.DataHost.Events;
using LibreKO.Editors.DataHost.World;
using MySqlConnector;

[Collection("World database")]
public class WorldTransactionTests
{
    [Fact]
    public async Task WorldBatchRollsBackAndSharesEventConflictRules()
    {
        await WorldFixture.Ensure(); var journal = new TestWorldJournal(); var world = new WorldDatabase(WorldFixture.Cs, WorldFixture.Scratch(), journal);
        var key = await WorldFixture.FreeKey(world, WorldTable.MapObjects);
        Assert.Equal(SaveStatus.Saved, (await world.SaveAsync(WorldWriteTests.Change(new WorldRowChange(key, null, WorldFixture.Values(key))))).Status);
        var events = new EventDatabase(WorldFixture.Cs, WorldFixture.Scratch(), new TestEventJournal());
        var oldEvent = (await events.GetAsync(new(EventFamily.MapObject, key.Id)))!;
        var otherZone = (await world.GetAsync(new(WorldTable.GameEvents, 7, 212)))!;
        var row = (await world.GetAsync(key))!;
        Assert.Equal(SaveStatus.Saved, (await world.SaveAsync(WorldWriteTests.Change(WorldWriteTests.Edit(row, "X", JsonValue.Create(88)!)))).Status);
        var stale = new EventDraft(oldEvent); stale.Set(oldEvent.Rows[0].Key, "X", 99);
        Assert.Equal(SaveStatus.Conflict, (await events.SaveAsync(stale.BuildChange(Guid.NewGuid()))).Status);
        Assert.Equal(otherZone.Version, (await world.GetAsync(otherZone.Key))!.Version);
        WorldKey[] keys = [new(WorldTable.Npcs, WorldFixture.NpcId, IsMonster: true), new(WorldTable.NpcItems, WorldFixture.DropId, IsMonster: true), new(WorldTable.NpcPositions, WorldFixture.SpawnId), key];
        var before = new List<WorldRowSnapshot>(); foreach (var k in keys) before.Add((await world.GetAsync(k))!);
        var changes = before.Zip(new[] { "Hp", "Percent1", "RegTime", "X" }, (r, f) => WorldWriteTests.Edit(r, f, JsonValue.Create(777)!)).ToArray();
        await using var c = new MySqlConnection(WorldFixture.Cs); await c.OpenAsync(); var trigger = "editor_test_world_" + key.Id;
        await new MySqlCommand("CREATE TRIGGER " + trigger + " BEFORE UPDATE ON MapObjects FOR EACH ROW BEGIN IF NEW.Id=" + key.Id + " THEN SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT='world fixture rollback'; END IF; END", c).ExecuteNonQueryAsync();
        try
        {
            var result = await world.SaveAsync(WorldWriteTests.Change(changes));
            Assert.Equal(SaveStatus.Invalid, result.Status);
            foreach (var original in before) Assert.Equal(original.Version, (await world.GetAsync(original.Key))!.Version);
            Assert.True(journal.Aborted > 0, result.Message + string.Join(";", result.Issues ?? []));
        }
        finally { await new MySqlCommand("DROP TRIGGER " + trigger, c).ExecuteNonQueryAsync(); }
    }

    [Fact]
    public async Task NewlyInsertedDependentInvalidatesCompleteMembershipBeforePrepare()
    {
        await WorldFixture.Ensure(); var journal = new TestWorldJournal(); var world = new WorldDatabase(WorldFixture.Cs, WorldFixture.Scratch(), journal);
        var owner = new WorldKey(WorldTable.Npcs, WorldFixture.NpcId, IsMonster: false);
        var membership = await world.MembershipAsync(owner, "npc-spawns");
        var key = await WorldFixture.FreeKey(world, WorldTable.NpcPositions); var values = WorldFixture.Values(key); values["ActType"] = 100;
        Assert.Equal(SaveStatus.Saved, (await world.SaveAsync(WorldWriteTests.Change(new WorldRowChange(key, null, values)))).Status);
        var row = (await world.GetAsync(owner))!; var prepared = journal.Prepared;
        var result = await world.SaveAsync(new(Guid.NewGuid(), [WorldWriteTests.Edit(row, "Name", JsonValue.Create("Must not save")!)], [membership]));
        Assert.Equal(SaveStatus.Conflict, result.Status); Assert.Equal(prepared, journal.Prepared);
        Assert.Equal(SaveStatus.Saved, (await world.SaveAsync(WorldWriteTests.Change(new WorldRowChange(key, await world.GetAsync(key), null)))).Status);
    }

    [Fact]
    public async Task ZoneRemovalAlsoChecksMapEventDestinationsInOtherZones()
    {
        await WorldFixture.Ensure(); var cs = await WorldFixture.Schema("worldlinks", true); var world = new WorldDatabase(cs, WorldFixture.Scratch(), new TestWorldJournal());
        var baseZone = new WorldKey(WorldTable.ZoneInfos, 210);
        Assert.Equal(SaveStatus.Saved, (await world.SaveAsync(WorldWriteTests.Change(new WorldRowChange(baseZone, null, WorldFixture.Values(baseZone))))).Status);
        var zone = await WorldFixture.FreeKey(world, WorldTable.ZoneInfos); var evt = await WorldFixture.FreeKey(world, WorldTable.GameEvents);
        var values = WorldFixture.Values(evt); values["Type"] = 1; values["Exec1"] = zone.Id;
        Assert.Equal(SaveStatus.Saved, (await world.SaveAsync(WorldWriteTests.Change(new(zone, null, WorldFixture.Values(zone)), new(evt, null, values)))).Status);
        Assert.Equal(SaveStatus.Conflict, (await world.SaveAsync(WorldWriteTests.Change(new WorldRowChange(zone, await world.GetAsync(zone), null)))).Status);
        Assert.Equal(SaveStatus.Saved, (await world.SaveAsync(WorldWriteTests.Change(new(zone, await world.GetAsync(zone), null), new(evt, await world.GetAsync(evt), null)))).Status);
    }

    [Fact]
    public async Task SqlCascadeRequiresEveryKnownChildAndRefusesUnknownChildren()
    {
        await WorldFixture.Ensure(); var cs = await WorldFixture.Schema("worldfk", true); var world = new WorldDatabase(cs, WorldFixture.Scratch(), new TestWorldJournal());
        var zone = new WorldKey(WorldTable.ZoneInfos, 213); var start = new WorldKey(WorldTable.StartPositions, 213);
        await using var c = new MySqlConnection(cs); await c.OpenAsync();
        await new MySqlCommand("ALTER TABLE StartPositions ADD CONSTRAINT fixture_start_fk FOREIGN KEY(ZoneId) REFERENCES ZoneInfos(ZoneNo) ON DELETE CASCADE", c).ExecuteNonQueryAsync();
        Assert.Equal(SaveStatus.Saved, (await world.SaveAsync(WorldWriteTests.Change(new(zone, null, WorldFixture.Values(zone)), new(start, null, WorldFixture.Values(start))))).Status);
        Assert.Equal(SaveStatus.Conflict, (await world.SaveAsync(WorldWriteTests.Change(new WorldRowChange(zone, await world.GetAsync(zone), null)))).Status);
        Assert.Equal(SaveStatus.Saved, (await world.SaveAsync(WorldWriteTests.Change(new(zone, await world.GetAsync(zone), null), new(start, await world.GetAsync(start), null)))).Status);
        Assert.Equal(SaveStatus.Saved, (await world.SaveAsync(WorldWriteTests.Change(new WorldRowChange(zone, null, WorldFixture.Values(zone))))).Status);
        await new MySqlCommand("CREATE TABLE CustomZoneUse(Id int NOT NULL PRIMARY KEY,ZoneId smallint NOT NULL,FOREIGN KEY(ZoneId) REFERENCES ZoneInfos(ZoneNo) ON DELETE CASCADE) ENGINE=InnoDB", c).ExecuteNonQueryAsync();
        await new MySqlCommand("INSERT INTO CustomZoneUse VALUES(1,213)", c).ExecuteNonQueryAsync();
        Assert.Equal(SaveStatus.Conflict, (await world.SaveAsync(WorldWriteTests.Change(new WorldRowChange(zone, await world.GetAsync(zone), null)))).Status);
        Assert.Equal(1, Convert.ToInt32(await new MySqlCommand("SELECT COUNT(*) FROM CustomZoneUse", c).ExecuteScalarAsync()));
    }

    [Fact]
    public async Task SharedEventRouteRevalidatesZeroWhenChangingOperandType()
    {
        await WorldFixture.Ensure(); var world = new WorldDatabase(WorldFixture.Cs, WorldFixture.Scratch(), new TestWorldJournal());
        var key = await WorldFixture.FreeKey(world, WorldTable.GameEvents);
        Assert.Equal(SaveStatus.Saved, (await world.SaveAsync(WorldWriteTests.Change(new WorldRowChange(key, null, WorldFixture.Values(key))))).Status);
        var events = new EventDatabase(WorldFixture.Cs, WorldFixture.Scratch(), new TestEventJournal());
        var draft = new EventDraft((await events.GetAsync(new(EventFamily.MapEvent, key.Id, key.Zone)))!);
        draft.Set(draft.Rows[0].Key, "Type", 1);
        Assert.Equal(SaveStatus.Invalid, (await events.SaveAsync(draft.BuildChange(Guid.NewGuid()))).Status);
    }

    [Fact]
    public async Task NpcRemovalChecksDatabaseBackedEventObjectives()
    {
        await WorldFixture.Ensure(); var world = new WorldDatabase(WorldFixture.Cs, WorldFixture.Scratch(), new TestWorldJournal());
        var key = await WorldFixture.FreeKey(world, WorldTable.Npcs);
        Assert.Equal(SaveStatus.Saved, (await world.SaveAsync(WorldWriteTests.Change(new WorldRowChange(key, null, WorldFixture.Values(key))))).Status);
        var events = new EventDatabase(WorldFixture.Cs, WorldFixture.Scratch(), new TestEventJournal());
        var race = EventFixture.Race(EventFixture.Id()); race.Set(race.Rows.Single(r => r.Key.Table == EventTable.CollectionRaceObjectives).Key, "TargetId", key.Id);
        Assert.Equal(SaveStatus.Saved, (await events.SaveAsync(race.BuildChange(Guid.NewGuid()))).Status);
        Assert.Equal(SaveStatus.Conflict, (await world.SaveAsync(WorldWriteTests.Change(new WorldRowChange(key, await world.GetAsync(key), null)))).Status);
        var remove = new EventDraft((await events.GetAsync(race.Original.Key))!); foreach (var row in remove.Rows.ToArray()) remove.Remove(row.Key);
        Assert.Equal(SaveStatus.Saved, (await events.SaveAsync(remove.BuildChange(Guid.NewGuid()))).Status);
        Assert.Equal(SaveStatus.Saved, (await world.SaveAsync(WorldWriteTests.Change(new WorldRowChange(key, await world.GetAsync(key), null)))).Status);
    }
}
