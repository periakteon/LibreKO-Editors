using Godot;
using System.Globalization;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using GameItems=LibreKO.Domain.ItemData;
using GameUi=LibreKO.UiTheme;
namespace LibreKO.Editors.Desktop;

// View treatment ported from ItemTooltipFeature.TipView in the LibreKO client.
// Statistics use the resolved database row, which already includes extension
// bonuses; adding the client's extension bonuses again would double them.
public partial class InventoryTooltip:PanelContainer {
 static readonly Color Plain=new("ece4d2"),Bonus=new("7fff00"),Muted=new("a8a298"),IdColour=new("ffff00");
 readonly VBoxContainer body;
 readonly JsonObject? values;
 long Number(string key)=>long.TryParse(values?[key]?.ToString(),NumberStyles.Integer,CultureInfo.InvariantCulture,out var value)?value:0;

 public InventoryTooltip(InventorySlotDto slot,ReferenceEntry? entry){
  values=entry?.Details;MouseFilter=MouseFilterEnum.Ignore;CustomMinimumSize=new(250,0);
  var bg=new StyleBoxFlat{BgColor=new(0.031f,0.027f,0.024f,0.97f),BorderColor=new(0.40f,0.33f,0.19f,0.92f),ShadowColor=new(0,0,0,0.60f),ShadowSize=8};bg.SetBorderWidthAll(1);bg.SetCornerRadiusAll(2);AddThemeStyleboxOverride("panel",bg);
  var margin=Ui.Add(this,new MarginContainer{MouseFilter=MouseFilterEnum.Ignore});GameUi.Margins(margin,10,8,10,9);body=Ui.Add(margin,new VBoxContainer{MouseFilter=MouseFilterEnum.Ignore});body.AddThemeConstantOverride("separation",1);
  if(slot.ItemId==0){Line(InventoryLayout.Name(slot.Index),GameUi.GoldBright);Line($"Slot {slot.Index} · Empty",Muted);Line("Drop a compatible item here",Plain);return;}
  var head=Ui.Add(body,new HBoxContainer{MouseFilter=MouseFilterEnum.Ignore});head.AddThemeConstantOverride("separation",6);
  var icon=Ui.Add(head,new TextureRect{Texture=LibreKO.ItemIcons.Get(slot.ItemId),CustomMinimumSize=new(42,42),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=MouseFilterEnum.Ignore});LibreKO.UpgradeBadge.Attach(icon).Set(slot.ItemId);
  var name=Text(entry?.Label??$"Unknown item {slot.ItemId}",15,LibreKO.ItemGrade.Tint(slot.ItemId));name.AutowrapMode=TextServer.AutowrapMode.WordSmart;name.CustomMinimumSize=new(180,0);name.SizeFlagsHorizontal=SizeFlags.ExpandFill;head.AddChild(name);
  Line("Item ID: "+slot.ItemId,IdColour,11);
  var rarity=GameItems.ExtFor(slot.ItemId)?.MagicOrRare??-1;
  Line(RarityName(rarity),new Color("ffad4d"),12,HorizontalAlignment.Right);
  Line(KindName((int)Number("Kind")),Plain,12,HorizontalAlignment.Right);
  if(values==null)Line("Database item details unavailable",Muted,11);
  var max=Number("Duration");
  if(max>0){var percent=(int)Math.Clamp((long)slot.Durability*100/max,0,100);var row=Ui.Row(body);row.MouseFilter=MouseFilterEnum.Ignore;var label=Text($"Durability: {slot.Durability} / {max} ({percent}%)",11,Plain);label.SizeFlagsHorizontal=SizeFlags.ExpandFill;row.AddChild(label);var gauge=Ui.Add(row,new ProgressBar{MinValue=0,MaxValue=100,Value=percent,ShowPercentage=false,CustomMinimumSize=new(45,7),SizeFlagsVertical=SizeFlags.ShrinkCenter,MouseFilter=MouseFilterEnum.Ignore});gauge.AddThemeStyleboxOverride("background",new StyleBoxFlat{BgColor=new(0.94f,0.98f,1,0.27f)});gauge.AddThemeStyleboxOverride("fill",new StyleBoxFlat{BgColor=new("ff8a00")});}
  else if(slot.Durability!=0)Line("Durability: "+slot.Durability,Plain,11);
  Rule();
  if(Number("SellPrice")>0)Line("Selling Price: "+Number("SellPrice").ToString("N0"));
  Stat("Damage","Attack Power");Stat("Ac","Defense Ability");
  if(Number("Delay")>0)Line("Attack Speed: "+Speed(Number("Delay")));
  if(Number("Range")>0)Line($"Effective Range: {Number("Range")/10m:0.00}");
  foreach(var (field,label) in new[]{("Hitrate","Hit Rate"),("Evasionrate","Dodging Rate"),("StrB","Strength Bonus"),("StaB","Health Bonus"),("DexB","Dexterity Bonus"),("IntelB","Intelligence Bonus"),("ChaB","Magic Power Bonus"),("MaxHpB","HP Bonus"),("MaxMpB","MP Bonus"),("FireDamage","Flame Damage"),("IceDamage","Glacier Damage"),("LightningDamage","Lightning Damage"),("PoisonDamage","Poison Damage"),("HpDrain","HP Drain"),("MpDrain","MP Drain"),("FireR","Resistance to Flame"),("ColdR","Resistance to Glacier"),("LightningR","Resistance to Lightning"),("MagicR","Resistance to Magic"),("PoisonR","Resistance to Poison"),("CurseR","Resistance to Curse"),("DaggerAc","Defense Ability (Dagger)"),("SwordAc","Defense Ability (Sword)"),("AxeAc","Defense Ability (Axe)"),("MaceAc","Defense Ability (Club)"),("SpearAc","Defense Ability (Spear)"),("BowAc","Defense Ability (Arrow)")})Stat(field,label,Bonus);
  if(Number("Weight")>0)Line($"Weight: {Number("Weight")/10m:0.00}");
  foreach(var (field,label) in new[]{("ReqLevel","Required Level"),("ReqStr","Required Strength"),("ReqSta","Required Health"),("ReqDex","Required Dexterity"),("ReqIntel","Required Intelligence"),("ReqCha","Required Magic Power")})Stat(field,label);
  if(Number("Class")!=0)Line("Required Class: "+LibreKO.Domain.CharacterClassCatalog.DisplayName((int)Number("Class")));
  Rule();Line("Item Grade: "+RarityName(rarity),new Color("ffad4d"),12,HorizontalAlignment.Center);
  var description=values?["Description"]?.ToString();if(!string.IsNullOrWhiteSpace(description)){Rule();var lines=LibreKO.Domain.ItemText.DescriptionLines(description).ToArray();for(var i=0;i<lines.Length;i++){var line=System.Net.WebUtility.HtmlDecode(lines[i]);if(i==0)line="*"+line;if(i==lines.Length-1)line+="*";var text=Line(line,Plain,11,HorizontalAlignment.Center);text.AutowrapMode=TextServer.AutowrapMode.WordSmart;text.CustomMinimumSize=new(230,0);}}
  Rule();Line($"{InventoryLayout.Name(slot.Index)} · slot {slot.Index}",Muted,11);Line($"Count: {slot.Count} · Flag: {slot.Flag} ({FlagName(slot.Flag)})",Muted,11);Line("Unique ID: "+slot.UniqueId,Muted,11);if(slot.ExpiresAt!=0)Line("Expires (Unix): "+slot.ExpiresAt,Muted,11);
 }
 Label Line(string text,Color? tint=null,int size=12,HorizontalAlignment alignment=HorizontalAlignment.Left){var label=Text(text,size,tint??Plain,alignment);body.AddChild(label);return label;}
 static Label Text(string text,int size,Color tint,HorizontalAlignment alignment=HorizontalAlignment.Left){var label=GameUi.Text(text,size,tint,alignment);label.AddThemeColorOverride("font_color",tint);label.MouseFilter=MouseFilterEnum.Ignore;return label;}
 void Stat(string field,string label,Color? tint=null){var value=Number(field);if(value!=0)Line(label+": "+value,tint);}
 void Rule()=>body.AddChild(new TextureRect{Texture=GameUi.Divider(),CustomMinimumSize=new(0,3),StretchMode=TextureRect.StretchModeEnum.Scale,MouseFilter=MouseFilterEnum.Ignore});
 static string FlagName(byte flag)=>Enum.IsDefined((LibreKO.Domain.ItemFlag)flag)?((LibreKO.Domain.ItemFlag)flag).ToString():"Unknown";
 static string Speed(long delay)=>delay<=89?"Very Fast":delay<=110?"Fast":delay<=130?"Normal":delay<=150?"Slow":"Very Slow";
 static string RarityName(int rarity)=>rarity switch{GameItems.Rarity.Magic=>"Magic item",GameItems.Rarity.Rare=>"Rare item",GameItems.Rarity.Craft=>"Craft item",GameItems.Rarity.Unique=>"Unique item",GameItems.Rarity.Upgrade=>"Upgrade item",GameItems.Rarity.Event=>"Event item",GameItems.Rarity.Cospre=>"Costume item",GameItems.Rarity.Reverse=>"Reverse item",GameItems.Rarity.ReverseUnique=>"Reverse unique item",_=>"Regular item"};
 static string KindName(int kind)=>kind switch{11=>"Dagger",21=>"One-handed Sword",22=>"Two-handed Sword",31=>"One-handed Axe",32=>"Two-handed Axe",41=>"One-handed Club",42=>"Two-handed Club",51=>"Spear",52=>"Two-handed Spear",60=>"Shield",70=>"Bow",71=>"Crossbow",80=>"Long Bow",91=>"Earring",92=>"Necklace",93=>"Ring",94=>"Belt",110=>"Staff",140=>"Jamadhar",181=>"Mace",210=>"Warrior Armour",220=>"Rogue Armour",230=>"Mage Armour",240=>"Priest Armour",252=>"Costume",_=>"Item"};
}
