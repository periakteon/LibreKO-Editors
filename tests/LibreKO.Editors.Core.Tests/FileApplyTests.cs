using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;

public class FileApplyTests
{
    [Fact]
    public async Task NoOpRetainsOriginalTimestampAndDuplicateOperationIsRefused()
    {
        using var f = SourceFixture.Create();
        var path = f.Write(SourceRoot.ServerData, "a.json", [239, 187, 191, 13, 10]);
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);
        var store = new SourceStore(f.Profile, f.WorkspaceRoot);
        var change = new FileChange(Guid.NewGuid(), [store.Stage(f.Key(SourceRoot.ServerData, "a.json"), File.ReadAllBytes(path))]);
        var apply = new FileApply(f.Profile, f.WorkspaceRoot, f.Validators());
        Assert.Equal(FileApplyState.Applied, (await apply.ApplyAsync(change)).State);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        Assert.Equal(FileApplyState.Invalid, (await apply.ApplyAsync(change)).State);
    }

    [Fact]
    public async Task CreatesDeletesAndRestoresExactBytesAfterRestart()
    {
        using var f = SourceFixture.Create();
        var removedPath = f.Write(SourceRoot.ServerData, "removed.json", [0, 1, 2, 239, 187, 191]);
        var store = new SourceStore(f.Profile, f.WorkspaceRoot);
        var createdKey = f.Key(SourceRoot.ServerData, "nested/new.json");
        var removedKey = f.Key(SourceRoot.ServerData, "removed.json");
        var change = new FileChange(Guid.NewGuid(), [store.Stage(createdKey, [3, 4]), store.Stage(removedKey, null)]);
        Assert.Equal(FileApplyState.Applied, (await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators()).ApplyAsync(change)).State);
        Assert.False(File.Exists(removedPath));
        Assert.Equal(new byte[] { 3, 4 }, File.ReadAllBytes(SourcePaths.Resolve(f.Profile, createdKey, false)));
        var history = new FileHistory(f.Profile, f.WorkspaceRoot);
        Assert.Single(history.List());
        var reverse = history.PrepareReverse(change.OperationId);
        Assert.NotEqual(change.OperationId, reverse.OperationId);
        Assert.Equal(FileApplyState.Applied, (await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators()).ApplyAsync(reverse)).State);
        Assert.False(File.Exists(SourcePaths.Resolve(f.Profile, createdKey, false)));
        Assert.Equal(new byte[] { 0, 1, 2, 239, 187, 191 }, File.ReadAllBytes(removedPath));
        Assert.Equal(2, new FileHistory(f.Profile, f.WorkspaceRoot).List().Count);
    }

    [Fact]
    public async Task AllFilesAreValidatedAndPreparedBeforeAnySourceWrite()
    {
        using var f = SourceFixture.Create();
        var path = f.Write(SourceRoot.ServerData, "a.json", [1]);
        var store = new SourceStore(f.Profile, f.WorkspaceRoot);
        var change = new FileChange(Guid.NewGuid(), [store.Stage(f.Key(SourceRoot.ServerData, "a.json"), [2]), store.Stage(f.Key(SourceRoot.ClientProject, "src/Boot.cs"), [3])]);
        var result = await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators()).ApplyAsync(change);
        Assert.Equal(FileApplyState.Invalid, result.State);
        Assert.NotEmpty(result.Issues);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path));
        Assert.Empty(new FileHistory(f.Profile, f.WorkspaceRoot).List());
        var good = change with { OperationId = Guid.NewGuid(), Files = [change.Files[0]] };
        var failed = await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators(), (step, _) => { if (step == "prepared") throw new IOException("Simulated disk failure"); }).ApplyAsync(good);
        Assert.Equal(FileApplyState.NotApplied, failed.State);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task CorruptStagedBlobIsRejectedBeforePreparingOrWriting()
    {
        using var f = SourceFixture.Create();
        var path = f.Write(SourceRoot.ServerData, "a.json", [1]);
        var mutation = new SourceStore(f.Profile, f.WorkspaceRoot).Stage(f.Key(SourceRoot.ServerData, "a.json"), [2]);
        var blobPath = Directory.GetFiles(f.WorkspaceRoot, mutation.After!.Sha256, SearchOption.AllDirectories).Single();
        File.WriteAllBytes(blobPath, [3]);
        var result = await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators()).ApplyAsync(new(Guid.NewGuid(), [mutation]));
        Assert.Equal(FileApplyState.Invalid, result.State);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path));
        Assert.Empty(new FileHistory(f.Profile, f.WorkspaceRoot).List());
    }

    [Fact]
    public async Task ExpectedAbsenceAndHashConflictsNeverOverwrite()
    {
        using var f = SourceFixture.Create();
        var store = new SourceStore(f.Profile, f.WorkspaceRoot);
        var key = f.Key(SourceRoot.ServerData, "a.json");
        var change = new FileChange(Guid.NewGuid(), [store.Stage(key, [2])]);
        var path = f.Write(SourceRoot.ServerData, "a.json", [7]);
        var result = await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators()).ApplyAsync(change);
        Assert.Equal(FileApplyState.Conflict, result.State);
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(path));
        var update = new FileChange(Guid.NewGuid(), [store.Stage(key, [3])]);
        File.WriteAllBytes(path, [8]);
        Assert.Equal(FileApplyState.Conflict, (await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators()).ApplyAsync(update)).State);
        Assert.Equal(new byte[] { 8 }, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task ReadOnlyTargetRetainsBytesAndReportsAnUnappliedOutcome()
    {
        using var f = SourceFixture.Create();
        var path = f.Write(SourceRoot.ServerData, "a.json", [1]);
        var mutation = new SourceStore(f.Profile, f.WorkspaceRoot).Stage(f.Key(SourceRoot.ServerData, "a.json"), [2]);
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
        try
        {
            var result = await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators()).ApplyAsync(new(Guid.NewGuid(), [mutation]));
            Assert.Equal(FileApplyState.NotApplied, result.State);
            Assert.Equal(FileApplyState.NotApplied, Assert.Single(result.Targets).State);
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(path));
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }
}
