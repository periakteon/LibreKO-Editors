using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Sources;

public sealed class FileApply(SourceProfile profile, string workspaceRoot, SourceValidationRegistry validators,
    Action<string, SourceFileKey>? checkpoint = null)
{
    public async Task<FileApplyResult> ApplyAsync(FileChange change, CancellationToken ct = default)
    {
        var recovery = new FileRecovery(profile, workspaceRoot);
        FileOperation? prepared = null;
        FileStream? fileLock = null;
        try
        {
            // Cross-process exclusion covers overlap review, prepare and replacement.
            for (var attempt = 0; fileLock == null; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try { fileLock = recovery.Lock(); }
                catch (IOException) when (attempt < 100) { await Task.Delay(50, ct); }
            }
            if (change.OperationId == Guid.Empty || change.Files.Count is 0 or > 4096 || recovery.Exists(change.OperationId))
                return Failure(change, FileApplyState.Invalid, "Operation IDs must be new and file batches contain 1–4096 targets.");
            new FileHistory(profile, workspaceRoot).ValidateRecovery(change);
            var evidence = new List<FileEvidence>();
            using var reads=SourceReadScope.Begin();
            var issues = new List<ValidationIssue>();
            foreach (var mutation in change.Files)
            {
                ct.ThrowIfCancellationRequested();
                var before = mutation.Before;
                if (before.Exists != (before.Content != null) || before.Sha256 != (before.Content?.Sha256 ?? ""))
                    return Failure(change, FileApplyState.Invalid, "Invalid original file snapshot.");
                if (before.Content != null) recovery.Blobs.Read(before.Content);
                var codec = validators.Codec(before.Key);
                if (mutation.After != null) issues.AddRange(validators.Validate(new(before.Key, mutation.After)));
                evidence.Add(recovery.Evidence(mutation, codec));
                var current = recovery.Store.Read(before.Key);
                if (!FileRecovery.Matches(current, before.Content))
                    throw new SourceConflictException("Original file changed or expected absence no longer holds: " + before.Key.RelativePath);
            }
            if (issues.Count == 0) issues.AddRange(await validators.ValidateBatchAsync(change,ct));
            if (issues.Count > 0) return new(change.OperationId, FileApplyState.Invalid, Unapplied(change), issues);
            var targets=change.Files.Select(f=>f.Before.Key).ToHashSet();var dependencies=reads.Seal().Where(s=>!targets.Contains(s.Key)).ToArray();
            void GuardDependencies()=>SourceReadScope.Verify(recovery.Store,dependencies);
            if (evidence.Where((a, i) => evidence.Skip(i + 1).Any(b => FileRecovery.Overlaps(a, b))).Any())
                return Failure(change, FileApplyState.Invalid, "A file batch cannot target the same physical file twice.");
            foreach (var operation in recovery.All(allProfiles: true).Where(x => !x.Finalized && x.ResolvedBy == null))
            {
                if (operation.Change.OperationId == change.RecoversOperationId) continue;
                if (operation.Evidence.Any(a => evidence.Any(b => FileRecovery.Overlaps(a, b))))
                    return Failure(change, FileApplyState.Pending, "An unresolved operation overlaps this physical target. Reconcile it first.");
            }
            prepared = new(change, DateTime.UtcNow, evidence);
            recovery.Prepare(prepared);
            foreach (var mutation in change.Files) checkpoint?.Invoke("prepared", mutation.Before.Key);
            foreach (var mutation in change.Files)
            {
                ct.ThrowIfCancellationRequested();
                checkpoint?.Invoke("before-replace", mutation.Before.Key);
                GuardDependencies();Replace(recovery, mutation, ct,GuardDependencies);
                checkpoint?.Invoke("after-replace", mutation.Before.Key);
                prepared = prepared with { Outcomes = recovery.Inspect(prepared).Targets };
                recovery.Save(prepared);
            }
            ct.ThrowIfCancellationRequested();
            prepared = prepared with { Finalized = true, Outcomes = recovery.Inspect(prepared).Targets };
            recovery.Save(prepared);
            recovery.ResolveParent(prepared);
            foreach (var mutation in change.Files) checkpoint?.Invoke("finalized", mutation.Before.Key);
            return recovery.Inspect(prepared);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or OperationCanceledException or System.Text.Json.JsonException)
        {
            if (prepared == null) return Failure(change, e is SourceConflictException ? FileApplyState.Conflict :
                e is InvalidOperationException or InvalidDataException or System.Text.Json.JsonException ? FileApplyState.Invalid : FileApplyState.NotApplied, e.Message);
            var result = recovery.Inspect(prepared);
            try { recovery.Save(prepared with { Outcomes = result.Targets, Error = e.Message }); }
            catch (Exception journalError) when (journalError is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
            { return result with { State = FileApplyState.Pending, Issues = [new("history", "Receipt could not be persisted: " + journalError.Message)] }; }
            return result with { State = result.State == FileApplyState.Applied && !prepared.Finalized ? FileApplyState.Pending : result.State,
                Issues = [new("files", e.Message)] };
        }
        finally { fileLock?.Dispose(); }
    }

    void Replace(FileRecovery recovery, FileMutation mutation, CancellationToken ct,Action guardDependencies)
    {
        recovery.GuardWorkspace();
        SourcePaths.Validate(profile, workspaceRoot);
        var key = mutation.Before.Key;
        var path = SourcePaths.Resolve(profile, key, true);
        if (!FileRecovery.Matches(recovery.Store.Read(key), mutation.Before.Content))
            throw new SourceConflictException("The file changed immediately before replacement: " + key.RelativePath);
        if (mutation.Before.Content == mutation.After) return;
        if (File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0)
            throw new UnauthorizedAccessException("Source target is read-only: " + key.RelativePath);
        if (mutation.After == null) { guardDependencies();File.Delete(path); return; }
        var bytes = recovery.Blobs.Read(mutation.After);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SourcePaths.Resolve(profile, key, true);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".editor-tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { stream.Write(bytes); stream.Flush(true); }
            ct.ThrowIfCancellationRequested();
            SourcePaths.Resolve(profile, key, true);
            if (!FileRecovery.Matches(recovery.Store.Read(key), mutation.Before.Content))
                throw new SourceConflictException("The file changed during staging: " + key.RelativePath);
            guardDependencies();
            if (mutation.Before.Exists) File.Replace(temp, path, null);
            else File.Move(temp, path, false);
        }
        finally
        {
            // Remove only our own staging file, and only after containment is rechecked.
            try { SourcePaths.Resolve(profile, key, true); if (File.Exists(temp)) File.Delete(temp); }
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException) { }
        }
    }

    static FileTargetOutcome[] Unapplied(FileChange change) => change.Files.Select(x => new FileTargetOutcome(x.Before.Key, FileApplyState.NotApplied)).ToArray();
    static FileApplyResult Failure(FileChange change, FileApplyState state, string message)
        => new(change.OperationId, state, Unapplied(change), [new("files", message)]);
}
