using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using Microsoft.EntityFrameworkCore;
using LibreKO.Editors.Core;
namespace LibreKO.Editors.DataHost;

public sealed partial class EditorDatabase {
 sealed class NamedRow {public int Id {get;set;} public string Label {get;set;}="";}
 public async Task<Page<ReferenceEntry>> LookupAsync(ReferenceQuery query,IReadOnlyList<int>? ids=null){
  await using var db=Open();var size=Math.Clamp(query.Size,1,100);var page=Math.Clamp(query.Page,0,1000000);
  byte[]? acceptedSlots=null;
  if(query.Inventory is {} filter){
   if(query.Kind!="items"||filter.Slot is <0 or >=96)throw new InvalidOperationException("Choose a valid inventory position.");
   var other=filter.OtherHandItemId==0?(byte?)null:await db.Items.Where(i=>i.Num==filter.OtherHandItemId).Select(i=>(byte?)i.Slot).SingleOrDefaultAsync();
   if(filter.OtherHandItemId!=0&&other==null)throw new InvalidOperationException("The other equipped item is missing from the database.");
   acceptedSlots=Enumerable.Range(0,256).Select(x=>(byte)x).Where(code=>InventoryLayout.FitsSlot(filter.Slot,code)&&(filter.Slot!=6||HandsCompatible(code,other))&&(filter.Slot!=8||HandsCompatible(other,code))).ToArray();
  }
  if(query.Kind=="bindpoints"){
   if(query.Context is not {} zone)throw new InvalidOperationException("Choose a zone before looking up bind points.");
   var points=db.MapObjects.AsNoTracking().Where(x=>x.ZoneId==zone&&(x.Type==0||x.Type==7));
   if(ids!=null){if(ids.Count>100)throw new InvalidOperationException("Resolve up to 100 IDs at once.");points=points.Where(x=>ids.Contains(x.Index));}
   else if(int.TryParse(query.Text,out var point))points=points.Where(x=>x.Index==point);
   var totalPoints=await points.CountAsync();var foundPoints=await points.OrderBy(x=>x.Index).Skip(page*size).Take(size).ToArrayAsync();
   return new(foundPoints.Select(x=>new ReferenceEntry(x.Index,$"Bind point · Zone {zone} · X {x.X:0.##}, Z {x.Z:0.##}")).ToArray(),totalPoints,page,size);
  }
  IQueryable<NamedRow> rows=query.Kind switch{
   "items"=>db.Items.AsNoTracking().Where(x=>acceptedSlots==null||acceptedSlots.Contains(x.Slot)).Select(x=>new NamedRow{Id=x.Num,Label=x.Name}),
   "npcs"=>db.Npcs.AsNoTracking().Select(x=>new NamedRow{Id=x.Id,Label=x.Name}),
   "event-races"=>db.CollectionRaces.AsNoTracking().Select(x=>new NamedRow{Id=x.Id,Label=x.Name}),
   "event-lotteries"=>db.LotteryEvents.AsNoTracking().Select(x=>new NamedRow{Id=x.Id,Label=x.Name}),
   "zones"=>db.ZoneInfos.AsNoTracking().Select(x=>new NamedRow{Id=x.ZoneNo,Label=x.MapName}),
   "magic"=>db.Magic.AsNoTracking().Select(x=>new NamedRow{Id=x.Id,Label=x.EnName}),
   "accounts"=>db.Accounts.AsNoTracking().Select(x=>new NamedRow{Id=x.Id,Label=x.Login}),
   "clans"=>db.Knights.AsNoTracking().Select(x=>new NamedRow{Id=x.Id,Label=x.Name}),
   "titles"=>db.AchievementTitles.AsNoTracking().Select(x=>new NamedRow{Id=x.Id,Label=x.Name}),
   "extensions"=>db.Items.AsNoTracking().GroupBy(x=>x.Extension).Select(g=>new NamedRow{Id=g.Key,Label=g.Min(x=>x.Name)!}),
   "premiums"=>db.PremiumItems.AsNoTracking().Select(x=>new NamedRow{Id=x.Type,Label="Premium type "+x.Type+" · drop +"+x.DropPercent+"% · Noah +"+x.NoahPercent+"%"}),
   _=>throw new InvalidOperationException("Unsupported reference type.")};
  if(ids!=null){if(ids.Count>100)throw new InvalidOperationException("Resolve up to 100 IDs at once.");rows=rows.Where(x=>ids.Contains(x.Id));}
  else if(!string.IsNullOrWhiteSpace(query.Text)){var text=query.Text.Trim();rows=int.TryParse(text,out var id)?rows.Where(x=>x.Id==id):rows.Where(x=>x.Label.Contains(text));}
  var total=await rows.CountAsync();var found=await rows.OrderBy(x=>x.Id).Skip(page*size).Take(size).ToArrayAsync();
  var result=found.Select(x=>new ReferenceEntry(x.Id,query.Kind=="extensions"?$"Extension group · {x.Label}":x.Label)).ToArray();
  if(query.Kind=="items"&&result.Length>0){var keys=result.Select(x=>x.Id).ToArray();var items=await db.Items.AsNoTracking().Where(x=>keys.Contains(x.Num)).ToArrayAsync();result=result.Select(x=>x with{Details=Snapshot(items.Single(i=>i.Num==x.Id)).Values}).ToArray();}
  return new(result,total,page,size);
 }
}
