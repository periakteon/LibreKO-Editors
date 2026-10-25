using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Client;
public static class ClientMapCatalog
{
    public static bool Accepts(SourceFileKey key)
    {var p=key.RelativePath.Split('/');return key.Root==SourceRoot.ClientProject&&p.Length==4&&p[0]=="assets"&&p[1]=="terrain"&&p[2].Length>0&&p[3] is "placements.json" or "areas.json" or "water.json" or "fx_placements.json" or "collision.bin";}
    public static void Register(SourceValidationRegistry registry,BlobStore blobs,SourceStore? store=null){registry.Register("client-map/1",Accepts,t=>Validate(t.Key,blobs.Read(t.Candidate)));if(store!=null)ClientResourceGraph.Register(registry,store);}
    public static IReadOnlyList<ValidationIssue> Validate(SourceFileKey key,byte[] bytes)
    {
        var issues=new List<ValidationIssue>();try
        {
            if(!Accepts(key))throw new InvalidDataException("Unsupported client map source.");
            if(key.RelativePath.EndsWith("collision.bin",StringComparison.Ordinal)){CollisionCodec.Read(bytes);return issues;}
            SourceLimits.CheckedLength(bytes.Length);var root=JsonNode.Parse(TextDocument.Decode(bytes).Text,documentOptions:new(){MaxDepth=SourceLimits.MaximumJsonDepth}) as JsonArray??throw new InvalidDataException("Map source must be an ordered JSON array.");
            if(root.Count>SourceLimits.MaximumJsonRows)throw new InvalidDataException("Too many map rows.");
            for(int i=0;i<root.Count;i++)try{Row((JsonObject)root[i]!,Path.GetFileName(key.RelativePath));}catch(Exception e){issues.Add(new("/"+i,e.Message));if(issues.Count>=100)break;}
        }catch(Exception e){issues.Add(new("Source",e.Message));}return issues;
    }
    public static JsonObject Default(string name)=>name switch
    {
        "placements.json"=>JsonNode.Parse("{\"name\":\"\",\"p\":[0,0,0],\"r\":[0,0,0,1],\"s\":[1,1,1],\"e\":0,\"et\":0,\"b\":0,\"n\":0}")!.AsObject(),
        "fx_placements.json"=>JsonNode.Parse("{\"fx\":\"\",\"p\":[0,0,0],\"q\":[0,0,0,1],\"s\":1}")!.AsObject(),
        "areas.json"=>JsonNode.Parse("{\"id\":0,\"minX\":0,\"maxX\":4,\"minZ\":0,\"maxZ\":4}")!.AsObject(),
        "water.json"=>JsonNode.Parse("{\"tex\":\"\",\"cols\":2,\"rows\":2,\"level_y\":0,\"verts\":[[0,0,0],[4,0,0],[0,0,4],[4,0,4]],\"uv\":[[0,0],[1,0],[0,1],[1,1]]}")!.AsObject(),_=>throw new InvalidDataException("Unsupported map JSON.")
    };
    static void Row(JsonObject row,string name)
    {
        if(name=="placements.json"||name=="fx_placements.json")
        {
            Text(row,name=="placements.json"?"name":"fx");Vector(row["p"],3);ClientMapDocument.Quaternion(Vector(row[name=="placements.json"?"r":"q"],4));
            if(name=="placements.json"){if(Vector(row["s"],3).Any(v=>v==0))throw new InvalidDataException("Scale components cannot be zero.");if(row.ContainsKey("e"))foreach(var f in new[]{"e","et","b","n"})Integer(row,f);}
            else if(row.ContainsKey("s")&&Number(row["s"])<=0)throw new InvalidDataException("FX scale must be positive.");
        }
        else if(name=="areas.json")
        {Integer(row,"id");if(Number(row["minX"])>Number(row["maxX"])||Number(row["minZ"])>Number(row["maxZ"]))throw new InvalidDataException("Area minimum must not exceed maximum.");}
        else
        {
            Text(row,"tex");int cols=Integer(row,"cols"),rows=Integer(row,"rows");Number(row["level_y"]);
            if(cols<2||rows<2||(long)cols*rows>SourceLimits.MaximumJsonRows||row["verts"] is not JsonArray verts||verts.Count!=(long)cols*rows)throw new InvalidDataException("Water rows×columns must match 2D grid vertex count (maximum one million).");
            foreach(var v in verts)Vector(v,3);if(row.ContainsKey("uv")){if(row["uv"] is not JsonArray uv||uv.Count!=verts.Count)throw new InvalidDataException("Water UV count must match the grid.");foreach(var v in uv)Vector(v,2);}
        }
    }
    static void Text(JsonObject row,string name){if(row[name] is not JsonValue value||!value.TryGetValue<string>(out var text)||text.Length>4096)throw new InvalidDataException(name+" must be a string of at most 4096 characters.");if(text.Contains('\\')||text.Contains('/')||text.Contains(':')||text is "." or "..")throw new InvalidDataException(name+" must be a resource stem, not a path.");}
    static int Integer(JsonObject row,string key)=>row[key]?.GetValue<int>()??throw new InvalidDataException(key+" needs an integer.");
    public static float Number(JsonNode? value){if(value==null||!float.TryParse(value.ToJsonString(),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var n)||!float.IsFinite(n))throw new InvalidDataException("A finite numeric value is required.");return n;}
    public static float[] Vector(JsonNode? value,int count){if(value is not JsonArray array||array.Count!=count)throw new InvalidDataException("Expected a numeric array of length "+count+".");return array.Select(Number).ToArray();}
}
