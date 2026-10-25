using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Globalization;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Game.World;
using Microsoft.EntityFrameworkCore;
namespace LibreKO.Editors.DataHost;
public sealed partial class EditorDatabase(string connectionString,string historyRoot) {
 public static bool FitsSlot(int storage,byte itemSlot)=>InventoryLayout.FitsSlot(storage,itemSlot);
 public static bool HandsCompatible(byte? right,byte? left)=>(right!=3||left==null)&&(left!=4||right==null);
 AppDbContext Open()=>new(new DbContextOptionsBuilder<AppDbContext>().UseMySql(connectionString,ServerVersion.AutoDetect(connectionString)).Options);
 public Task<DatabaseCompatibility> ConnectAsync()=>CheckCompatibilityAsync();
 public static RecordSnapshot Snapshot(object entity,byte? nation=null,bool accountOnline=false){
  var kind=entity is ItemData?RecordKind.Item:entity is Account?RecordKind.Account:RecordKind.Character;var values=new JsonObject();
  foreach(var p in FieldSchema.Properties(kind))values[p.Name]=p.GetValue(entity) is DateTime date?JsonValue.Create(UtcDate(date).ToString("O",CultureInfo.InvariantCulture)):JsonSerializer.SerializeToNode(p.GetValue(entity),p.PropertyType);
  var id=entity is ItemData item?item.Num:((Entity)entity).Id;IReadOnlyList<InventorySlotDto>? inventory=null;bool online=entity is Account a&&(a.OnlineServerId!=null||accountOnline);var versionValues=(JsonObject)values.DeepClone();
  if(entity is Character c){online=c.IsOnline;try{inventory=InventoryAdapter.Decode(c.Items);}catch(InvalidDataException){}versionValues["_itemsHash"]=Convert.ToHexString(SHA256.HashData(c.Items));}
  return new(new(kind,id),values,RecordFingerprint.Compute(versionValues),nation,online,inventory);
 }
 public async Task<RecordSnapshot?> GetAsync(RecordKey key){await using var db=Open();return await Get(db,key);}
 static async Task<RecordSnapshot?> Get(AppDbContext db,RecordKey key){if(key.Kind==RecordKind.Item){var i=await db.Items.AsNoTracking().SingleOrDefaultAsync(i=>i.Num==key.Id);return i==null?null:Snapshot(i);}if(key.Kind==RecordKind.Account){var a=await db.Accounts.AsNoTracking().SingleOrDefaultAsync(a=>a.Id==key.Id);return a==null?null:Snapshot(a,accountOnline:await db.Characters.AnyAsync(c=>c.AccountId==a.Id&&c.IsOnline));}var c=await db.Characters.AsNoTracking().SingleOrDefaultAsync(c=>c.Id==key.Id);if(c==null)return null;var nation=await db.Accounts.Where(a=>a.Id==c.AccountId).Select(a=>a.Nation).SingleAsync();return Snapshot(c,(byte)nation);}
 public async Task<Page<RecordSummary>> ListAsync(RecordKind kind,RecordQuery q){
  await using var db=Open();var size=Math.Clamp(q.Size,1,100);var page=Math.Clamp(q.Page,0,1000000);
  if(kind==RecordKind.Account){var accounts=db.Accounts.AsNoTracking();if(!string.IsNullOrWhiteSpace(q.Text)){var text=q.Text.Trim();accounts=int.TryParse(text,out var accountId)?accounts.Where(a=>a.Id==accountId):accounts.Where(a=>a.Login.Contains(text));}var totalAccounts=await accounts.CountAsync();var found=await accounts.OrderBy(a=>a.Id).Skip(page*size).Take(size).Select(a=>new{a.Id,a.Login,a.Authority,a.KnightCash}).ToArrayAsync();return new(found.Select(a=>new RecordSummary(new(kind,a.Id),a.Login,new(){["Authority"]=(int)a.Authority,["KnightCash"]=a.KnightCash})).ToArray(),totalAccounts,page,size);}
  if(kind==RecordKind.Item){var rows=db.Items.AsNoTracking().AsQueryable();if(!string.IsNullOrWhiteSpace(q.Text)){var term=q.Text.Trim();rows=int.TryParse(term,out var id)?rows.Where(x=>x.Num==id):rows.Where(x=>x.Name.Contains(term));}
   if(q.Filters!=null)foreach(var f in q.Filters){if(f.Value==null||f.Value.ToString()=="")continue;var n=f.Value.GetValue<int>();rows=f.Key switch{"Kind"=>rows.Where(x=>x.Kind==n),"Slot"=>rows.Where(x=>x.Slot==n),"Class"=>rows.Where(x=>x.Class==n),"Race"=>rows.Where(x=>x.Race==n),"ReqLevel"=>rows.Where(x=>x.ReqLevel==n),_=>rows};}
   var total=await rows.CountAsync();var list=await rows.OrderBy(x=>x.Num).Skip(page*size).Take(size).Select(x=>new{x.Num,x.Name,x.Kind,x.Slot}).ToListAsync();return new(list.Select(x=>new RecordSummary(new(kind,x.Num),x.Name,new(){["Kind"]=x.Kind,["Slot"]=x.Slot})).ToArray(),total,page,size);
  }else{var rows=from c in db.Characters.AsNoTracking() join a in db.Accounts on c.AccountId equals a.Id select new{Character=c,Login=a.Login};if(q.Filters?["AccountId"] is {} account)rows=rows.Where(x=>x.Character.AccountId==account.GetValue<int>());if(!string.IsNullOrWhiteSpace(q.Text)){var t=q.Text.Trim();rows=int.TryParse(t,out var id)?rows.Where(x=>x.Character.Id==id||x.Character.AccountId==id):rows.Where(x=>x.Character.Name.Contains(t)||x.Login.Contains(t));}var total=await rows.CountAsync();var list=await rows.OrderBy(x=>x.Character.Id).Skip(page*size).Take(size).Select(x=>new{x.Character.Id,x.Character.Name,x.Character.Level,x.Character.IsOnline}).ToListAsync();return new(list.Select(x=>new RecordSummary(new(kind,x.Id),x.Name,new(){["Level"]=x.Level,["Online"]=x.IsOnline})).ToArray(),total,page,size);}
 }
 static object? ConvertValue(JsonNode? value,Type type)=>value==null?null:JsonSerializer.Deserialize(value.ToJsonString(),type);
 static DateTime UtcDate(DateTime date){var utc=date.Kind==DateTimeKind.Local?date.ToUniversalTime():DateTime.SpecifyKind(date,DateTimeKind.Utc);return new DateTime(utc.Ticks-utc.Ticks%10,DateTimeKind.Utc);}
 static RecordChange NormalizeDates(RecordChange change){var values=(JsonObject)change.ChangedValues.DeepClone();foreach(var pair in change.ChangedValues){var property=FieldSchema.EntityType(change.Before.Key.Kind).GetProperty(pair.Key);if(pair.Value!=null&&(Nullable.GetUnderlyingType(property!.PropertyType)??property.PropertyType)==typeof(DateTime))values[pair.Key]=UtcDate((DateTime)ConvertValue(pair.Value,property.PropertyType)!).ToString("O",CultureInfo.InvariantCulture);}return change with{ChangedValues=values};}
 static List<ValidationIssue> Validate(RecordChange change){
  var issues=new List<ValidationIssue>();var schema=FieldSchema.For(change.Before.Key.Kind).ToDictionary(f=>f.Name);
  if(change.Inventory!=null&&change.Before.Key.Kind!=RecordKind.Character)issues.Add(new("Inventory","Only characters have an inventory."));
  foreach(var p in change.ChangedValues){if(!schema.TryGetValue(p.Key,out var field)||!field.Writable&&!(change.Create&&p.Key=="Num")){issues.Add(new(p.Key,"This field is read-only."));continue;}try{var prop=FieldSchema.EntityType(change.Before.Key.Kind).GetProperty(p.Key)!;var v=ConvertValue(p.Value,prop.PropertyType);if(v==null&&Nullable.GetUnderlyingType(prop.PropertyType)==null)throw new FormatException();if(v is float f&&!float.IsFinite(f))throw new FormatException();if(v is double d&&!double.IsFinite(d))throw new FormatException();if(v is DateTime date&&date.Year<1000)throw new FormatException();if(v!=null&&v is not DateTime&&(field.Minimum!=null||field.Maximum!=null)){var number=Convert.ToDecimal(v);if(field.Minimum is {} min&&number<min||field.Maximum is {} max&&number>max)throw new FormatException();}}catch{issues.Add(new(p.Key,"Invalid value or numeric range."));}}
  return issues;
 }
 public async Task<SaveResult> SaveAsync(RecordChange change){
  var issues=Validate(change);if(issues.Count>0)return new(SaveStatus.Invalid,Issues:issues);var support=(await CheckCompatibilityAsync()).Modules.Single(m=>m.Kind==change.Before.Key.Kind);if(!support.CanSave)return new(SaveStatus.Invalid,Message:"Database compatibility prevents saving: "+support.Reason);change=NormalizeDates(change);var journal=new OperationJournal(historyRoot);journal.Prepare(change);
  bool retainJournal=false;try{
  await using var db=Open();await using var tx=await db.Database.BeginTransactionAsync();object? entity;RecordSnapshot? original;
  if(change.Before.Key.Kind==RecordKind.Item){var rows=await db.Items.FromSqlInterpolated($"SELECT * FROM Items WHERE Num = {change.Before.Key.Id} FOR UPDATE").ToListAsync();entity=rows.SingleOrDefault();original=entity==null?null:Snapshot(entity);if(change.Create){if(entity!=null)return new(SaveStatus.Conflict,Message:"Item ID already exists.");entity=new ItemData{Num=change.Before.Key.Id};db.Items.Add((ItemData)entity);}}
  else if(change.Before.Key.Kind==RecordKind.Account){if(change.Create)return new(SaveStatus.Invalid,Message:"Account creation is not supported here.");var rows=await db.Accounts.FromSqlInterpolated($"SELECT * FROM Accounts WHERE Id = {change.Before.Key.Id} FOR UPDATE").ToListAsync();entity=rows.SingleOrDefault();original=entity==null?null:Snapshot(entity,accountOnline:await db.Characters.AnyAsync(c=>c.AccountId==change.Before.Key.Id&&c.IsOnline));}
  else{if(change.Create)return new(SaveStatus.Invalid,Message:"Player creation is not supported here.");var rows=await db.Characters.FromSqlInterpolated($"SELECT * FROM Characters WHERE Id = {change.Before.Key.Id} FOR UPDATE").ToListAsync();entity=rows.SingleOrDefault();original=entity==null?null:Snapshot(entity,(byte)await db.Accounts.Where(a=>a.Id==((Character)entity).AccountId).Select(a=>a.Nation).SingleAsync());}
  if(entity==null)return new(SaveStatus.Conflict,Message:"Record no longer exists.");
  if(original?.IsOnline==true)return new(SaveStatus.Online,Message:"Log out all affected sessions and use an offline maintenance window.");
  if(!change.Create&&original!.Version!=change.Before.Version)return new(SaveStatus.Conflict,Record:original,Message:"The record changed after you opened it. Reload before saving.");
  foreach(var p in change.ChangedValues)FieldSchema.EntityType(change.Before.Key.Kind).GetProperty(p.Key)!.SetValue(entity,ConvertValue(p.Value,FieldSchema.EntityType(change.Before.Key.Kind).GetProperty(p.Key)!.PropertyType));
  if(entity is ItemData item){if(item.Num<=0||string.IsNullOrWhiteSpace(item.Name))return new(SaveStatus.Invalid,Message:"An item requires a positive ID and name.");if(item.Num!=change.Before.Key.Id)return new(SaveStatus.Invalid,Message:"Item identity cannot change.");}
  if(entity is Account account){if(account.KnightCash<0||!Enum.IsDefined(account.Authority)||!Enum.IsDefined(account.Language))return new(SaveStatus.Invalid,Message:"Knight Cash must be nonnegative; choose a valid authority and language.");if(change.ChangedValues.ContainsKey("PremiumType")&&account.PremiumType!=0&&!await db.PremiumItems.AnyAsync(p=>p.Type==account.PremiumType))return new(SaveStatus.Invalid,Message:"Choose an existing premium type.");}
  if(entity is Character c){
   if(string.IsNullOrWhiteSpace(c.Name)||c.Name.Length>50)return new(SaveStatus.Invalid,Message:"Name must contain 1–50 characters.");
   if(!CharacterRaceNations.BelongsTo(c.Race,(AccountNation)original!.Nation!.Value)||!ValidClass(c.Race,c.Class,(byte)original.Nation.Value)||!CharacterLookRules.Allows(c.Race,c.Face,c.Hair,change.Before.Values["Hair"]!.GetValue<int>()))return new(SaveStatus.Invalid,Message:"Race, class, face, or hair is incompatible with the character's nation.");
   if(c.Level==0||c.Money<0||c.Experience<0)return new(SaveStatus.Invalid,Message:"Level must be positive; money and experience cannot be negative.");
   if(c.ClanMemo.Length>Character.ClanMemoMaxLength)return new(SaveStatus.Invalid,Message:"Clan memo must contain at most 20 characters.");
   if(await db.Characters.AnyAsync(x=>x.Id!=c.Id&&x.Name==c.Name))return new(SaveStatus.Invalid,Message:"Character name is already in use.");
   if(change.Inventory!=null){
    if(original.Inventory==null)return new(SaveStatus.Invalid,Message:"Unknown original inventory layout.");
    var ids=change.Inventory.Where(x=>x.ItemId>0).Select(x=>x.ItemId).Distinct().ToArray();
    var found=await db.Items.Where(x=>ids.Contains(x.Num)).Select(x=>new{x.Num,x.Slot}).ToDictionaryAsync(x=>x.Num,x=>x.Slot);
    try{InventoryLayout.Validate(change.Inventory,id=>found.TryGetValue(id,out var code)?code:null);}
    catch(InvalidOperationException e){return new(SaveStatus.Invalid,Message:e.Message);}
    c.Items=InventoryAdapter.Encode(change.Inventory);
   }
  }
  bool committing=false;try{await db.SaveChangesAsync();committing=true;await tx.CommitAsync();}catch(DbUpdateException){return new(SaveStatus.Invalid,Message:"Database constraints rejected this change.");}catch when(committing){retainJournal=true;return new(SaveStatus.UnknownCommit,Message:"Commit response was interrupted. Reconcile the pending operation before retrying.");}
  retainJournal=true;var result=(await GetAsync(change.Before.Key))!;try{journal.Commit(change,result);}catch(Exception e) when(e is IOException or UnauthorizedAccessException){return new(SaveStatus.Saved,result,Message:"Saved to database. Local history needs recovery; the pending journal is retained.");}return new(SaveStatus.Saved,result,Message:change.Before.Key.Kind==RecordKind.Item?"Saved. Reload/restart the game server to refresh cached item data.":"Saved to database. Changes apply on the next login.");
  }finally{if(!retainJournal)journal.Abort(change.OperationId);}
 }
 public static bool ValidClass(byte race,short code,byte nation){if(code/100!=nation)return false;int family=LibreKO.Domain.CharacterClassCatalog.Family(code);return race switch{1 or 11=>family==1,2=>family is 2 or 4,3=>family==3,4=>family is 3 or 4,6 or 14=>family==5,12 or 13=>family is >=1 and <=4,_=>false};}
}
