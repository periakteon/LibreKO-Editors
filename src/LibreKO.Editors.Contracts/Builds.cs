namespace LibreKO.Editors.Contracts;
public record AuthoringTools(string? GodotEditor,string? DotnetSdk,string? Python,string? ExportTemplate=null);
public record BuildSnapshotFile(string RelativePath,string Sha256,long Length,bool Generated=false);
public record ClientBuildPlan(Guid Id,string SourceProfileId,string SnapshotRoot,string OutputRoot,IReadOnlyList<string> Stages,IReadOnlyList<SourceFileSnapshot> Inputs,FileChange? PreviewDraft=null)
{
    public SourceProfile? Profile{get;init;}public string WorkspaceRoot{get;init;}="";
    public string SnapshotIdentity{get;init;}="";public string? OutputIdentity{get;init;}
    public IReadOnlyDictionary<SourceRoot,string> RootIdentities{get;init;}=new Dictionary<SourceRoot,string>();
    public IReadOnlyList<BuildSnapshotFile> SnapshotFiles{get;init;}=[];
}
public record ClientBuildResult(Guid Id,bool Succeeded,string ManifestPath,IReadOnlyList<ValidationIssue> Issues);
