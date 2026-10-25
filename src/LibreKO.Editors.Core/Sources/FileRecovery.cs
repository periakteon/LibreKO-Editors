using System.Text.Json;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Sources;

internal sealed record FileEvidence(SourceFileKey Key, string Codec, string PathIdentity, string? PhysicalIdentity);
internal sealed record FileOperation(FileChange Change, DateTime CreatedUtc, IReadOnlyList<FileEvidence> Evidence,
    bool Finalized = false, Guid? ResolvedBy = null, IReadOnlyList<FileTargetOutcome>? Outcomes = null, string? Error = null);
internal sealed record FileReceipt(bool Finalized, Guid? ResolvedBy, IReadOnlyList<FileTargetOutcome>? Outcomes, string? Error);
internal sealed class SourceConflictException(string message) : InvalidOperationException(message);

// The prepared record and blobs are immutable. Only a separate receipt is replaced.
internal sealed class FileRecovery
{
    readonly SourceProfile profile;
    readonly string workspace, workspaceIdentity, directory;
    internal BlobStore Blobs { get; }
    internal SourceStore Store { get; }

    internal FileRecovery(SourceProfile profile, string workspaceRoot)
    {
        this.profile = profile;
        workspace = Path.GetFullPath(workspaceRoot);
        SourcePaths.Validate(profile, workspace);
        Directory.CreateDirectory(workspace);
        workspaceIdentity = SourceIdentity.Root(workspace);
        directory = WorkspacePaths.ResolveOwnedPath(workspace, "source-history/" + SourceIdentity.Hash(profile.Id));
        Store = new(profile, workspace);
        Blobs = Store.Blobs;
    }

    internal void GuardWorkspace()
    {
        if (SourceIdentity.Root(workspace) != workspaceIdentity)
            throw new InvalidOperationException("The editor history root was replaced.");
        SourcePaths.AssertNoLinks(directory);
    }

    internal FileStream Lock()
    {
        GuardWorkspace();
        var path = WorkspacePaths.ResolveOwnedPath(workspace, "source-history/apply.lock");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SourcePaths.AssertNoLinks(path);
        return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    internal bool Exists(Guid id) => File.Exists(PreparedPath(id));
    string PreparedPath(Guid id) { GuardWorkspace(); return WorkspacePaths.ResolveOwnedPath(directory, id.ToString("N") + ".prepared.json"); }
    string ReceiptPath(Guid id) { GuardWorkspace(); return WorkspacePaths.ResolveOwnedPath(directory, id.ToString("N") + ".receipt.json"); }

    internal void Prepare(FileOperation operation)
        => WriteJson(PreparedPath(operation.Change.OperationId), operation, false);
    internal void Save(FileOperation operation)
        => WriteJson(ReceiptPath(operation.Change.OperationId), new FileReceipt(operation.Finalized, operation.ResolvedBy, operation.Outcomes, operation.Error), true);

    internal FileOperation Load(Guid id) => LoadPath(PreparedPath(id));

    FileOperation LoadPath(string preparedPath)
    {
        var operation = ReadJson<FileOperation>(preparedPath);
        if (operation.Change.OperationId == Guid.Empty || operation.Change.Files.Count == 0 || operation.Evidence.Count != operation.Change.Files.Count)
            throw new InvalidDataException("Invalid prepared file-operation record.");
        var receiptPath = preparedPath.Replace(".prepared.json", ".receipt.json", StringComparison.Ordinal);
        if (!File.Exists(receiptPath)) return operation;
        var receipt = ReadJson<FileReceipt>(receiptPath);
        return operation with { Finalized = receipt.Finalized, ResolvedBy = receipt.ResolvedBy, Outcomes = receipt.Outcomes, Error = receipt.Error };
    }

    internal IReadOnlyList<FileOperation> All(bool allProfiles = false)
    {
        GuardWorkspace();
        var root = allProfiles ? Path.GetDirectoryName(directory)! : directory;
        if (!Directory.Exists(root)) return [];
        // Never follow a reparse directory while enumerating journals.
        var result = new List<FileOperation>();
        var directories = allProfiles ? Directory.GetDirectories(root) : [root];
        foreach (var folder in directories)
        {
            SourcePaths.AssertNoLinks(folder);
            foreach (var file in Directory.GetFiles(folder, "*.prepared.json")) result.Add(LoadPath(file));
        }
        return result.OrderByDescending(x => x.CreatedUtc).ToArray();
    }

    internal FileTargetOutcome Inspect(FileMutation mutation)
    {
        try
        {
            var current = Store.Read(mutation.Before.Key);
            if (Matches(current, mutation.After)) return new(current.Key, FileApplyState.Applied);
            if (Matches(current, mutation.Before.Content)) return new(current.Key, FileApplyState.NotApplied);
            return new(current.Key, FileApplyState.Conflict, "Current file differs from both reviewed outcomes.");
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        { return new(mutation.Before.Key, FileApplyState.Conflict, e.Message); }
    }

    internal FileApplyResult Inspect(FileOperation operation)
    {
        var outcomes = operation.Change.Files.Select(Inspect).ToArray();
        return new(operation.Change.OperationId, Aggregate(outcomes), outcomes, []);
    }

    internal static FileApplyState Aggregate(IReadOnlyList<FileTargetOutcome> outcomes)
    {
        if (outcomes.Any(x => x.State == FileApplyState.Conflict)) return FileApplyState.Conflict;
        if (outcomes.All(x => x.State == FileApplyState.Applied)) return FileApplyState.Applied;
        if (outcomes.All(x => x.State == FileApplyState.NotApplied)) return FileApplyState.NotApplied;
        return FileApplyState.Partial;
    }

    internal static bool Matches(SourceFileSnapshot current, BlobRef? expected)
        => expected == null ? !current.Exists : current.Exists && current.Sha256 == expected.Sha256 && current.Content?.Length == expected.Length;

    internal FileEvidence Evidence(FileMutation mutation, string codec)
    {
        var path = SourcePaths.Resolve(profile, mutation.Before.Key, true);
        return new(mutation.Before.Key, codec, SourceIdentity.Hash(SourcePaths.Canonical(path)),
            File.Exists(path) ? SourceIdentity.Physical(path) : null);
    }

    internal static bool Overlaps(FileEvidence a, FileEvidence b)
        => a.PathIdentity == b.PathIdentity || a.PhysicalIdentity != null && a.PhysicalIdentity == b.PhysicalIdentity;

    internal void ResolveParent(FileOperation completed)
    {
        if (completed.Change.RecoversOperationId is not Guid id) return;
        var parent = Load(id);
        var state = Inspect(parent).State;
        if (state is FileApplyState.Applied or FileApplyState.NotApplied)
            Save(parent with { ResolvedBy = completed.Change.OperationId });
    }

    T ReadJson<T>(string path)
    {
        GuardWorkspace();
        SourcePaths.AssertNoLinks(path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 16 * 1024 * 1024) throw new InvalidDataException("File history record exceeds its size limit.");
        return JsonSerializer.Deserialize<T>(stream, JsonConfig.Options) ?? throw new InvalidDataException("Invalid file history record.");
    }

    void WriteJson<T>(string path, T value, bool replace)
    {
        GuardWorkspace();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonConfig.Options);
        if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("File history record exceeds its size limit.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SourcePaths.AssertNoLinks(path);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(bytes); stream.Flush(true); }
        GuardWorkspace();
        SourcePaths.AssertNoLinks(path);
        if (replace && File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path, false);
    }
}
