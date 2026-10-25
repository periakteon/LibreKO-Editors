using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.World;

public class WorldHistoryTests
{
    static WorldRowSnapshot Row(int id, int a, int b) { var key = new WorldKey(WorldTable.MapObjects, id); var values = new JsonObject { ["Id"] = id, ["X"] = a, ["Z"] = b }; return new(key, values, RecordFingerprint.Compute(values)); }
    [Fact]
    public void WorldHistoryNeverCrossesScopesAndReservesOperationEvidence()
    {
        var root = Directory.CreateTempSubdirectory("world-history-").FullName; var journal = new WorldJournal(root);
        var before = Row(4, 1, 2); var after = Row(4, 8, 2);
        var change = new WorldChange(Guid.NewGuid(), [new(before.Key, before, after.Values)], []);
        journal.Prepare(change); Assert.Single(journal.Pending());
        Assert.Throws<IOException>(() => journal.Prepare(change));
        journal.Commit(change, [after]); Assert.Empty(journal.Pending());
        var entry = journal.Read(change.OperationId); Assert.Equal(before.Version, entry.Change.Rows[0].Before!.Version);
        var link = Assert.Single(journal.List()); Assert.Equal("World", link.Origin); Assert.Equal(Path.GetFullPath(root), link.ScopeId);
        Assert.Equal("world", Assert.Single(link.Targets).EditorId);
        var other = Directory.CreateTempSubdirectory("world-other-").FullName;
        Directory.CreateDirectory(Path.Combine(other, "world"));
        foreach (var file in Directory.GetFiles(Path.Combine(root, "world"), "*.committed.json")) File.Copy(file, Path.Combine(other, "world", Path.GetFileName(file)));
        Assert.Throws<InvalidDataException>(() => new WorldJournal(other).Read(change.OperationId));
    }

    [Fact]
    public void RecoveryAndReplayPreserveUnrelatedFieldsAndRefuseMixedOutcomes()
    {
        var before = Row(4, 1, 2); var after = Row(4, 8, 2);
        var change = new WorldChange(Guid.NewGuid(), [new(before.Key, before, after.Values)], []);
        Assert.Equal(RecoveryDisposition.NotApplied, WorldRecovery.Resolve(change, [before]));
        Assert.Equal(RecoveryDisposition.Applied, WorldRecovery.Resolve(change, [after]));
        Assert.Equal(RecoveryDisposition.Conflict, WorldRecovery.Resolve(change, [Row(4, 6, 2)]));
        var reverse = WorldReplay.Prepare(change, [Row(4, 8, 99)], true);
        Assert.Equal("1", reverse.Rows[0].After!["X"]!.ToString()); Assert.Equal("99", reverse.Rows[0].After!["Z"]!.ToString());
        Assert.Throws<InvalidOperationException>(() => WorldReplay.Prepare(change, [Row(4, 6, 99)], true));
        var second = Row(5, 1, 2); var secondAfter = Row(5, 9, 2);
        var batch = change with { Rows = [change.Rows[0], new(second.Key, second, secondAfter.Values)] };
        Assert.Equal(RecoveryDisposition.Conflict, WorldRecovery.Resolve(batch, [after, second]));
    }
}
