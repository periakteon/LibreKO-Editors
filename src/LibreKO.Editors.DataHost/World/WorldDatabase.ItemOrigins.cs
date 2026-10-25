using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Sources;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Editors.DataHost.World;
public sealed partial class WorldDatabase
{
    public async Task<ItemOriginPage> ItemOriginsAsync(ItemOriginQuery query,SourceStore? sources)
    {
        if(query.ItemId<=0)throw new InvalidOperationException("Choose a positive item ID.");
        var page=Math.Clamp(query.Page,0,1_000_000);var size=Math.Clamp(query.Size,1,100);
        var report=await CheckCompatibilityAsync();Require(report,[WorldTable.Npcs]);
        var diagnostics=new List<string>();var results=new List<ItemOrigin>();
        await using var db=Open();
        if(!await db.Items.AsNoTracking().AnyAsync(i=>i.Num==query.ItemId))diagnostics.Add("The item is missing from the database; these are configured references, not active rewards.");
        var pools=new Dictionary<int,(int Matches,int Size)>();
        if(report.Tables.Single(t=>t.Table==WorldTable.MakeItemGroups).CanBrowse)
        {
            int count=0;
            await foreach(var pool in db.MakeItemGroups.AsNoTracking().AsAsyncEnumerable())
            {
                if(++count>1_000_000)throw new InvalidOperationException("Item origin lookup exceeds one million pool definitions.");
                var entries=pool.GetItems();var matches=entries.Count(i=>i==query.ItemId);
                if(matches>0&&pool.GroupNum is >=100 and <100_000_000)pools[pool.GroupNum]=(matches,entries.Length);
            }
        }
        else diagnostics.Add("Pooled drops unavailable: unsupported MakeItemGroups structure.");
        if(report.Tables.Single(t=>t.Table==WorldTable.NpcItems).CanBrowse)
        {
            var poolIds=pools.Keys.ToArray();var id=query.ItemId;
            var groups=await db.NpcItems.AsNoTracking().Where(d=>d.Index>0).Where(d=>d.Item1==id||d.Item2==id||d.Item3==id||d.Item4==id||d.Item5==id||d.Item6==id||d.Item7==id||poolIds.Contains(d.Item1)||poolIds.Contains(d.Item2)||poolIds.Contains(d.Item3)||poolIds.Contains(d.Item4)||poolIds.Contains(d.Item5)||poolIds.Contains(d.Item6)||poolIds.Contains(d.Item7)).ToArrayAsync();
            var ids=groups.Select(g=>g.Index).Distinct().ToArray();
            var owners=await db.Npcs.AsNoTracking().Where(n=>ids.Contains(n.ItemGroup)).ToArrayAsync();
            foreach(var group in groups)
            {
                var values=new[]{group.Item1,group.Item2,group.Item3,group.Item4,group.Item5,group.Item6,group.Item7};
                var chances=new[]{group.Percent1,group.Percent2,group.Percent3,group.Percent4,group.Percent5,group.Percent6,group.Percent7};
                for(int slot=0;slot<7;slot++)
                {
                    int value=values[slot];bool direct=value>=100_000_000&&value==id;
                    if(chances[slot]<=0||!direct&&!pools.ContainsKey(value))continue;
                    var weight=direct?(1,1):pools[value];
                    foreach(var npc in owners.Where(n=>n.ItemGroup==group.Index&&n.IsMonster==group.IsMonster))
                        results.Add(new("drop",new(WorldTable.Npcs,npc.Id,IsMonster:npc.IsMonster),npc.Name,new(WorldTable.NpcItems,group.Index,IsMonster:group.IsMonster),slot+1,chances[slot],direct?null:value,weight.Item1,weight.Item2));
                }
            }
        }
        else diagnostics.Add("Direct drops unavailable: unsupported NpcItems structure.");
        if(sources==null)diagnostics.Add("Merchant shop lookup unavailable: select a ServerData source root.");
        else
        {
            try
            {
                var key=sources.Key(SourceRoot.ServerData,"ItemSells.json");var snapshot=sources.Read(key);
                if(!snapshot.Exists)diagnostics.Add("Merchant shop lookup unavailable: ItemSells.json is missing.");
                else
                {
                    var document=JsonSourceDocument.Open(snapshot,sources.Blobs.Read(snapshot.Content!));
                    var matches=Enumerable.Range(0,document.RowCount).Select(i=>(Row:document.RowAt(i).Values,Ordinal:i)).Where(r=>FieldValueParser.ReadInt32(r.Row["ItemId"])==query.ItemId).ToArray();
                    var groups=matches.Select(r=>FieldValueParser.ReadInt32(r.Row["SellingGroup"])).Where(g=>g>0).Distinct().ToArray();
                    var owners=await db.Npcs.AsNoTracking().Where(n=>groups.Contains(n.SellingGroup)).ToArrayAsync();
                    foreach(var row in matches)
                    {
                        var group=FieldValueParser.ReadInt32(row.Row["SellingGroup"]);
                        foreach(var npc in owners.Where(n=>n.SellingGroup==group&&n.NpcType is NpcData.TypeTradeMerchant or NpcData.TypeRepairMerchant or NpcData.TypeFamiliarTamer))
                            results.Add(new("shop",new(WorldTable.Npcs,npc.Id,IsMonster:npc.IsMonster),npc.Name,null,FieldValueParser.ReadInt32(row.Row["Index"]),0,Source:key,SourceRow:row.Ordinal,ShopGroup:group,ShopLine:FieldValueParser.ReadInt32(row.Row["Line"])));
                    }
                    diagnostics.Add("Merchant results use the selected ItemSells.json and database shop-capable prototypes. Unsaved source drafts and live server caches are not queried.");
                }
            }
            catch(Exception e) when(e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or System.Text.Json.JsonException or FormatException or OverflowException)
            {diagnostics.Add("Merchant shop lookup unavailable: "+e.Message);}
        }
        var sorted=results.OrderBy(r=>r.Kind,StringComparer.Ordinal).ThenBy(r=>r.Owner.Id).ThenBy(r=>r.Owner.IsMonster).ThenBy(r=>r.Slot).ThenBy(r=>r.SourceRow).ToArray();
        return new(sorted.Skip(page*size).Take(size).ToArray(),sorted.Length,page,size,diagnostics);
    }
}
