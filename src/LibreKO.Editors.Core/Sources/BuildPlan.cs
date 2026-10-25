using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Sources;
public static class BuildPlan
{
    static readonly HashSet<string> excluded=new(StringComparer.OrdinalIgnoreCase){".git",".godot",".mono",".vscode",".vs",".idea",".github","bin","obj","history",".superpowers","export","exports","build","builds","artifacts","tests","_capture","plugins"};
    static readonly HashSet<string> privateFiles=new(StringComparer.OrdinalIgnoreCase){"export_presets.cfg","export_credentials.cfg","settings.cfg","windows.cfg","keys.cfg","presets.cfg","override.cfg","lootfilter.cfg"};
    public static Task<ClientBuildPlan> CaptureAsync(SourceStore store,string workspaceRoot,string snapshotRoot,string outputRoot,FileChange? preview=null,CancellationToken ct=default)=>Task.Run(()=>
    {
        var profile=store.Profile;var issues=BuildPaths.Validate(profile,snapshotRoot,outputRoot,workspaceRoot);if(issues.Count>0)throw new InvalidDataException(string.Join("; ",issues.Select(x=>x.Message)));
        if(Directory.Exists(snapshotRoot)||Directory.Exists(outputRoot)||File.Exists(snapshotRoot)||File.Exists(outputRoot))throw new InvalidDataException("A new build needs unused snapshot and output directories.");
        var identities=profile.Roots.Where(x=>Directory.Exists(x.Value)).ToDictionary(x=>x.Key,x=>SourceIdentity.Root(x.Value));
        var inputs=new Dictionary<SourceFileKey,SourceFileSnapshot>();var snapshots=new Dictionary<string,BuildSnapshotFile>(StringComparer.OrdinalIgnoreCase);var sourceRoot=profile.Roots[SourceRoot.ClientProject];store.GuardKey(store.Key(SourceRoot.ClientProject,"project.godot"));
        var files=new List<string>();Walk(sourceRoot,0);files.Sort(StringComparer.Ordinal);var overlay=preview?.Files.Where(f=>f.Before.Key.Root==SourceRoot.ClientProject).ToDictionary(f=>f.Before.Key)??[];
        Directory.CreateDirectory(snapshotRoot);SourcePaths.AssertNoLinks(snapshotRoot);
        foreach(var relative in files)
        {
            ct.ThrowIfCancellationRequested();var key=store.Key(SourceRoot.ClientProject,relative);var before=store.Read(key);inputs.Add(key,before);if(before.Content==null)throw new InvalidDataException("Source disappeared during build capture: "+relative);
            var candidate=before.Content;if(overlay.Remove(key,out var mutation)){if(mutation.Before!=before)throw new InvalidDataException("Preview draft has an externally changed baseline: "+relative);candidate=mutation.After;}if(candidate!=null)Copy(relative,store.Blobs.Read(candidate));
        }
        foreach(var mutation in overlay.Values)
        {
            ct.ThrowIfCancellationRequested();store.GuardKey(mutation.Before.Key);var before=store.Read(mutation.Before.Key);if(before!=mutation.Before)throw new InvalidDataException("Preview draft has an externally changed baseline: "+before.Key.RelativePath);inputs[before.Key]=before;if(mutation.After!=null)Copy(before.Key.RelativePath,store.Blobs.Read(mutation.After));
        }
        if(!snapshots.ContainsKey("project.godot"))throw new InvalidDataException("Select a complete Godot client project with project.godot.");
        var plan=new ClientBuildPlan(Guid.NewGuid(),profile.Id,Path.GetFullPath(snapshotRoot),Path.GetFullPath(outputRoot),["tools","compile","import","export","probe"],inputs.Values.ToArray(),preview)
        {Profile=profile,WorkspaceRoot=Path.GetFullPath(workspaceRoot),RootIdentities=identities,SnapshotIdentity=SourceIdentity.Root(snapshotRoot),SnapshotFiles=snapshots.Values.OrderBy(x=>x.RelativePath,StringComparer.Ordinal).ToArray()};BuildPaths.Guard(plan);return plan;
        void Walk(string directory,int depth)
        {
            ct.ThrowIfCancellationRequested();if(depth>128)throw new InvalidDataException("Client project directory nesting exceeds128.");SourcePaths.AssertNoLinks(directory);
            foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                string name=Path.GetFileName(entry);if(Directory.Exists(entry)){if(!excluded.Contains(name))Walk(entry,depth+1);continue;}if(privateFiles.Contains(name)||name.EndsWith(".old",StringComparison.OrdinalIgnoreCase)||name.EndsWith(".tmp",StringComparison.OrdinalIgnoreCase)||Path.GetExtension(name) is ".pck" or ".exe" or ".zip")continue;
                SourcePaths.AssertNoLinks(entry);files.Add(Path.GetRelativePath(sourceRoot,entry).Replace('\\','/'));if(files.Count>100000)throw new InvalidDataException("A client build is limited to100,000 source files.");
            }
        }
        void Copy(string relative,byte[] data)
        {
            SourcePaths.ValidateRelative(relative);if(relative.Split('/').Any(excluded.Contains)||privateFiles.Contains(Path.GetFileName(relative)))throw new InvalidDataException("Preview candidate targets an excluded build path: "+relative);
            var path=WorkspacePaths.ResolveOwnedPath(snapshotRoot,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);SourcePaths.AssertNoLinks(path);using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))output.Write(data);
            if(!snapshots.TryAdd(relative,new(relative,Convert.ToHexString(SHA256.HashData(data)),data.Length)))throw new InvalidDataException("Duplicate case-insensitive snapshot identity: "+relative);
        }
    },ct);
    public static ClientBuildPlan Generate(ClientBuildPlan plan,string relative,byte[] data)
    {
        BuildPaths.Guard(plan);var path=WorkspacePaths.ResolveOwnedPath(plan.SnapshotRoot,relative);if(File.Exists(path))throw new InvalidDataException("Generated build input would replace a captured source: "+relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);SourcePaths.AssertNoLinks(path);File.WriteAllBytes(path,data);return plan with{SnapshotFiles=[..plan.SnapshotFiles,new(relative,Convert.ToHexString(SHA256.HashData(data)),data.Length,true)]};
    }
    public static IReadOnlyList<ValidationIssue> VerifySnapshot(ClientBuildPlan plan,bool derivedImports=false)
    {
        var issues=new List<ValidationIssue>();try
        {
            BuildPaths.Guard(plan);var expected=plan.SnapshotFiles.Select(f=>f.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach(var input in plan.SnapshotFiles){if(derivedImports&&input.RelativePath.EndsWith(".import",StringComparison.Ordinal))continue;var path=WorkspacePaths.ResolveOwnedPath(plan.SnapshotRoot,input.RelativePath);SourcePaths.AssertNoLinks(path);using var file=File.OpenRead(path);if(file.Length!=input.Length||Convert.ToHexString(SHA256.HashData(file))!=input.Sha256)issues.Add(new(input.RelativePath,"Captured snapshot bytes changed."));}
            Walk(plan.SnapshotRoot);
            void Walk(string directory){SourcePaths.AssertNoLinks(directory);foreach(var path in Directory.EnumerateFileSystemEntries(directory)){SourcePaths.AssertNoLinks(path);if(Directory.Exists(path)){if(derivedImports&&Path.GetFileName(path) is ".godot" or "bin" or "obj")continue;Walk(path);continue;}var relative=Path.GetRelativePath(plan.SnapshotRoot,path).Replace('\\','/');if(!expected.Contains(relative)&&!(derivedImports&&(relative.EndsWith(".import",StringComparison.Ordinal)||relative.EndsWith(".uid",StringComparison.Ordinal))))issues.Add(new(relative,"Uncaptured file was added to the snapshot."));}}
        }catch(Exception e)when(BuildPaths.AssetError(e)){issues.Add(new("Snapshot",e.Message));}return issues;
    }
}
