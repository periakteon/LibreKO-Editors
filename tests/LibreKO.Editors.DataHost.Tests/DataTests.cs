using LibreKO.Editors.Contracts;
using LibreKO.Editors.DataHost;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Common.Domain.Entities.GameData;
using Microsoft.EntityFrameworkCore;
public class DataTests {
 [Fact]public void FieldSchemaExcludesComputedProperties(){var fields=FieldSchema.For(RecordKind.Item);Assert.Contains(fields,f=>f.Name=="Damage"&&f.Writable);Assert.DoesNotContain(fields,f=>f.Name=="IsUntradeable");Assert.False(FieldSchema.For(RecordKind.Character).Single(f=>f.Name=="AccountId").Writable);}
 [Fact]public void InventoryRoundTripsAllMetadata(){var slots=Enumerable.Range(0,96).Select(i=>new InventorySlotDto(i,1001,17,2,4,9007199254740993L,42)).ToArray();var bytes=InventoryAdapter.Encode(slots);Assert.Equal(2016,bytes.Length);var read=InventoryAdapter.Decode(bytes);Assert.Equal(slots[95],read[95]);Assert.Equal(96,InventoryAdapter.Decode(new byte[768]).Count);Assert.Throws<InvalidDataException>(()=>InventoryAdapter.Decode(new byte[2017]));}
 [Fact]public async Task SavesAndRereadsWithoutOverwritingOtherFields(){
  const string cs="Server=127.0.0.1;Port=33307;Database=libreko_editor_test_native;User=root;Pooling=false;";var options=new DbContextOptionsBuilder<AppDbContext>().UseMySql(cs,new MySqlServerVersion(new Version(8,2,0))).Options;
  await using var db=new AppDbContext(options);await db.Database.EnsureCreatedAsync();if(!await db.Items.AnyAsync(x=>x.Num==123450001)){db.Items.Add(new ItemData{Num=123450001,Name="Test fixture",Damage=10,Weight=7});await db.SaveChangesAsync();}
  var service=new EditorDatabase(cs,Path.Combine(Path.GetTempPath(),"editor-journal-"+Guid.NewGuid()));var original=(await service.GetAsync(new(RecordKind.Item,123450001)))!;var next=(short)(original.Values["Damage"]!.GetValue<short>()==10?20:10);var result=await service.SaveAsync(new(Guid.NewGuid(),original,new(){["Damage"]=next}));Assert.Equal(SaveStatus.Saved,result.Status);var read=(await service.GetAsync(original.Key))!;Assert.Equal(next,read.Values["Damage"]!.GetValue<short>());Assert.Equal(7,read.Values["Weight"]!.GetValue<short>());var stale=await service.SaveAsync(new(Guid.NewGuid(),original,new(){["Damage"]=(short)30}));Assert.Equal(SaveStatus.Conflict,stale.Status);
 }
}
