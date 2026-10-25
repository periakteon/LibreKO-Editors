using System.Reflection;
using LibreKO.Editors.Contracts;
using LibreKO.Common.Domain.Entities;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Editors.Core;
namespace LibreKO.Editors.DataHost;
public static class FieldSchema {
 static readonly HashSet<string> CharacterWritable="Name NameChangeRequired Race Class Hair Face Level Experience Strength Stamina Dexterity Intelligence Magic StatPoints Money Loyalty LoyaltyMonthly LoyaltyDaily Hp Mp X Y Z MapId Bind IsMuted DisplayTitleId DeathExpLoss RebirthLevel RebStr RebSta RebDex RebIntel RebMagic GenieRemainingSeconds ClanMemo JuraidWarnings JuraidWarnedAt DrakiStage DrakiSubStage DrakiEntranceLimit DrakiEntranceLimitResetDate AttendanceDays AttendanceClaimedDays AttendanceClaimedBonus AttendanceCheckedOn GenieRewardClaimedOn SealedExperience ExpSealOn".Split(' ').ToHashSet();
 static readonly HashSet<string> AccountFields="Id Login Nation Authority KnightCash PremiumType PremiumDate VipVaultExpiry Language AccessDate OnlineServerId OnlineSince".Split(' ').ToHashSet();
 static readonly HashSet<string> AccountWritable="Authority KnightCash PremiumType PremiumDate VipVaultExpiry Language".Split(' ').ToHashSet();
 public static Type EntityType(RecordKind kind)=>kind switch{RecordKind.Item=>typeof(ItemData),RecordKind.Character=>typeof(Character),RecordKind.Account=>typeof(Account),_=>throw new ArgumentOutOfRangeException(nameof(kind))};
 public static PropertyInfo[] Properties(RecordKind kind)=>EntityType(kind).GetProperties().Where(p=>p.CanRead&&p.SetMethod?.IsPublic==true&&p.PropertyType!=typeof(byte[])&&(kind!=RecordKind.Account||AccountFields.Contains(p.Name))).ToArray();
 public static IReadOnlyList<FieldDescriptor> For(RecordKind kind)=>kind==RecordKind.Account?AccountSchema():Properties(kind).Select(p=>{
  var t=Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType;var numeric=t!=typeof(string)&&t!=typeof(bool)&&t!=typeof(DateTime);
  decimal? min=t==typeof(byte)?0:t==typeof(short)?short.MinValue:t==typeof(int)?int.MinValue:t==typeof(long)?long.MinValue:null;
  decimal? max=t==typeof(byte)?255:t==typeof(short)?short.MaxValue:t==typeof(int)?int.MaxValue:t==typeof(long)?long.MaxValue:null;
  if(kind==RecordKind.Character&&(p.Name.StartsWith("Reb")||p.Name.StartsWith("Attendance")||p.Name is "GenieRemainingSeconds" or "SealedExperience" or "DeathExpLoss" or "JuraidWarnings"))min=0;
  bool writable=kind==RecordKind.Item?p.Name!="Num":CharacterWritable.Contains(p.Name);
  var group=p.Name is "Name" or "Description" or "Num" or "Id" or "AccountId"?"Identity":p.Name.StartsWith("Req")?"Requirements":p.Name.EndsWith("R")||p.Name.EndsWith("Ac")?"Resistances":p.Name.Contains("Price")||p.Name is "Money" or "Weight"?"Economy":p.Name is "Race" or "Class" or "Hair" or "Face"?"Appearance":p.Name is "X" or "Y" or "Z" or "MapId" or "Bind"?"Position":"Properties";
  if(kind==RecordKind.Character)group=p.Name.StartsWith("Reb")?"Rebirth":p.Name.StartsWith("Attendance")?"Attendance":p.Name.StartsWith("Genie")?"Genie automation":p.Name.StartsWith("Draki")?"Draki progress":p.Name is "IsMuted" or "NameChangeRequired" or "JuraidWarnings" or "JuraidWarnedAt"?"Moderation":p.Name is "DisplayTitleId" or "ClanMemo" or "KnightsId" or "KnightsPoints" or "Fame"?"Clan & title":p.Name is "SealedExperience" or "ExpSealOn" or "DeathExpLoss"?"Experience reserve":group;
  IReadOnlyDictionary<string,string>? choices=p.Name switch{
   "Kind" when kind==RecordKind.Item=>Enum.GetValues<ItemKind>().ToDictionary(x=>((byte)x).ToString(),x=>x==ItemKind.SwordTwoHand?"Sword (two-handed)":System.Text.RegularExpressions.Regex.Replace(x.ToString(),@"(?<=[a-z])(?=[A-Z])"," ")),
   "Slot" when kind==RecordKind.Item=>CodeLabels.Slots,
   "Slot"=>Enumerable.Range(0,4).ToDictionary(x=>x.ToString(),x=>$"Character slot {x+1}"),
   "Race"=>kind==RecordKind.Character?CodeLabels.Races.Where(x=>x.Key!="0").ToDictionary():new Dictionary<string,string>(CodeLabels.Races){["7"]="Mail restricted",["20"]="Untradeable",["70"]="No trade / destroy",["71"]="No trade / destroy (alternate)",["73"]="No trade / sell / store"},
   "Class" when kind==RecordKind.Character=>new[]{101,102,103,104,105,106,107,108,109,110,111,112,113,114,115,201,202,203,204,205,206,207,208,209,210,211,212,213,214,215}.ToDictionary(x=>x.ToString(),x=>$"{(x<200?"Karus":"El Morad")} · {LibreKO.Domain.CharacterClassCatalog.SpecializationName(x)} · {LibreKO.Domain.CharacterClassCatalog.TierName(x)}"),
   "Class"=>Enumerable.Range(0,16).ToDictionary(x=>x.ToString(),x=>x==0?"All classes":$"{LibreKO.Domain.CharacterClassCatalog.DisplayName(x)} · {LibreKO.Domain.CharacterClassCatalog.TierName(x)}"),
   "Effect1"=>new Dictionary<string,string>{["0"]="None"},
   "Effect2"=>new Dictionary<string,string>{["0"]="None",["251"]="Exchange piece"},
   "Countable"=>new Dictionary<string,string>{["0"]="Single item",["1"]="Stackable",["2"]="Charge item"},
   "ItemType"=>new Dictionary<string,string>{["0"]="Normal",["4"]="Krowaz",["5"]="Standard",["11"]="Rebirth",["12"]="Rebirth Krowaz"},
   "Face"=>Enumerable.Range(0,4).ToDictionary(x=>x.ToString(),x=>$"Face style {x+1}"),
   "Bind"=>new Dictionary<string,string>{["-1"]="Default spawn / no bind point"},
   _=>null};
  string? reference=p.Name switch{"ItemPlusId" or "ItemAlteration"=>"items","Extension"=>"extensions","Effect1" or "Effect2"=>"magic","MapId"=>"zones","AccountId"=>"accounts","KnightsId"=>"clans","DisplayTitleId" or "ReqTitle"=>"titles","Bind"=>"bindpoints",_=>null};
  return new FieldDescriptor(p.Name,group,!writable?FieldKind.ReadOnly:t==typeof(string)?FieldKind.Text:t==typeof(bool)?FieldKind.Boolean:t==typeof(DateTime)?FieldKind.DateTime:t==typeof(float)||t==typeof(double)?FieldKind.Decimal:numeric?FieldKind.Integer:FieldKind.ReadOnly,writable,min,max,choices,reference);
 }).ToArray();
 static IReadOnlyList<FieldDescriptor> AccountSchema()=>Properties(RecordKind.Account).Select(p=>{
  var t=Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType;var writable=AccountWritable.Contains(p.Name);
  IReadOnlyDictionary<string,string>? choices=p.Name switch{"Authority"=>new Dictionary<string,string>{["0"]="Game master",["1"]="Normal player",["2"]="Banned"},"Nation"=>new Dictionary<string,string>{["0"]="None",["1"]="Karus",["2"]="El Morad"},"Language"=>Enum.GetValues<GameLanguage>().ToDictionary(x=>((byte)x).ToString(),x=>x.ToString()),"PremiumType"=>new Dictionary<string,string>{["0"]="No premium"},_=>null};
  return new FieldDescriptor(p.Name,p.Name is "Id" or "Login" or "Nation"?"Account identity":p.Name is "Authority" or "Language"?"Access & preferences":p.Name=="KnightCash"?"Power-up store":p.Name.StartsWith("Premium")?"Premium":p.Name=="VipVaultExpiry"?"VIP warehouse":"Session information",!writable?FieldKind.ReadOnly:t==typeof(DateTime)?FieldKind.DateTime:FieldKind.Integer,writable,t==typeof(byte)||p.Name=="KnightCash"?0:null,t==typeof(byte)?255:p.Name=="KnightCash"?int.MaxValue:null,choices,p.Name=="PremiumType"?"premiums":null);
 }).OrderBy(f=>f.Group switch{"Power-up store"=>0,"Access & preferences"=>1,"Premium"=>2,"VIP warehouse"=>3,"Account identity"=>4,_=>5}).ToArray();
}

