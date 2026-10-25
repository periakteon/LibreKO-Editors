using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Sources;
public sealed record SourcePreviewManifest(Guid Id,string SourceProfileId,int SourceGeneration,IReadOnlyList<SourceFileSnapshot> Inputs,string ProjectRoot)
{
    public ClientBuildPlan? Build{get;init;}public SourceFileKey? Target{get;init;}public int SourceRevision{get;init;}public long DraftVersion{get;init;}
    public string Kind{get;init;}="model";public string? Stem{get;init;}public JsonObject? Prototype{get;init;}
    public string ExpectedSourceHash{get;init;}="";public int AutoCloseFrames{get;init;}
    [System.Text.Json.Serialization.JsonIgnore]
    public string ModelResourcePath
    {
        get{if(Target==null||Target.Root!=SourceRoot.ClientProject)throw new InvalidDataException("A client source target is required.");SourcePaths.ValidateRelative(Target.RelativePath);if(Client.AssetDependencyAudit.IsModel(Target))return Target.RelativePath;return "assets/npcs/"+Client.NpcVisualCatalog.Stem(JsonValue.Create(Stem))+".glb";}
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool AssembleNpc=>ModelResourcePath.StartsWith("assets/npcs/",StringComparison.Ordinal)&&ModelResourcePath.Split('/').Length==3;
    public void Guard(string profile,int generation,int revision,long draftVersion)
    {if(profile!=SourceProfileId||generation!=SourceGeneration||revision!=SourceRevision||draftVersion!=DraftVersion)throw new OperationCanceledException("Source preview belongs to a changed selection/profile/draft.");if(Build==null||Build.Id!=Id||Build.SourceProfileId!=profile||Path.GetFullPath(Build.SnapshotRoot)!=Path.GetFullPath(ProjectRoot)||Target?.ProfileId!=profile||Target.Root!=SourceRoot.ClientProject)throw new InvalidDataException("Source preview manifest lacks matching captured build/target evidence.");BuildPaths.Guard(Build);var file=Build.SnapshotFiles.SingleOrDefault(f=>f.RelativePath==Target.RelativePath);if(file?.Sha256!=ExpectedSourceHash)throw new InvalidDataException("Source preview target hash differs from its captured bytes.");}
}
