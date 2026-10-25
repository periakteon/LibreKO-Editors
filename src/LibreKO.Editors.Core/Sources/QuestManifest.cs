using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Sources;
public static class QuestManifest
{
    public static string Path(string relative)
    {
        var value=relative.Replace('\\','/');try{SourcePaths.ValidateRelative(value);}catch(InvalidOperationException e){throw new InvalidDataException(e.Message,e);}
        if(!value.EndsWith(".quest",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Quest membership requires a relative .quest source path.");return value;
    }
    public static IReadOnlyList<string> Entries(JsonSourceDocument manifest)
    {
        if(manifest.Snapshot.Key.Root!=SourceRoot.ServerQuests||!manifest.Snapshot.Key.RelativePath.Equals("quest-manifest.json",StringComparison.OrdinalIgnoreCase)||manifest.Root is not JsonArray array)throw new InvalidDataException("Expected the selected quest root's quest-manifest.json array.");
        var entries=array.Select(x=>x is JsonValue value&&value.TryGetValue<string>(out var s)?Path(s):throw new InvalidDataException("Manifest entries must be quest source paths.")).ToArray();
        if(entries.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=entries.Length)throw new InvalidDataException("Duplicate manifest paths are ambiguous.");
        if(entries.Length>4096)throw new InvalidDataException("Quest manifests are limited to 4096 entry points.");return entries;
    }
    public static void Prepare(JsonSourceDocument manifest,string relativeQuest,bool include)
    {
        var relative=Path(relativeQuest);var entries=Entries(manifest);int at=entries.ToList().FindIndex(x=>x.Equals(relative,StringComparison.OrdinalIgnoreCase));
        if(include&&at<0)manifest.InsertValue(JsonValue.Create(relative)!,entries.Count);else if(!include&&at>=0)manifest.RemoveAt(at);
    }
    public static bool Accepts(SourceFileKey key)=>key.Root==SourceRoot.ServerQuests&&(key.RelativePath.Equals("quest-manifest.json",StringComparison.OrdinalIgnoreCase)||key.RelativePath.EndsWith(".quest",StringComparison.OrdinalIgnoreCase));
    public static IReadOnlyList<ValidationIssue> ValidateText(SourceValidationTicket ticket,BlobStore blobs)
    {
        var bytes=blobs.Read(ticket.Candidate);if(ticket.Key.RelativePath.EndsWith(".quest",StringComparison.OrdinalIgnoreCase)){var text=TextDocument.Decode(bytes).Text;if(text.Length>2*1024*1024)throw new InvalidDataException("Quest scripts are limited to 2 MiB of text.");return [];}
        _=Entries(JsonSourceDocument.Open(new(ticket.Key,true,ticket.Candidate.Sha256,ticket.Candidate),bytes));return [];
    }
}
