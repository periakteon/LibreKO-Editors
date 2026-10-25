using LibreKO.Editors.Contracts;
using LibreKO.Editors.DataHost;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

public class ReferenceTests {
 [Fact] public async Task InventoryChoicesAreFilteredBeforePaginationAndSearch(){
  var service=await Fixture();await using var db=Context();foreach(var (id,slot) in new[]{(123450011,(byte)7),(123450012,(byte)5),(123450013,(byte)7),(123450014,(byte)3),(123450015,(byte)2)}){if(!await db.Items.AnyAsync(i=>i.Num==id))db.Items.Add(new ItemData{Num=id,Name="compat_fixture_"+id,Slot=slot,Duration=100});}await db.SaveChangesAsync();
  var first=await service.LookupAsync(new("items","compat_fixture_",Size:1,Inventory:new(1)));Assert.Equal(2,first.Total);Assert.Equal(123450011,Assert.Single(first.Rows).Id);
  var second=await service.LookupAsync(new("items","compat_fixture_",Page:1,Size:1,Inventory:new(1)));Assert.Equal(123450013,Assert.Single(second.Rows).Id);
  Assert.Empty((await service.LookupAsync(new("items","123450012",Inventory:new(1)))).Rows);
  Assert.Empty((await service.LookupAsync(new("items","123450014",Inventory:new(6,123450015)))).Rows);
  Assert.Empty((await service.LookupAsync(new("items","compat_fixture_",Inventory:new(48)))).Rows);
 }
 const string Cs="Server=127.0.0.1;Port=33307;Database=libreko_editor_test_native;User=root;Pooling=false;";
 static AppDbContext Context()=>new(new DbContextOptionsBuilder<AppDbContext>().UseMySql(Cs,new MySqlServerVersion(new Version(8,2,0))).Options);
 static async Task<EditorDatabase> Fixture(){
  await using var context=Context();await context.Database.EnsureCreatedAsync();
  if(!await context.Items.AnyAsync(x=>x.Num==123450001))context.Items.Add(new ItemData{Num=123450001,Name="Test fixture",Damage=10,Weight=7});
  if(!await context.Accounts.AnyAsync(x=>x.Id==300001))context.Accounts.Add(new Account{Id=300001,Login="reference_fixture",Password="fixture-only"});
  await context.SaveChangesAsync();return new EditorDatabase(Cs,Path.Combine(Path.GetTempPath(),"reference-test"));
 }

 [Fact] public void NumericCodesHaveNamesAndDatabaseReferencesHaveLookupKinds(){
  var item=FieldSchema.For(RecordKind.Item);var player=FieldSchema.For(RecordKind.Character);
  Assert.Equal("Sword (two-handed)",item.Single(f=>f.Name=="Kind").Choices!["22"]);
  Assert.Equal("items",item.Single(f=>f.Name=="ItemPlusId").Reference);
  Assert.Equal("zones",player.Single(f=>f.Name=="MapId").Reference);
  Assert.Contains("Warrior",player.Single(f=>f.Name=="Class").Choices!["101"]);
 }
 [Theory][InlineData("zones")][InlineData("magic")][InlineData("accounts")][InlineData("clans")][InlineData("titles")][InlineData("extensions")]
 public async Task EveryReferenceKindSupportsSearchAndResolution(string kind){var db=await Fixture();await db.LookupAsync(new(kind,"does not exist"));await db.LookupAsync(new(kind),[0]);}
 [Fact] public async Task LookupSearchesNamesAndResolvesNumericIdsWithItemDetails(){
  var service=await Fixture();
  var results=await service.LookupAsync(new("items","123450001"));Assert.Single(results.Rows);Assert.NotEmpty(results.Rows[0].Label);Assert.NotNull(results.Rows[0].Details?["Slot"]);
  var byName=await service.LookupAsync(new("items",results.Rows[0].Label));Assert.Contains(byName.Rows,x=>x.Id==123450001);
  var accounts=await service.LookupAsync(new("accounts"),[300001]);var account=Assert.Single(accounts.Rows);Assert.Equal("reference_fixture",account.Label);Assert.Null(account.Details);
 }
 [Fact] public async Task BindPointLookupIsScopedToItsZone(){
  const string cs="Server=127.0.0.1;Port=33307;Database=libreko_editor_test_native;User=root;Pooling=false;";
  await using var context=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseMySql(cs,new MySqlServerVersion(new Version(8,2,0))).Options);
  await context.Database.EnsureCreatedAsync();
  await context.MapObjects.Where(x=>x.Id==910001||x.Id==910002).ExecuteDeleteAsync();context.MapObjects.AddRange(new MapObjectData{Id=910001,ZoneId=1,Index=7,Type=0,X=100,Z=200},new MapObjectData{Id=910002,ZoneId=2,Index=8,Type=0,X=300,Z=400});await context.SaveChangesAsync();
  var db=new EditorDatabase(cs,Path.GetTempPath());await Assert.ThrowsAsync<InvalidOperationException>(()=>db.LookupAsync(new("bindpoints")));var rows=await db.LookupAsync(new("bindpoints",Context:1));Assert.Contains(rows.Rows,x=>x.Id==7&&x.Label.Contains("Zone 1"));Assert.DoesNotContain(rows.Rows,x=>x.Id==8);
 }
}
