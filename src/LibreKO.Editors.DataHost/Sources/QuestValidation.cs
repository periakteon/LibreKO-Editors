using System.Globalization;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
using LibreKO.Quests;
using LibreKO.Quests.Catalog;
using LibreKO.Quests.Text;
namespace LibreKO.Editors.DataHost.Sources;

public sealed class QuestValidation(SourceStore store)
{
    public SourceValidationAudit Audit(IReadOnlyList<SourceValidationTicket> tickets,IReadOnlyList<SourceFileKey>? deleted=null)
    {using var reads=SourceReadScope.Begin();var issues=Validate(tickets,deleted);return new(issues,reads.Seal());}
    public IReadOnlyList<ValidationIssue> Validate(IReadOnlyList<SourceValidationTicket> tickets,IReadOnlyList<SourceFileKey>? deleted=null)
    {
        var issues=new List<ValidationIssue>();
        try
        {
            var includes=new QuestDraftIncludes(store,tickets,deleted);var bytes=includes.Read(SourceRoot.ServerQuests,"quest-manifest.json")??throw new InvalidDataException("A valid quest-manifest.json is required.");
            var key=new SourceFileKey(store.Profile.Id,SourceRoot.ServerQuests,SourceIdentity.Root(store.Profile.Roots[SourceRoot.ServerQuests]),"quest-manifest.json");
            var manifest=JsonSourceDocument.Open(new(key,false,"",null),bytes);var entries=QuestManifest.Entries(manifest);
            var catalog=new SeedCatalog(includes,store);
            var affected=entries.Concat(tickets.Where(t=>t.Key.Root==SourceRoot.ServerQuests&&t.Key.RelativePath.EndsWith(".quest",StringComparison.OrdinalIgnoreCase)).Select(t=>t.Key.RelativePath)).Distinct(StringComparer.OrdinalIgnoreCase);
            foreach(var entry in affected)
            {
                try
                {
                    var candidate=includes.Read(SourceRoot.ServerQuests,entry)??throw new InvalidDataException("Manifest source is missing or deleted: "+entry);
                    var text=TextDocument.Decode(candidate).Text;QuestDraftIncludes.CheckText(text);
                    var compilation=QuestCompilation.Create(text,entry,catalog,includes.ForFile(entry));
                    foreach(var diagnostic in compilation.Diagnostics.Where(d=>d.Severity==DiagnosticSeverity.Error))
                    {var at=compilation.Source.GetLinePosition(diagnostic.Span.Start);issues.Add(new(entry+":"+(at.Line+1)+":"+(at.Character+1),diagnostic.Id+" · "+diagnostic.Message+(diagnostic.Hint==null?"":" · "+diagnostic.Hint)));}
                }
                catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException){issues.Add(new(entry+":1:1",e.Message));}
            }
        }
        catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException){issues.Add(new("quest-manifest.json:1:1",e.Message));}
        return issues;
    }
    // Immutable selected-source names only. This implements no runtime host/interpreter or network API.
    sealed class SeedCatalog: IQuestCatalog
    {
        readonly Dictionary<long,string> items=[],npcs=[],zones=[],exchanges=[];readonly HashSet<long> stacks=[];
        public SeedCatalog(QuestDraftIncludes includes,SourceStore store)
        {
            IEnumerable<JsonObject> Rows(string path)
            {var bytes=includes.Read(SourceRoot.ServerData,path);if(bytes==null)return [];var text=TextDocument.Decode(bytes).Text;return JsonNode.Parse(text) is JsonArray a?a.OfType<JsonObject>().ToArray():throw new InvalidDataException("Invalid source name array: "+path);}
            long Id(JsonObject row,string field)=>long.TryParse(row[field]?.ToString(),NumberStyles.Integer,CultureInfo.InvariantCulture,out var id)?id:0;
            if(!store.Profile.Roots.TryGetValue(SourceRoot.ServerData,out var root)||!Directory.Exists(root))return;SourceIdentity.Root(root);
            var shards=Directory.GetFiles(root,"Items.slot*.json").Select(System.IO.Path.GetFileName).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach(var file in shards.Length>0?shards:new[]{"Items.json"})foreach(var row in Rows(file!)){var id=Id(row,"Num");items[id]=row["Name"]?.ToString()??"";if(Id(row,"Countable")==1)stacks.Add(id);}
            foreach(var row in Rows("Npcs.json"))npcs[Id(row,"Id")]=row["Name"]?.ToString()??"";
            foreach(var row in Rows("ZoneInfos.json"))zones[Id(row,"ZoneNo")]=row["MapName"]?.ToString()??"";
            foreach(var row in Rows("ItemExchanges.json"))exchanges[Id(row,"Index")]=row["Name"]?.ToString()??"Item exchange";
        }
        public bool KnowsItems=>items.Count>0;public bool KnowsExchanges=>exchanges.Count>0;public bool KnowsQuests=>false;public bool KnowsNpcs=>npcs.Count>0;public bool KnowsZones=>zones.Count>0;public bool KnowsText=>false;
        public string? ItemName(long id)=>items.GetValueOrDefault(id);public bool ItemStacks(long id)=>stacks.Contains(id);public string? ExchangeSummary(long id)=>exchanges.GetValueOrDefault(id);
        public string? NpcName(long id)=>npcs.GetValueOrDefault(id);public string? ZoneName(long id)=>zones.GetValueOrDefault(id);public string? MapName(long id)=>ZoneName(id);
        public string? QuestSummary(long id)=>null;public string? QuestTitle(long id)=>null;public IReadOnlyCollection<long> NpcsForQuest(long id)=>[];public string? EventTriggerSummary(long npc,long id)=>null;public string? TalkText(long id)=>null;public string? MenuText(long id)=>null;
    }
}
