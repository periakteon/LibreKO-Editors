using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using MySqlConnector;
namespace LibreKO.Editors.DataHost;
public sealed partial class EditorDatabase {
 public async Task<DatabaseCompatibility> CheckCompatibilityAsync(){
  await using var db=Open();var kinds=new[]{RecordKind.Item,RecordKind.Character,RecordKind.Account};
  var expected=kinds.ToDictionary(k=>k,k=>{
   var entity=db.Model.FindEntityType(FieldSchema.EntityType(k))!;var table=entity.GetTableName()!;var store=StoreObjectIdentifier.Table(table,null);var key=entity.FindPrimaryKey()!.Properties;
   return entity.GetProperties().Select(p=>new CompatibilityColumn(table,p.GetColumnName(store)!,p.GetColumnType()??p.GetRelationalTypeMapping().StoreType,p.IsNullable,key.Contains(p),p.GetDefaultValue()!=null||p.GetDefaultValueSql()!=null,p.ValueGenerated!=ValueGenerated.Never)).ToArray();
  });
  var lookups=new[]{((IQueryable)db.Magic,new[]{"Id","EnName"}),((IQueryable)db.ZoneInfos,new[]{"ZoneNo","MapName"}),((IQueryable)db.MapObjects,new[]{"ZoneId","Type","Index","X","Z"}),((IQueryable)db.Knights,new[]{"Id","Name"}),((IQueryable)db.AchievementTitles,new[]{"Id","Name"}),((IQueryable)db.PremiumItems,new[]{"Type","DropPercent","NoahPercent"})}.Select(pair=>{
   var entity=db.Model.FindEntityType(pair.Item1.ElementType)!;var table=entity.GetTableName()!;var store=StoreObjectIdentifier.Table(table,null);
   return entity.GetProperties().Where(p=>pair.Item2.Contains(p.Name)).Select(p=>new CompatibilityColumn(table,p.GetColumnName(store)!,p.GetColumnType()??p.GetRelationalTypeMapping().StoreType,p.IsNullable)).ToArray();
  }).ToArray();
  var inspected=await SchemaInspection.ReadAsync(connectionString,expected.Values.Concat(lookups).SelectMany(x=>x).Select(x=>x.Table));var actual=inspected.Columns;var engines=inspected.Engines;
  var issues=new List<CompatibilityIssue>();
  foreach(var columns in expected.Values){var table=columns[0].Table;issues.AddRange(CompatibilityRules.CompareColumns(columns,actual.Where(c=>c.Table==table).ToArray()));if(engines.TryGetValue(table,out var engine)&&!engine.Equals("InnoDB",StringComparison.OrdinalIgnoreCase))issues.Add(new("schema.engine",CompatibilitySeverity.Blocking,"Schema",table,$"Engine {engine} does not provide the transactional behavior required by this editor."));}
  foreach(var columns in lookups){var present=actual.Where(c=>c.Table==columns[0].Table&&columns.Any(e=>e.Name.Equals(c.Name,StringComparison.OrdinalIgnoreCase))).Select(c=>c with{PrimaryKey=false}).ToArray();var faults=CompatibilityRules.CompareColumns(columns,present);if(faults.Count>0)issues.Add(new("schema.lookup",CompatibilitySeverity.Warning,"Named references",columns[0].Table,"Related name pickers or validation may be unavailable: "+string.Join("; ",faults.Take(3).Select(f=>f.Target+": "+f.Message))));}
  var modules=kinds.Select(kind=>{
   var tables=(kind==RecordKind.Item?new[]{RecordKind.Item}:kind==RecordKind.Account?new[]{RecordKind.Account,RecordKind.Character}:kinds).Select(k=>expected[k][0].Table).ToArray();
   var faults=issues.Where(i=>tables.Contains(i.Target.Split('.')[0])&&i.Severity==CompatibilitySeverity.Blocking).ToArray();
   var browse=!faults.Any(i=>i.Code is not ("schema.required-extra" or "schema.engine"));return new ModuleCompatibility(kind,browse,faults.Length==0,faults.Length==0?"Mapped database structure supported":string.Join("; ",faults.Take(3).Select(i=>i.Target+": "+i.Message)));
  }).ToArray();
  return new(inspected.Database,inspected.ServerVersion,modules,issues);
 }
 public async Task<CompatibilityItemBatch> ScanItemsAsync(int? afterId=null){
  await using var db=Open();var found=await db.Items.AsNoTracking().Where(i=>afterId==null||i.Num>afterId).OrderBy(i=>i.Num).Take(1000).Select(i=>new{i.Num,i.Name,i.Kind,i.Slot,i.Extension,i.Damage,i.Ac,i.Duration}).ToArrayAsync();
  return new(found.Select(i=>new CompatibilityItem(i.Num,i.Name,i.Kind,i.Slot,i.Extension,i.Damage,i.Ac,i.Duration)).ToArray(),found.Length==1000?found[^1].Num:null);
 }
 public async Task<CompatibilityInventoryBatch> ScanInventoriesAsync(int? afterId=null){
  await using var db=Open();var rows=await db.Characters.AsNoTracking().Where(c=>afterId==null||c.Id>afterId).OrderBy(c=>c.Id).Take(100).Select(c=>new{c.Id,c.Name,c.Items}).ToArrayAsync();
  var decoded=new List<(int Id,string Name,IReadOnlyList<InventorySlotDto> Slots)>();var issues=new List<CompatibilityIssue>();
  foreach(var row in rows){try{decoded.Add((row.Id,row.Name,InventoryAdapter.Decode(row.Items)));}catch(InvalidDataException){issues.Add(new("inventory.format",CompatibilitySeverity.Warning,"Inventory",$"{row.Id} · {row.Name}",$"Unrecognized {row.Items.Length}-byte inventory. The blob is preserved and inventory editing is disabled.",new(RecordKind.Character,row.Id)));}}
  var ids=decoded.SelectMany(r=>r.Slots).Where(s=>s.ItemId!=0).Select(s=>s.ItemId).Distinct().ToArray();var present=(await db.Items.AsNoTracking().Where(i=>ids.Contains(i.Num)).Select(i=>i.Num).ToArrayAsync()).ToHashSet();
  foreach(var row in decoded)foreach(var slot in row.Slots.Where(s=>s.ItemId!=0&&!present.Contains(s.ItemId)))issues.Add(new("inventory.orphan",CompatibilitySeverity.Warning,"Inventory",$"Character {row.Id} · slot {slot.Index} · item {slot.ItemId}",$"{row.Name}: inventory references an item absent from the database. The scan preserves it.",new(RecordKind.Character,row.Id)));
  return new(rows.Length,rows.Length==100?rows[^1].Id:null,issues);
 }
}
