using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.World;

namespace LibreKO.Editors.Core.Sources;

public record SourceJsonFormat(string Id,IReadOnlyList<string> KeyFields,WorldTable? Table);
public static class ServerJsonCatalog
{
    public static void RegisterIdentityValidation(SourceValidationRegistry registry,SourceStore store,Func<WorldCatalog?>? catalog=null)
    {
        registry.RegisterBatch("server-identities/1",c=>c.Files.Any(f=>Accepts(f.Before.Key)),(c,ct)=>
        {
            var issues=new List<ValidationIssue>();foreach(var file in c.Files.Where(f=>f.After!=null&&Accepts(f.Before.Key)))
            {
                ct.ThrowIfCancellationRequested();var format=Format(file.Before.Key.RelativePath)!;
                // Spawn Index is assigned by active shard order, not persisted from a source row.
                if(format.Table==WorldTable.NpcPositions)continue;
                var fields=Fields(format,catalog?.Invoke());if(fields.Count==0)continue;
                Dictionary<string,int> Counts(byte[] bytes)
                {
                    var rows=JsonNode.Parse(TextDocument.Decode(bytes).Text) as JsonArray??throw new InvalidDataException("Expected source row array.");var counts=new Dictionary<string,int>(StringComparer.Ordinal);
                    foreach(var row in rows.OfType<JsonObject>()){var normalized=Normalize(row,fields);var key=new JsonArray(format.KeyFields.Select(k=>normalized[k]?.DeepClone()).ToArray()).ToJsonString();counts[key]=counts.GetValueOrDefault(key)+1;}return counts;
                }
                var before=file.Before.Content==null?new Dictionary<string,int>():Counts(store.Blobs.Read(file.Before.Content));var after=Counts(store.Blobs.Read(file.After!));
                foreach(var pair in after.Where(p=>p.Value>Math.Max(1,before.GetValueOrDefault(p.Key))))issues.Add(new(file.Before.Key.RelativePath,"New duplicate persisted identity ("+string.Join(", ",format.KeyFields)+"): "+pair.Key+". Choose distinct keys; existing legacy duplicates are retained."));
            }return Task.FromResult<IReadOnlyList<ValidationIssue>>(issues);
        });
    }
    public static SourceJsonFormat? Format(string filename)
    {
        if(filename.Contains('/') || filename.Contains('\\')) return null;
        if(Regex.IsMatch(filename,@"^NpcPositions\.zone.*\.json$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant)) return new("NpcPositions",WorldKeys.Fields(WorldTable.NpcPositions),WorldTable.NpcPositions);
        foreach(var table in Enum.GetValues<WorldTable>()) if(string.Equals(filename,table+".json",StringComparison.OrdinalIgnoreCase)) return new(table.ToString(),WorldKeys.Fields(table),table);
        return filename.ToLowerInvariant() switch
        {
            "itemsells.json"=>new("ItemSells",["SellingGroup","Line","Index"],null),
            "itemexchangeexps.json"=>new("ItemExchangeExps",["Index"],null),
            "miningexchanges.json"=>new("MiningExchanges",["Index"],null),
            "miningfishingitems.json"=>new("MiningFishingItems",["Index"],null), _=>null
        };
    }
    public static bool Accepts(SourceFileKey key)=>key.Root==SourceRoot.ServerData && Format(key.RelativePath)!=null;
    public static void Register(SourceValidationRegistry registry,BlobStore blobs,Func<WorldCatalog?>? catalog=null)
        =>registry.Register("server-json/1",Accepts,ticket=>
        {
            var bytes=blobs.Read(ticket.Candidate);
            var doc=JsonSourceDocument.Open(new(ticket.Key,true,ticket.Candidate.Sha256,ticket.Candidate),bytes);
            return Validate(doc,catalog?.Invoke());
        });
    public static IReadOnlyList<WorldFieldDescriptor> Fields(SourceJsonFormat format,WorldCatalog? catalog)
    {
        if(format.Table is WorldTable table) return catalog?.Tables.Single(x=>x.Table==table).Fields ?? [];
        var names=format.Id switch
        {
            "ItemSells"=>"SellingGroup:int Line:byte Index:byte ItemId:int",
            "ItemExchangeExps"=>"Index:int RandomFlag:byte ExchangeItem1:int ExchangeCount1:int ExchangeItem2:int ExchangeCount2:int ExchangeItem3:int ExchangeCount3:int ExchangeItem4:int ExchangeCount4:int ExchangeItem5:int ExchangeCount5:int",
            "MiningExchanges"=>"Index:short NpcId:short GiveEffect:byte OreType:byte OriginItemNum:int GiveItemNum:int GiveItemCount:short SuccessRate:int",
            "MiningFishingItems"=>"Index:int Type:byte WarStatus:byte UseItemType:byte GiveItemNum:int GiveItemCount:short SuccessRate:int", _=>throw new InvalidOperationException("Unsupported service schema.")
        };
        return names.Split(' ').Select(s=>
        {
            var parts=s.Split(':'); var type=parts[1];
            return new WorldFieldDescriptor(new(parts[0],"Source properties",FieldKind.Integer,true,type=="byte"?0:type=="short"?short.MinValue:int.MinValue,type=="byte"?255:type=="short"?short.MaxValue:int.MaxValue),parts[0],false,null);
        }).ToArray();
    }
    public static JsonObject Normalize(JsonObject row,IReadOnlyList<WorldFieldDescriptor> fields,JsonObject? defaults=null)
    {
        var result=defaults==null?new JsonObject():(JsonObject)defaults.DeepClone();
        foreach(var descriptor in fields)
        {
            var field=descriptor.Field; var matches=row.Where(x=>string.Equals(x.Key,field.Name,StringComparison.OrdinalIgnoreCase)).ToArray();
            if(matches.Length>1) throw new InvalidDataException("Ambiguous case-insensitive property: "+field.Name);
            if(matches.Length==0) continue;
            var value=matches[0].Value;
            if(value==null) { if(field.Kind==FieldKind.Text) { result[field.Name]=""; continue; } if(!descriptor.Nullable) throw new InvalidDataException(field.Name+" cannot be null."); result[field.Name]=null; continue; }
            var raw=value.ToString();
            if(field.Kind==FieldKind.Text) { if(value.GetValueKind()!=System.Text.Json.JsonValueKind.String || descriptor.MaxLength is int max && raw.Length>max) throw new InvalidDataException(field.Name+" is invalid text."); result[field.Name]=raw; }
            else if(field.Kind==FieldKind.Boolean)
            { result[field.Name]=raw switch { "true" or "1"=>true,"false" or "0"=>false,_=>throw new InvalidDataException(field.Name+" must be boolean or 0/1.") }; }
            else
            {
                if(!decimal.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out var number) || field.Minimum is decimal min && number<min || field.Maximum is decimal max && number>max || field.Kind!=FieldKind.Decimal && decimal.Truncate(number)!=number) throw new InvalidDataException(field.Name+" is outside its supported numeric range.");
                result[field.Name]=field.Kind==FieldKind.Decimal ? JsonValue.Create((double)number) : JsonNode.Parse(number.ToString("0",CultureInfo.InvariantCulture));
            }
        }
        return result;
    }
    public static IReadOnlyList<ValidationIssue> Validate(JsonSourceDocument document,WorldCatalog? catalog=null)
    {
        var format=Format(document.Snapshot.Key.RelativePath) ?? throw new InvalidOperationException("Unsupported server source.");
        if(document.Root is not JsonArray array || array.Any(x=>x is not JsonObject)) return [new("Rows","Expected an array of object rows.")];
        var fields=Fields(format,catalog);
        if(fields.Count==0) return [new("Schema","Offline world metadata is required before applying this source.")];
        var issues=new List<ValidationIssue>();
        for(int i=0;i<array.Count;i++)
        {
            try { _=Normalize((JsonObject)array[i]!,fields); }
            catch(Exception e) when(e is InvalidDataException or InvalidOperationException or FormatException) { issues.Add(new("/"+i,e.Message)); }
        }
        return issues;
    }
}
