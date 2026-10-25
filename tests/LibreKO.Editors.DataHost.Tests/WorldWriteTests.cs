using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.DataHost.World;
using MySqlConnector;

[Collection("World database")]
public class WorldWriteTests
{
    public static IEnumerable<object[]> Tables() => Enum.GetValues<WorldTable>().Select(t => new object[] { t });
    internal static WorldChange Change(params WorldRowChange[] rows) => new(Guid.NewGuid(), rows, []);
    internal static WorldRowChange Edit(WorldRowSnapshot row, string field, JsonNode value)
    { var after = (JsonObject)row.Values.DeepClone(); after[field] = value; return new(row.Key, row, after); }

    [Theory, MemberData(nameof(Tables))]
    public async Task CreatesUpdatesDuplicatesAndDeletesEveryMappedTable(WorldTable table)
    {
        await WorldFixture.Ensure(); var journal = new TestWorldJournal(); var service = new WorldDatabase(WorldFixture.Cs, WorldFixture.Scratch(), journal);
        var key = await WorldFixture.FreeKey(service, table); var values = WorldFixture.Values(key);
        var parents = new List<WorldRowChange>();
        async Task Parent(WorldKey child)
        {
            if (child.Table == WorldTable.StartPositions && await service.GetAsync(new(WorldTable.ZoneInfos, child.Id)) == null)
                parents.Add(new(new(WorldTable.ZoneInfos, child.Id), null, WorldFixture.Values(new(WorldTable.ZoneInfos, child.Id))));
        }
        await Parent(key);
        var created = await service.SaveAsync(Change(parents.Append(new(key, null, values)).ToArray()));
        Assert.True(created.Status == SaveStatus.Saved, created.Message + string.Join(";", created.Issues ?? []));
        var original = (await service.GetAsync(key))!;
        var field = service.Catalog().Tables.Single(t => t.Table == table).Fields.First(f => f.Field.Writable && f.Field.Reference == null && f.Field.Kind != FieldKind.Boolean && f.Field.Choices == null && f.Field.Name is not ("Path" or "DotCnt"));
        JsonNode value = field.Field.Kind == FieldKind.Text ? JsonValue.Create(table == WorldTable.MakeItemGroups ? "123450001" : "Edited fixture")! : JsonValue.Create(1)!;
        Assert.Equal(SaveStatus.Saved, (await service.SaveAsync(Change(Edit(original, field.Field.Name, value)))).Status);
        if (table != WorldTable.Homes)
        {
            var copy = await WorldFixture.FreeKey(service, table, key.Zone, key.IsMonster); var copied = WorldFixture.Values(copy);
            var beforeParents = parents.Count; await Parent(copy);
            Assert.Equal(SaveStatus.Saved, (await service.SaveAsync(Change(parents.Skip(beforeParents).Append(new(copy, null, copied)).ToArray()))).Status);
            Assert.Equal(SaveStatus.Saved, (await service.SaveAsync(Change(new WorldRowChange(copy, await service.GetAsync(copy), null)))).Status);
        }
        Assert.Equal(SaveStatus.Saved, (await service.SaveAsync(Change(new WorldRowChange(key, await service.GetAsync(key), null)))).Status);
        foreach (var parent in parents) Assert.Equal(SaveStatus.Saved, (await service.SaveAsync(Change(new WorldRowChange(parent.Key, await service.GetAsync(parent.Key), null)))).Status);
        Assert.Null(await service.GetAsync(key)); Assert.True(journal.Prepared >= 3); Assert.Equal(journal.Prepared, journal.Committed);
    }

    [Fact]
    public async Task StaleRowsTypedReferencesAndDeletionDependenciesAreGuarded()
    {
        await WorldFixture.Ensure(); var service = new WorldDatabase(WorldFixture.Cs, WorldFixture.Scratch(), new TestWorldJournal());
        var spawn = (await service.GetAsync(new(WorldTable.NpcPositions, WorldFixture.SpawnId)))!;
        Assert.Equal(SaveStatus.Invalid, (await service.SaveAsync(Change(Edit(spawn, "NpcId", JsonValue.Create(1999999999)!)))).Status);
        var monster = (await service.GetAsync(new(WorldTable.Npcs, WorldFixture.NpcId, IsMonster: true)))!;
        Assert.Equal(SaveStatus.Conflict, (await service.SaveAsync(Change(new WorldRowChange(monster.Key, monster, null)))).Status);
        var edited = Edit(spawn, "RegTime", JsonValue.Create(int.Parse(spawn.Values["RegTime"]!.ToString()) + 1)!);
        Assert.Equal(SaveStatus.Saved, (await service.SaveAsync(Change(edited))).Status);
        Assert.Equal(SaveStatus.Conflict, (await service.SaveAsync(Change(edited))).Status);
        var mapEvent = (await service.GetAsync(new(WorldTable.GameEvents, 7, 212)))!;
        // Type3 has no zone operand. Type1 turns the unchanged zero Exec1 into a zone reference.
        var typed = Edit(mapEvent, "Type", JsonValue.Create(1)!);
        Assert.Equal(SaveStatus.Invalid, (await service.SaveAsync(Change(typed))).Status);
        var wrong = (JsonObject)spawn.Values.DeepClone(); wrong["Index"] = EventFixture.Id();
        Assert.Equal(SaveStatus.Invalid, (await service.SaveAsync(Change(new WorldRowChange(spawn.Key, spawn, wrong)))).Status);
    }

