using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.DataHost.World;

[Collection("World database")]
public class WorldHistoryDatabaseTests
{
    [Fact]
    public async Task WorldHistoryNeverCrossesScopesOrRemovesLaterDependents()
    {
        await WorldFixture.Ensure(); var root = WorldFixture.Scratch(); var journal = new WorldJournal(root);
        var service = new WorldDatabase(WorldFixture.Cs, root, new LostWorldReceipt(journal));
        var key = await WorldFixture.FreeKey(service, WorldTable.Npcs);
        var create = WorldWriteTests.Change(new WorldRowChange(key, null, WorldFixture.Values(key)));
        Assert.Equal(SaveStatus.UnknownCommit, (await service.SaveAsync(create)).Status);
        Assert.Single(journal.Pending()); Assert.Equal(RecoveryDisposition.Applied, await service.ReconcileAsync(create.OperationId));
        Assert.Empty(journal.Pending()); Assert.Single(await service.HistoryAsync());
        var normal = new WorldDatabase(WorldFixture.Cs, root);
        var spawn = await WorldFixture.FreeKey(normal, WorldTable.NpcPositions); var values = WorldFixture.Values(spawn); values["NpcId"] = key.Id;
        Assert.Equal(SaveStatus.Saved, (await normal.SaveAsync(WorldWriteTests.Change(new WorldRowChange(spawn, null, values)))).Status);
        Assert.Equal(SaveStatus.Conflict, (await normal.ReplayAsync(create.OperationId, true)).Status);
        var other = new WorldDatabase(WorldFixture.Cs, WorldFixture.Scratch());
        Assert.Equal(SaveStatus.Invalid, (await other.ReplayAsync(create.OperationId, true)).Status);
        Assert.Equal(SaveStatus.Saved, (await normal.SaveAsync(WorldWriteTests.Change(new WorldRowChange(spawn, await normal.GetAsync(spawn), null)))).Status);
        Assert.Equal(SaveStatus.Saved, (await normal.ReplayAsync(create.OperationId, true)).Status);
        Assert.Null(await normal.GetAsync(key));
    }

    [Fact]
    public async Task PreparedUnappliedAndMixedBatchesReconcileWithoutOverwriting()
    {
        await WorldFixture.Ensure(); var root = WorldFixture.Scratch(); var journal = new WorldJournal(root); var service = new WorldDatabase(WorldFixture.Cs, root);
        var a = await WorldFixture.FreeKey(service, WorldTable.MapObjects); var b = a with { Id = a.Id + 1 };
        var create = WorldWriteTests.Change(new(a, null, WorldFixture.Values(a)), new(b, null, WorldFixture.Values(b)));
        journal.Prepare(create); Assert.Equal(RecoveryDisposition.NotApplied, await service.ReconcileAsync(create.OperationId));
        Assert.Equal(SaveStatus.Saved, (await service.SaveAsync(create with { OperationId = Guid.NewGuid() })).Status);
        var rowA = (await service.GetAsync(a))!; var rowB = (await service.GetAsync(b))!;
        var mixed = WorldWriteTests.Change(WorldWriteTests.Edit(rowA, "X", JsonValue.Create(71)!), WorldWriteTests.Edit(rowB, "X", JsonValue.Create(72)!)); journal.Prepare(mixed);
        var direct = new WorldDatabase(WorldFixture.Cs, WorldFixture.Scratch(), new TestWorldJournal());
        Assert.Equal(SaveStatus.Saved, (await direct.SaveAsync(WorldWriteTests.Change(mixed.Rows[0]))).Status);
        Assert.Equal(RecoveryDisposition.Conflict, await service.ReconcileAsync(mixed.OperationId));
        Assert.Single(journal.Pending()); Assert.Equal(rowB.Version, (await service.GetAsync(b))!.Version);
        Assert.Equal(SaveStatus.Invalid, (await service.SaveAsync(WorldWriteTests.Change(mixed.Rows[1]))).Status);
    }

    sealed class LostWorldReceipt(WorldJournal inner) : IWorldJournal
    { public void Prepare(WorldChange c) => inner.Prepare(c); public void Commit(WorldChange c, IReadOnlyList<WorldRowSnapshot> after) => throw new IOException("lost receipt"); public void Abort(Guid id) => inner.Abort(id); }
}
