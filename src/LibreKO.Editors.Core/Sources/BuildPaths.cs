using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Sources;
public static class BuildPaths
{
    public static IReadOnlyList<ValidationIssue> Validate(SourceProfile profile,string snapshotRoot,string outputRoot,string? workspaceRoot=null)
    {
        var issues=new List<ValidationIssue>();try
        {
            string snapshot=SourcePaths.Canonical(snapshotRoot),output=SourcePaths.Canonical(outputRoot);SourcePaths.AssertNoLinks(snapshot);SourcePaths.AssertNoLinks(output);
            if(SourcePaths.Overlaps(snapshot,output))throw new InvalidOperationException("Snapshot and output directories must not overlap.");
            foreach(var root in profile.Roots.Where(x=>x.Key!=SourceRoot.BuildOutput)){SourcePaths.AssertNoLinks(root.Value);if(SourcePaths.Overlaps(root.Value,snapshot)||SourcePaths.Overlaps(root.Value,output))throw new InvalidOperationException("Build paths must not overlap selected source/pipeline/retail inputs.");}
            if(workspaceRoot!=null&&(SourcePaths.Overlaps(workspaceRoot,snapshot)||SourcePaths.Overlaps(workspaceRoot,output)))throw new InvalidOperationException("Build paths must not overlap editor history/workspace.");
            if(!profile.Roots.TryGetValue(SourceRoot.BuildOutput,out var selected)||!Directory.Exists(selected)||!SourcePaths.Contains(selected,snapshot)||!SourcePaths.Contains(selected,output))throw new InvalidOperationException("Snapshot and output must be separate children of an existing selected Build output root.");
            SourcePaths.AssertNoLinks(selected);
        }catch(Exception e)when(AssetError(e)){issues.Add(new("Build paths",e.Message));}return issues;
    }
    internal static bool AssetError(Exception e)=>e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or ArgumentException;
    public static void Guard(ClientBuildPlan plan)
    {
        var profile=plan.Profile??throw new InvalidDataException("Build plan lacks captured profile evidence.");var issues=Validate(profile,plan.SnapshotRoot,plan.OutputRoot,plan.WorkspaceRoot);if(issues.Count>0)throw new InvalidDataException(string.Join("; ",issues.Select(x=>x.Message)));
        foreach(var pair in plan.RootIdentities)if(!profile.Roots.TryGetValue(pair.Key,out var root)||SourceIdentity.Root(root)!=pair.Value)throw new InvalidDataException("A selected build/source root changed identity after capture.");
        if(SourceIdentity.Root(plan.SnapshotRoot)!=plan.SnapshotIdentity)throw new InvalidDataException("The captured snapshot directory was replaced.");
        if(plan.OutputIdentity!=null&&SourceIdentity.Root(plan.OutputRoot)!=plan.OutputIdentity)throw new InvalidDataException("The owned output directory was replaced.");
    }
}
