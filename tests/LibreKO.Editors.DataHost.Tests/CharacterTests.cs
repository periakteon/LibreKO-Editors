using LibreKO.Editors.Contracts;
using LibreKO.Editors.DataHost;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
[assembly:CollectionBehavior(DisableTestParallelization=true)]
public class CharacterTests {
 [Fact]public async Task CharacterProgressAndModerationControlsPreserveOpaqueState(){
  var options=new DbContextOptionsBuilder<AppDbContext>().UseMySql(Cs,new MySqlServerVersion(new Version(8,2,0))).Options;await using var db=new AppDbContext(options);await db.Database.EnsureCreatedAsync();if(!await db.Accounts.AnyAsync(a=>a.Id==320001)){db.Accounts.Add(new Account{Id=320001,Login="character_controls_fixture",Password="fixture-only",Nation=AccountNation.Karus});await db.SaveChangesAsync();}if(!await db.Characters.AnyAsync(c=>c.Id==720001)){db.Characters.Add(new Character{Id=720001,AccountId=320001,Name="Controls fixture",Race=1,Class=101,Level=1,SkillData=[3,4],QuestData=[8,9],Items=new byte[2016]});await db.SaveChangesAsync();}
  var schema=FieldSchema.For(RecordKind.Character);Assert.True(schema.Single(f=>f.Name=="IsMuted").Writable);Assert.Equal(FieldKind.DateTime,schema.Single(f=>f.Name=="AttendanceCheckedOn").Kind);Assert.Equal("titles",schema.Single(f=>f.Name=="DisplayTitleId").Reference);
  var service=new EditorDatabase(Cs,Path.Combine(Path.GetTempPath(),"character-controls-"+Guid.NewGuid()));var before=(await service.GetAsync(new(RecordKind.Character,720001)))!;var result=await service.SaveAsync(new(Guid.NewGuid(),before,new(){["IsMuted"]=true,["NameChangeRequired"]=true,["GenieRemainingSeconds"]=3600,["RebirthLevel"]=1,["AttendanceDays"]=2,["AttendanceCheckedOn"]="2030-01-02T00:00:00Z"}));Assert.Equal(SaveStatus.Saved,result.Status);db.ChangeTracker.Clear();var row=(await db.Characters.FindAsync(720001))!;Assert.True(row.IsMuted);Assert.Equal(3600,row.GenieRemainingSeconds);Assert.Equal(new byte[]{8,9},row.QuestData);Assert.Equal(new byte[]{3,4},row.SkillData);
 }
 const string Cs="Server=127.0.0.1;Port=33307;Database=libreko_editor_test_native;User=root;Pooling=false;";
 [Fact]public async Task PlayerSavePreservesOpaqueStateAndRejectsOnlineOrInvalidRace(){
  var options=new DbContextOptionsBuilder<AppDbContext>().UseMySql(Cs,new MySqlServerVersion(new Version(8,2,0))).Options;await using var db=new AppDbContext(options);await db.Database.EnsureCreatedAsync();
  var a=await db.Accounts.SingleOrDefaultAsync(x=>x.Login=="editor_fixture");if(a==null){a=new Account{Login="editor_fixture",Password="fixture-only",Nation=AccountNation.Karus};db.Accounts.Add(a);await db.SaveChangesAsync();}
  var c=await db.Characters.FindAsync(700001);if(c==null){c=new Character{Id=700001,AccountId=a.Id,Slot=0,Name="Editor fixture",Race=1,Class=101,Face=0,Hair=0,Level=1,Hp=100,Mp=100,Items=new byte[2016],SkillData=[1,2,3],QuestData=[4,5],Experience=9007199254740993L};db.Characters.Add(c);await db.SaveChangesAsync();}
  var service=new EditorDatabase(Cs,Path.Combine(Path.GetTempPath(),"editor-character-journal-"+Guid.NewGuid()));var before=(await service.GetAsync(new(RecordKind.Character,700001)))!;var money=before.Values["Money"]!.GetValue<int>()+1;
  var saved=await service.SaveAsync(new(Guid.NewGuid(),before,new(){["Money"]=money}));Assert.Equal(SaveStatus.Saved,saved.Status);Assert.Equal(9007199254740993L,saved.Record!.Values["Experience"]!.GetValue<long>());
  db.ChangeTracker.Clear();var stored=(await db.Characters.FindAsync(700001))!;Assert.Equal(new byte[]{1,2,3},stored.SkillData);Assert.Equal(new byte[]{4,5},stored.QuestData);
  var bad=await service.SaveAsync(new(Guid.NewGuid(),saved.Record,new(){["Race"]=(byte)99}));Assert.Equal(SaveStatus.Invalid,bad.Status);
  await db.Characters.Where(x=>x.Id==700001).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsOnline,true));
  try{var online=(await service.GetAsync(new(RecordKind.Character,700001)))!;var result=await service.SaveAsync(new(Guid.NewGuid(),online,new(){["Money"]=1}));Assert.Equal(SaveStatus.Online,result.Status);}finally{await db.Characters.Where(x=>x.Id==700001).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.IsOnline,false));}
 }
 [Theory][InlineData(1,101,1,true)][InlineData(1,106,1,true)][InlineData(1,201,1,false)][InlineData(4,110,1,true)][InlineData(6,115,1,true)][InlineData(13,208,2,true)]public void ValidatesNationAndPromotedClasses(byte race,short code,byte nation,bool expected)=>Assert.Equal(expected,EditorDatabase.ValidClass(race,code,nation));
}

