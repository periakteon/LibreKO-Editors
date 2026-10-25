using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Sources;

public sealed class FileHistory
{
    readonly SourceProfile profile;
    readonly FileRecovery recovery;
    public FileHistory(SourceProfile profile, string workspaceRoot)
    { this.profile = profile; recovery = new(profile, workspaceRoot); }

    public IReadOnlyList<OperationLink> List() => recovery.All().Select(x => new OperationLink(
        "files", x.Change.OperationId, profile.Id, $"{x.Change.Files.Count} file(s) · {x.CreatedUtc:u}",
        !x.Finalized && x.ResolvedBy == null, x.Change.Files.Select(f => new NavigationTarget("world", "source",
            JsonSerializer.SerializeToNode(f.Before.Key, JsonConfig.Options)!.AsObject(), profile.Id)).ToArray())).ToArray();

    public FileApplyResult Reconcile(Guid id)
    {
        using var fileLock = recovery.Lock();
        var operation = recovery.Load(id);
        var result = recovery.Inspect(operation);
        var terminal = result.State is FileApplyState.Applied or FileApplyState.NotApplied;
        var updated = operation with { Finalized = terminal, Outcomes = result.Targets };
        recovery.Save(updated);
        if (result.State == FileApplyState.Applied) recovery.ResolveParent(updated);
        return result;
    }

    public FileChange PrepareReverse(Guid id) => Prepare(id, reverse: true);
    public FileChange Read(Guid id) => recovery.Load(id).Change;
    public FileChange PrepareCompletion(Guid id) => Prepare(id, reverse: false);

    FileChange Prepare(Guid id, bool reverse)
    {
        var operation = recovery.Load(id);
        var current = recovery.Inspect(operation);
        if (current.State == FileApplyState.Conflict)
            throw new InvalidOperationException("External or root conflicts must be resolved before preparing recovery.");
        var files = new List<FileMutation>();
        foreach (var mutation in operation.Change.Files)
        {
            var snapshot = recovery.Store.Read(mutation.Before.Key);
            var desired = reverse ? mutation.Before.Content : mutation.After;
            if (!FileRecovery.Matches(snapshot, desired)) files.Add(new(snapshot, desired));
        }
        if (files.Count == 0) throw new InvalidOperationException("No file differences remain for this recovery action.");
        return new(Guid.NewGuid(), files, id);
    }

    internal void ValidateRecovery(FileChange candidate)
    {
        if (candidate.RecoversOperationId is not Guid id) return;
        foreach (var reverse in new[] { true, false })
        {
            FileChange expected;
            try { expected = Prepare(id, reverse); }
            catch (InvalidOperationException) { continue; }
            var actualFiles = JsonSerializer.Serialize(candidate.Files, JsonConfig.Options);
            if (actualFiles == JsonSerializer.Serialize(expected.Files, JsonConfig.Options)) return;
        }
        throw new InvalidOperationException("Recovery must exactly match a freshly reviewed completion or reversal.");
    }
}
