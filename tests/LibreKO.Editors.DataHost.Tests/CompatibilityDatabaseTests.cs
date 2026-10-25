using LibreKO.Editors.Contracts;
using LibreKO.Editors.DataHost;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
public class CompatibilityDatabaseTests {
 const string Cs="Server=127.0.0.1;Port=33307;Database=libreko_editor_test_native;User=root;Pooling=false;";
 [Fact] public async Task ScanFindsOrphanReferencesWithoutChangingInventory(){
  await using var context=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseMySql(Cs,new MySqlServerVersion(new Version(8,2,0))).Options);await context.Database.EnsureCreatedAsync();
  if(!await context.Accounts.AnyAsync(x=>x.Id==330001)){context.Accounts.Add(new Account{Id=330001,Login="compat_fixture",Password="never report this",Nation=LibreKO.Common.Enums.AccountNation.Karus});await context.SaveChangesAsync();}
  var slots=Enumerable.Range(0,96).Select(i=>new InventorySlotDto(i,i==14?1999999999:0,5,(ushort)(i==14?1:0),0,0,0)).ToArray();var bytes=InventoryAdapter.Encode(slots);
  var character=await context.Characters.FindAsync(730001);if(character==null){character=new Character{Id=730001,AccountId=330001,Name="Compatibility fixture",Race=1,Class=101,Level=1};context.Characters.Add(character);}character.Items=bytes;await context.SaveChangesAsync();
  var service=new EditorDatabase(Cs,Path.Combine(Path.GetTempPath(),"compat-"+Guid.NewGuid()));var preflight=await service.CheckCompatibilityAsync();Assert.All(preflight.Modules,m=>Assert.True(m.CanSave,m.Reason));
  var report=await service.ScanInventoriesAsync(730000);Assert.Contains(report.Issues,i=>i.Code=="inventory.orphan"&&i.Record?.Id==730001&&i.Target.Contains("1999999999"));
  context.ChangeTracker.Clear();Assert.Equal(bytes,(await context.Characters.FindAsync(730001))!.Items);
  Assert.DoesNotContain("never report this",System.Text.Json.JsonSerializer.Serialize(report));
 }
 [Fact] public async Task IncompatibleSchemaCanBeInspectedAndCannotBeWritten(){
  var name="libreko_editor_test_compat_"+Guid.NewGuid().ToString("N");await using var connection=new MySqlConnection(Cs);await connection.OpenAsync();
  await new MySqlCommand("CREATE DATABASE `"+name+"`",connection).ExecuteNonQueryAsync();
  try{
   await new MySqlCommand("CREATE TABLE `"+name+"`.Items(Num int PRIMARY KEY) ENGINE=InnoDB",connection).ExecuteNonQueryAsync();
   var history=Path.Combine(Path.GetTempPath(),"compat-reject-"+Guid.NewGuid());var service=new EditorDatabase(new MySqlConnectionStringBuilder(Cs){Database=name}.ConnectionString,history);
   var report=await service.CheckCompatibilityAsync();Assert.False(report.Modules.Single(m=>m.Kind==RecordKind.Item).CanBrowse);Assert.Contains(report.Issues,i=>i.Code=="schema.column"&&i.Target=="Items.Name");
   var result=await service.SaveAsync(new(Guid.NewGuid(),new(new(RecordKind.Item,1),new(),"missing"),new(){["Name"]="Unsafe"},Create:true));Assert.Equal(SaveStatus.Invalid,result.Status);Assert.False(Directory.Exists(history));
  }finally{await new MySqlCommand("DROP DATABASE `"+name+"`",connection).ExecuteNonQueryAsync();}
 }
 [Fact] public void UnknownInventoryShapeIsPreservedReadOnly(){var bytes=new byte[865];var row=EditorDatabase.Snapshot(new Character{Id=1,Name="Custom",Items=bytes});Assert.Null(row.Inventory);Assert.Equal(865,bytes.Length);}
}
