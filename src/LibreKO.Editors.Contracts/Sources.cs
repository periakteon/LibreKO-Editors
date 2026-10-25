namespace LibreKO.Editors.Contracts;

public enum SourceRoot { ServerData, ServerMaps, ServerQuests, ClientProject, Pipeline, KoInput, BuildOutput }
public record SourceProfile(string Id, string Label, IReadOnlyDictionary<SourceRoot, string> Roots,
    IReadOnlyDictionary<int, string>? TerrainAliases = null,AuthoringTools? Tools=null);
public record SourceFileKey(string ProfileId, SourceRoot Root, string RootIdentity, string RelativePath);
public record BlobRef(string Sha256, long Length);
public record SourceFileSnapshot(SourceFileKey Key, bool Exists, string Sha256, BlobRef? Content);
public record FileMutation(SourceFileSnapshot Before, BlobRef? After);
public record FileChange(Guid OperationId, IReadOnlyList<FileMutation> Files, Guid? RecoversOperationId = null);
public enum FileApplyState { Applied, NotApplied, Partial, Conflict, Pending, Invalid }
public record FileTargetOutcome(SourceFileKey Key, FileApplyState State, string? Message = null);
public record FileApplyResult(Guid OperationId, FileApplyState State,
    IReadOnlyList<FileTargetOutcome> Targets, IReadOnlyList<ValidationIssue> Issues);
public record SourceValidationTicket(SourceFileKey Key, BlobRef Candidate);
public record SourceValidationAudit(IReadOnlyList<ValidationIssue> Issues,IReadOnlyList<SourceFileSnapshot> Dependencies);
public enum OperationKind { Database, Source, Build }
