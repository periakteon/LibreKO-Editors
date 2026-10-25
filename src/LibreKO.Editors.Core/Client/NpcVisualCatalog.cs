using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Client;
public static class NpcVisualCatalog
{
    public static JsonObject Default(SourceFileKey key)
    {
        if(!Accepts(key))throw new InvalidDataException("Unsupported NPC visual source.");var name=Path.GetFileName(key.RelativePath);return name switch{"index.json" or "plug_slots.json"=>new(),"role_fx.json"=>new(){["npcs"]=new JsonObject(),["questMarkers"]=new JsonObject()},_=>new(){[name.EndsWith(".anim.json",StringComparison.Ordinal)?"animations":name.EndsWith(".plugs.json",StringComparison.Ordinal)?"plugs":"parts"]=new JsonArray()}};
    }
    public static bool Accepts(SourceFileKey key)
    {var p=key.RelativePath.Split('/');return key.Root==SourceRoot.ClientProject&&p.Length==3&&p[0]=="assets"&&p[1]=="npcs"&&(p[2] is "index.json" or "plug_slots.json" or "role_fx.json"||p[2].EndsWith(".anim.json",StringComparison.Ordinal)||p[2].EndsWith(".plugs.json",StringComparison.Ordinal)||p[2].EndsWith(".fxplug.json",StringComparison.Ordinal));}
    public static void Register(SourceValidationRegistry registry,SourceStore store)
    {
        registry.Register("npc-visual/1",Accepts,t=>Validate(t.Key,store.Blobs.Read(t.Candidate)));
        registry.Register("compatible-assets/1",AssetDependencyAudit.Accepts,t=>AssetDependencyAudit.ValidateStandalone(t.Key,store.Blobs.Read(t.Candidate)));
        registry.RegisterBatch("asset-graph/1",c=>c.Files.Any(f=>Accepts(f.Before.Key)||AssetDependencyAudit.Accepts(f.Before.Key)),(c,ct)=>
        {
            var overlay=c.Files.ToDictionary(f=>f.Before.Key,f=>f.After);var issues=new List<ValidationIssue>();
            foreach(var file in c.Files.Where(f=>f.After!=null&&(Accepts(f.Before.Key)||AssetDependencyAudit.Accepts(f.Before.Key)))){ct.ThrowIfCancellationRequested();issues.AddRange(AssetDependencyAudit.Validate(store.Profile,file.Before.Key,file.After!,store,overlay));}
            // A replaced rig must also satisfy its existing sidecars, even when no sidecar was edited.
            foreach(var file in c.Files.Where(f=>AssetDependencyAudit.IsModel(f.Before.Key)))foreach(var suffix in new[]{".anim.json",".plugs.json",".fxplug.json"})
            {
                var key=store.Key(SourceRoot.ClientProject,Path.ChangeExtension(file.Before.Key.RelativePath,null)+suffix);if(overlay.ContainsKey(key))continue;var existing=store.Read(key);if(existing.Content!=null)issues.AddRange(AssetDependencyAudit.Validate(store.Profile,key,existing.Content,store,overlay));
            }
            foreach(var consumer in c.Files.Where(f=>AssetDependencyAudit.Accepts(f.Before.Key)).SelectMany(f=>AssetDependencyAudit.Dependents(store,f.Before.Key)).Distinct())
            {ct.ThrowIfCancellationRequested();var data=overlay.TryGetValue(consumer,out var candidate)?candidate:store.Read(consumer).Content;if(data!=null)issues.AddRange(AssetDependencyAudit.Validate(store.Profile,consumer,data,store,overlay));}
            return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
        });
    }
    public static IReadOnlyList<ValidationIssue> Validate(SourceFileKey key,byte[] bytes)
    {
        try
        {
            if(!Accepts(key))throw new InvalidDataException("Unsupported NPC visual source.");SourceLimits.CheckedLength(bytes.LongLength);var root=JsonNode.Parse(TextDocument.Decode(bytes).Text,documentOptions:new(){MaxDepth=SourceLimits.MaximumJsonDepth}) as JsonObject??throw new InvalidDataException("NPC visual source must be a JSON object.");var name=Path.GetFileName(key.RelativePath);
            if(name=="index.json"||name=="plug_slots.json")foreach(var field in root.Where(p=>int.TryParse(p.Key,out _))){if(name=="index.json")Stem(field.Value,true);else if(field.Value?.GetValue<int>() is not int n||n<0)throw new InvalidDataException("Plug count must be a nonnegative integer (absent models have unlimited slots).");}
            else if(name=="role_fx.json")foreach(var group in new[]{"npcs","questMarkers"}){if(!root.ContainsKey(group))continue;if(root[group] is not JsonObject values)throw new InvalidDataException("Role effects groups must be objects when present.");foreach(var pair in values.Where(p=>group!="npcs"||int.TryParse(p.Key,out _)))Stem(pair.Value,true);}
            else
            {
                var field=name.EndsWith(".anim.json",StringComparison.Ordinal)?"animations":name.EndsWith(".plugs.json",StringComparison.Ordinal)?"plugs":"parts";
                if(root[field] is not JsonArray rows||rows.Count>SourceLimits.MaximumJsonRows)throw new InvalidDataException("Expected bounded "+field+" array.");
                foreach(var value in rows)
                {
                    var row=value?.AsObject()??throw new InvalidDataException("Expected an object record.");
                    if(field=="animations")
                    {Int(row,"index",0);Stem(row["exported"],true);foreach(var n in new[]{"start","end","fps","blend","strike0","strike1","sound0","sound1","plugTraceStart","plugTraceEnd"})if(row.ContainsKey(n))ClientMapCatalog.Number(row[n]);if(ClientMapCatalog.Number(row["fps"]??JsonValue.Create(30))<=0||ClientMapCatalog.Number(row["end"]??JsonValue.Create(0))<ClientMapCatalog.Number(row["start"]??JsonValue.Create(0)))throw new InvalidDataException("Animation fps must be positive and end must follow start.");}
                    else if(field=="plugs")
                    {Int(row,"slot",0);Int(row,"joint",0);Stem(row["model"]);ClientMapCatalog.Vector(row["pos"],3);ClientMapDocument.Quaternion(ClientMapCatalog.Vector(row["quat"],4));if(ClientMapCatalog.Vector(row["scale"],3).Any(x=>x==0))throw new InvalidDataException("Plug scales cannot be zero.");if(row["anim"]!=null)Stem(row["anim"],true);}
                    else{Stem(row["fx"]);if(row.ContainsKey("bone"))Int(row,"bone",-1);if(row.ContainsKey("p"))ClientMapCatalog.Vector(row["p"],3);if(row.ContainsKey("dir"))ClientMapCatalog.Vector(row["dir"],3);if(row.ContainsKey("scale")&&ClientMapCatalog.Number(row["scale"])<=0)throw new InvalidDataException("FX scale must be positive.");}
                }
            }return [];
        }catch(Exception e)when(e is InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or FormatException){return [new(key.RelativePath,e.Message)];}
    }
    internal static int Int(JsonObject o,string field,int min){int n=o[field]?.GetValue<int>()??throw new InvalidDataException(field+" requires an integer.");if(n<min)throw new InvalidDataException(field+" is below "+min+".");return n;}
    public static string Stem(JsonNode? value,bool allowEmpty=false){if(value is not JsonValue v||!v.TryGetValue<string>(out var stem)||(!allowEmpty&&string.IsNullOrWhiteSpace(stem))||stem.Length>1024)throw new InvalidDataException("A resource stem/name is required.");if(stem.Length>0){SourcePaths.ValidateRelative(stem);if(stem.Contains('/')||stem.Contains('\\'))throw new InvalidDataException("Resource mappings require a stem, not a path.");}return stem;}
}
