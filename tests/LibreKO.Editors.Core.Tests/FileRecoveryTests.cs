using System.Diagnostics;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;

public class FileRecoveryTests
{
    [Fact]
    public async Task InterruptedSecondReplacementReconcilesWithoutOverwritingExternalEdit()
    {
        using var f = SourceFixture.Create();
        var firstPath = f.Write(SourceRoot.ServerData, "first.json", [1]);
        var secondPath = f.Write(SourceRoot.ServerData, "second.json", [2]);
        var store = new SourceStore(f.Profile, f.WorkspaceRoot);
        var change = new FileChange(Guid.NewGuid(), [store.Stage(f.Key(SourceRoot.ServerData, "first.json"), [3]), store.Stage(f.Key(SourceRoot.ServerData, "second.json"), [4])]);
        var result = await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators(), (step, _) => { if (step == "after-replace") throw new IOException("Interrupted"); }).ApplyAsync(change);
        Assert.Equal(FileApplyState.Partial, result.State);
        Assert.Equal(FileApplyState.Applied, result.Targets[0].State);
        Assert.Equal(FileApplyState.NotApplied, result.Targets[1].State);
        var history = new FileHistory(f.Profile, f.WorkspaceRoot);
        Assert.Equal(FileApplyState.Partial, history.Reconcile(change.OperationId).State);
        Assert.True(Assert.Single(history.List()).Pending);
        File.WriteAllText(secondPath, "external");
        Assert.Equal(FileApplyState.Conflict, history.Reconcile(change.OperationId).State);
        Assert.Throws<InvalidOperationException>(() => history.PrepareReverse(change.OperationId));
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(firstPath));
        Assert.Equal("external", File.ReadAllText(secondPath));
    }

    [Fact]
    public async Task PendingPhysicalTargetsBlockAliasesButAllowUnrelatedFilesAndGuardedReverse()
    {
        using var f = SourceFixture.Create();
        f.Write(SourceRoot.ServerData, "a.json", [1]);
        f.Write(SourceRoot.ServerData, "b.json", [2]);
        var store = new SourceStore(f.Profile, f.WorkspaceRoot);
        var change = new FileChange(Guid.NewGuid(), [store.Stage(f.Key(SourceRoot.ServerData, "a.json"), [3]), store.Stage(f.Key(SourceRoot.ServerData, "b.json"), [4])]);
        await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators(), (step, _) => { if (step == "after-replace") throw new IOException("Interrupted"); }).ApplyAsync(change);
        var alias = f.Profile with { Id = Guid.NewGuid().ToString("N") };
        var key = f.Key(SourceRoot.ServerData, "a.json") with { ProfileId = alias.Id };
        var aliasStore = new SourceStore(alias, f.WorkspaceRoot);
        var validators = new SourceValidationRegistry(aliasStore.Blobs, alias);
        validators.Register("fixture/1", _ => true, _ => []);
        var blocked = await new FileApply(alias, f.WorkspaceRoot, validators).ApplyAsync(new(Guid.NewGuid(), [aliasStore.Stage(key, [9])]));
        Assert.Equal(FileApplyState.Pending, blocked.State);
        var unrelated = new FileChange(Guid.NewGuid(), [store.Stage(f.Key(SourceRoot.ServerData, "c.json"), [5])]);
        Assert.Equal(FileApplyState.Applied, (await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators()).ApplyAsync(unrelated)).State);
        var history = new FileHistory(f.Profile, f.WorkspaceRoot);
        var reverse = history.PrepareReverse(change.OperationId);
        Assert.Equal(FileApplyState.Applied, (await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators()).ApplyAsync(reverse)).State);
        Assert.DoesNotContain(new FileHistory(f.Profile, f.WorkspaceRoot).List(), x => x.Pending);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(SourcePaths.Resolve(f.Profile, key with { ProfileId = f.Profile.Id }, false)));
    }

    [Fact]
    public async Task ReviewedCompletionResumesOnlyTheUnappliedTargets()
    {
        using var f = SourceFixture.Create();
        f.Write(SourceRoot.ServerData, "a.json", [1]);
        f.Write(SourceRoot.ServerData, "b.json", [2]);
        var store = new SourceStore(f.Profile, f.WorkspaceRoot);
        var change = new FileChange(Guid.NewGuid(), [store.Stage(f.Key(SourceRoot.ServerData, "a.json"), [3]), store.Stage(f.Key(SourceRoot.ServerData, "b.json"), [4])]);
        await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators(), (step, _) => { if (step == "after-replace") throw new IOException("Interrupted"); }).ApplyAsync(change);
        var history = new FileHistory(f.Profile, f.WorkspaceRoot);
        var completion = history.PrepareCompletion(change.OperationId);
        Assert.Equal("b.json", Assert.Single(completion.Files).Before.Key.RelativePath);
        var tampered = completion with { Files = [store.Stage(f.Key(SourceRoot.ServerData, "a.json"), [99])] };
        Assert.Equal(FileApplyState.Invalid, (await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators()).ApplyAsync(tampered)).State);
        Assert.Equal(FileApplyState.Applied, (await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators()).ApplyAsync(completion)).State);
        Assert.DoesNotContain(history.List(), x => x.Pending);
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(SourcePaths.Resolve(f.Profile, f.Key(SourceRoot.ServerData, "a.json"), false)));
        Assert.Equal(new byte[] { 4 }, File.ReadAllBytes(SourcePaths.Resolve(f.Profile, f.Key(SourceRoot.ServerData, "b.json"), false)));
    }

    [Fact]
    public async Task CancellationAfterReplaceKeepsDurablePartialEvidence()
    {
        using var f = SourceFixture.Create();
        f.Write(SourceRoot.ServerData, "a.json", [1]);
        f.Write(SourceRoot.ServerData, "b.json", [2]);
        var store = new SourceStore(f.Profile, f.WorkspaceRoot);
        var change = new FileChange(Guid.NewGuid(), [store.Stage(f.Key(SourceRoot.ServerData, "a.json"), [3]), store.Stage(f.Key(SourceRoot.ServerData, "b.json"), [4])]);
        using var cancel = new CancellationTokenSource();
        var result = await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators(), (step, _) => { if (step == "after-replace") cancel.Cancel(); }).ApplyAsync(change, cancel.Token);
        Assert.Equal(FileApplyState.Partial, result.State);
        Assert.Equal(FileApplyState.Partial, new FileHistory(f.Profile, f.WorkspaceRoot).Reconcile(change.OperationId).State);
    }

    [Fact]
    public async Task ReplacedRootIsRecheckedImmediatelyBeforeMutation()
    {
        using var f = SourceFixture.Create();
        f.Write(SourceRoot.ServerData, "a.json", [1]);
        var outside = Path.Combine(f.Root, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "a.json"), [7]);
        var store = new SourceStore(f.Profile, f.WorkspaceRoot);
        var change = new FileChange(Guid.NewGuid(), [store.Stage(f.Key(SourceRoot.ServerData, "a.json"), [2])]);
        var result = await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators(), (step, _) =>
        {
            if (step != "before-replace") return;
            var root = f.Profile.Roots[SourceRoot.ServerData];
            Directory.Move(root, root + "-original");
            if (!OperatingSystem.IsWindows()) Directory.CreateSymbolicLink(root, outside);
            else
            {
                var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                foreach (var arg in new[] { "/c", "mklink", "/J", root, outside }) start.ArgumentList.Add(arg);
                using var p = Process.Start(start)!; p.WaitForExit(); Assert.Equal(0, p.ExitCode);
            }
        }).ApplyAsync(change);
        Assert.Equal(FileApplyState.Conflict, result.State);
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(Path.Combine(outside, "a.json")));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(f.Profile.Roots[SourceRoot.ServerData] + "-original", "a.json")));
    }

    [Fact]
    public async Task MissingFinalReceiptStaysPendingUntilExplicitReconciliation()
    {
        using var f = SourceFixture.Create();
        var key = f.Key(SourceRoot.ServerData, "a.json");
        var change = new FileChange(Guid.NewGuid(), [new SourceStore(f.Profile, f.WorkspaceRoot).Stage(key, [2])]);
        var result = await new FileApply(f.Profile, f.WorkspaceRoot, f.Validators(), (step, _) => { if (step == "after-replace") throw new IOException("Lost receipt"); }).ApplyAsync(change);
        Assert.Equal(FileApplyState.Pending, result.State);
        var history = new FileHistory(f.Profile, f.WorkspaceRoot);
        Assert.True(Assert.Single(history.List()).Pending);
        Assert.Equal(FileApplyState.Applied, history.Reconcile(change.OperationId).State);
        Assert.False(Assert.Single(history.List()).Pending);
    }
}
