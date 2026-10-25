using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Client;
public static class ClientResourceGraph
{
    public static IEnumerable<string> Dependencies(string path,byte[] bytes)
    {
        if(path.EndsWith(".glb",StringComparison.Ordinal)||path.EndsWith(".gltf",StringComparison.Ordinal))
        {var model=GltfAssetDocument.Read(bytes,path.EndsWith(".glb",StringComparison.Ordinal));string dir=Path.GetDirectoryName(path)!.Replace('\\','/');foreach(var uri in model.ExternalUris)yield return dir+"/"+uri;yield break;}
        if(!path.EndsWith(".json",StringComparison.Ordinal))yield break;
        var root=JsonNode.Parse(TextDocument.Decode(bytes).Text);var name=Path.GetFileName(path);
        if(path.StartsWith("assets/terrain/",StringComparison.Ordinal)&&root is JsonArray rows)
        {
            foreach(var row in rows.OfType<JsonObject>())
            {
                string? stem=(name=="placements.json"?row["name"]:name=="fx_placements.json"?row["fx"]:name=="water.json"?row["tex"]:null)?.GetValue<string>();if(string.IsNullOrEmpty(stem))continue;NpcVisualCatalog.Stem(JsonValue.Create(stem));
                if(name=="placements.json")yield return "assets/objects/"+new string(stem.ToLowerInvariant().Select(c=>char.IsLetterOrDigit(c)||c is '_' or '-' or '.'?c:'_').ToArray())+".glb";
                else if(name=="fx_placements.json")yield return "assets/fx/"+stem.ToLowerInvariant()+".json";
                else yield return "assets/water/"+stem+".png";
            }
        }
        else if(path.StartsWith("assets/fx/",StringComparison.Ordinal)&&root is JsonObject obj)
            foreach(var reference in CompatibilityResources.References(path,obj)){if(!reference.Path.StartsWith("res://",StringComparison.Ordinal))throw new InvalidDataException("Unsupported effect dependency.");yield return reference.Path[6..];}
    }
    public static IReadOnlyList<ValidationIssue> Validate(string path,byte[] bytes,Func<string,byte[]> read)
    {
        try
        {
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);void Visit(string relative,byte[] data,int depth)
            {
                SourcePaths.ValidateRelative(relative);if(!seen.Add(relative))return;if(depth>64||seen.Count>10000)throw new InvalidDataException("Resource graph exceeds64 levels/10,000 files.");
                if(relative.EndsWith(".png",StringComparison.Ordinal))RgbaPngCodec.Decode(data);
                foreach(var dependency in Dependencies(relative,data)){SourcePaths.ValidateRelative(dependency);Visit(dependency,read(dependency),depth+1);}
            }
            Visit(path,bytes,0);return [];
        }catch(Exception e)when(AssetDependencyAudit.Expected(e)){return [new(path,"Resource dependency: "+e.Message)];}
    }
    public static void Register(SourceValidationRegistry registry,SourceStore store)
    {
        registry.RegisterBatch("map-resource-graph/1",c=>c.Files.Any(f=>ClientMapCatalog.Accepts(f.Before.Key)||f.After==null&&AssetDependencyAudit.Accepts(f.Before.Key)),(c,ct)=>
        {
            var overlay=c.Files.Where(f=>f.Before.Key.Root==SourceRoot.ClientProject).ToDictionary(f=>f.Before.Key.RelativePath,f=>f.After,StringComparer.Ordinal);var roots=c.Files.Where(f=>f.After!=null&&ClientMapCatalog.Accepts(f.Before.Key)&&f.Before.Key.RelativePath.EndsWith(".json",StringComparison.Ordinal)).Select(f=>f.Before.Key.RelativePath).ToHashSet(StringComparer.Ordinal);
            var deleted=overlay.Where(p=>p.Value==null).Select(p=>p.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);var issues=new List<ValidationIssue>();
            byte[] Read(string path){ct.ThrowIfCancellationRequested();SourcePaths.ValidateRelative(path);if(overlay.TryGetValue(path,out var candidate))return store.Blobs.Read(candidate??throw new InvalidDataException("Dependency is deleted: "+path));var snapshot=store.Read(store.Key(SourceRoot.ClientProject,path));return store.Blobs.Read(snapshot.Content??throw new InvalidDataException("Missing resource: "+path));}
            if(deleted.Count>0)
            {
                var dir=SourcePaths.Resolve(store.Profile,store.Key(SourceRoot.ClientProject,"assets/terrain"),false);var mapFiles=Directory.Exists(dir)?Directory.EnumerateFiles(dir,"*.json",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint}).Take(10001).ToArray():[];if(mapFiles.Length>10000)throw new InvalidDataException("Narrow this dependency review: more than10,000 map source files.");
                foreach(var file in mapFiles)
                {
                    string relative=Path.GetRelativePath(store.Profile.Roots[SourceRoot.ClientProject],file).Replace('\\','/');var key=store.Key(SourceRoot.ClientProject,relative);if(!ClientMapCatalog.Accepts(key)||overlay.GetValueOrDefault(relative)!=null||overlay.ContainsKey(relative)&&overlay[relative]==null)continue;
                    var visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);bool UsesDeleted(string p,int depth){if(deleted.Contains(p))return true;if(depth>64||!visited.Add(p))return false;try{return Dependencies(p,Read(p)).Any(d=>UsesDeleted(d,depth+1));}catch(Exception e)when(AssetDependencyAudit.Expected(e)){return false;}}
                    if(UsesDeleted(relative,0))roots.Add(relative);
                }
            }
            foreach(var root in roots){ct.ThrowIfCancellationRequested();try{issues.AddRange(Validate(root,Read(root),Read));}catch(Exception e)when(AssetDependencyAudit.Expected(e)){issues.Add(new(root,e.Message));}}
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
        });
    }
}