    [Fact]
    public async Task RequiredCustomColumnsPermitUpdatesButRefuseInsertAndDestructiveHistory()
    {
        await WorldFixture.Ensure(); var cs = await WorldFixture.Schema("worldextra", true);
        var service = new WorldDatabase(cs, WorldFixture.Scratch(), new TestWorldJournal());
        var key = new WorldKey(WorldTable.Npcs, EventFixture.Id(), IsMonster: true);
        Assert.Equal(SaveStatus.Saved, (await service.SaveAsync(Change(new WorldRowChange(key, null, WorldFixture.Values(key))))).Status);
        await using var c = new MySqlConnection(cs); await c.OpenAsync();
        await new MySqlCommand("ALTER TABLE Npcs ADD COLUMN VendorTag int NOT NULL", c).ExecuteNonQueryAsync();
        await new MySqlCommand("UPDATE Npcs SET VendorTag=42", c).ExecuteNonQueryAsync();
        var original = (await service.GetAsync(key))!;
        Assert.Equal(SaveStatus.Saved, (await service.SaveAsync(Change(Edit(original, "Name", JsonValue.Create("Custom name")!)))).Status);
        Assert.Equal(42, Convert.ToInt32(await new MySqlCommand("SELECT VendorTag FROM Npcs", c).ExecuteScalarAsync()));
        Assert.Equal(SaveStatus.Invalid, (await service.SaveAsync(Change(new WorldRowChange(key, await service.GetAsync(key), null)))).Status);
        var next = key with { Id = key.Id + 1 };
        Assert.Equal(SaveStatus.Invalid, (await service.SaveAsync(Change(new WorldRowChange(next, null, WorldFixture.Values(next))))).Status);
    }

    [Fact]
    public async Task OversizedAndPrepareFailuresNeverWrite()
    {
        await WorldFixture.Ensure(); var journal = new TestWorldJournal { FailPrepare = true };
        var service = new WorldDatabase(WorldFixture.Cs, WorldFixture.Scratch(), journal);
        var key = await WorldFixture.FreeKey(service, WorldTable.Npcs); var values = WorldFixture.Values(key);
        Assert.Equal(SaveStatus.Invalid, (await service.SaveAsync(Change(new WorldRowChange(key, null, values)))).Status); Assert.Null(await service.GetAsync(key));
        values["Name"] = new string('x', 4 * 1024 * 1024);
        var result = await service.SaveAsync(Change(new WorldRowChange(key, null, values)));
        Assert.Equal(SaveStatus.Invalid, result.Status); Assert.Contains("4 MiB", result.Message); Assert.Equal(0, journal.Prepared);
    }

    [Fact]
    public async Task ReceiptFailureReportsUnknownCommitWithoutAbortingSavedRows()
    {
        await WorldFixture.Ensure(); var journal = new TestWorldJournal { FailCommit = true };
        var service = new WorldDatabase(WorldFixture.Cs, WorldFixture.Scratch(), journal);
        var key = await WorldFixture.FreeKey(service, WorldTable.Npcs);
        var result = await service.SaveAsync(Change(new WorldRowChange(key, null, WorldFixture.Values(key))));
        Assert.Equal(SaveStatus.UnknownCommit, result.Status); Assert.NotNull(await service.GetAsync(key)); Assert.Equal(0, journal.Aborted);
    }
}

internal sealed class TestWorldJournal : IWorldJournal
{
    internal int Prepared, Committed, Aborted; internal bool FailPrepare, FailCommit;
    public void Prepare(WorldChange c) { if (FailPrepare) throw new IOException("prepare failure"); Prepared++; }
    public void Commit(WorldChange c, IReadOnlyList<WorldRowSnapshot> after) { if (FailCommit) throw new IOException("commit receipt failure"); Committed++; }
    public void Abort(Guid id) => Aborted++;
}
