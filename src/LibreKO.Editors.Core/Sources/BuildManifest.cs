using System.Text.Json;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Sources;
public sealed record BuildManifest(ClientBuildPlan Plan,string Status,bool Verified,IReadOnlyDictionary<string,string> ToolVersions,IReadOnlyList<BuildSnapshotFile> Outputs,IReadOnlyList<ValidationIssue> Issues,DateTimeOffset UpdatedAt)
{
    public static BuildManifest Read(string path){SourcePaths.AssertNoLinks(path);using var file=File.OpenRead(path);SourceLimits.CheckedLength(file.Length);return JsonSerializer.Deserialize<BuildManifest>(file,JsonConfig.Options)??throw new InvalidDataException("Build manifest is empty.");}
    public void Save(string path)
    {BuildPaths.Guard(Plan);SourcePaths.AssertNoLinks(path);var bytes=JsonSerializer.SerializeToUtf8Bytes(this,JsonConfig.Options);SourceLimits.CheckedLength(bytes.Length);var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}File.Move(temp,path,true);}
}
