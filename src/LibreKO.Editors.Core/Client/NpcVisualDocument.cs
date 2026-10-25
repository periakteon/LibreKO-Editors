using System.Globalization;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Client;
public sealed class NpcVisualDocument
{
    public JsonSourceDocument Json{get;}
    NpcVisualDocument(JsonSourceDocument json){Json=json;}
    public static NpcVisualDocument Open(SourceFileSnapshot snapshot,byte[] bytes)=>new(JsonSourceDocument.Open(snapshot,bytes));
    public static NpcVisualDocument OpenDraft(SourceFileSnapshot snapshot,byte[] bytes)=>new(JsonSourceDocument.OpenDraft(snapshot,bytes));
    public byte[] Render()=>Json.Render();public FileMutation Build(SourceStore store)=>new(Json.Snapshot,store.Blobs.Put(Render()));
    public void SetMapping(int model,string stem){NpcVisualCatalog.Stem(JsonValue.Create(stem),true);Json.Set(Json.RowAt(0).Token,model.ToString(CultureInfo.InvariantCulture),JsonValue.Create(stem));}
    public void SetNested(string group,int ordinal,string field,JsonNode? value)
    {var root=Json.RowAt(0);var array=root.Values[group]?.AsArray()??throw new InvalidDataException("Missing ordered visual records.");array[ordinal]!.AsObject()[field]=value?.DeepClone();Json.Set(root.Token,group,array);}
    public IReadOnlyList<WorldRowSnapshot> SharedUsers(string stem,IEnumerable<WorldRowSnapshot> prototypes)
    {var index=Json.Root.AsObject();return prototypes.Where(p=>p.Key.Table==WorldTable.Npcs&&int.TryParse(p.Values["ModelId"]?.ToString(),out var id)&&index[id.ToString(CultureInfo.InvariantCulture)]?.ToString()==stem).ToArray();}
    public FileChange Clone(string stem,string newStem,SourceStore store,IReadOnlyDictionary<SourceFileKey,BlobRef?>? overlay=null)
    {
        NpcVisualCatalog.Stem(JsonValue.Create(stem));NpcVisualCatalog.Stem(JsonValue.Create(newStem));if(stem.Equals(newStem,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("A clone needs a new resource stem.");var files=new List<FileMutation>();
        foreach(var extension in new[]{".glb",".anim.json",".plugs.json",".fxplug.json"}){var original=store.Read(store.Key(SourceRoot.ClientProject,"assets/npcs/"+stem+extension));var candidate=overlay!=null&&overlay.TryGetValue(original.Key,out var value)?value:original.Content;if(candidate==null){if(extension==".glb")throw new FileNotFoundException("Selected loose model is missing or deleted.");continue;}store.Blobs.Read(candidate);var target=store.Read(store.Key(SourceRoot.ClientProject,"assets/npcs/"+newStem+extension));if(target.Exists||overlay?.ContainsKey(target.Key)==true)throw new InvalidDataException("Clone destination exists or has a draft: "+target.Key.RelativePath);files.Add(new(target,candidate));}return new(Guid.NewGuid(),files);
    }
}
