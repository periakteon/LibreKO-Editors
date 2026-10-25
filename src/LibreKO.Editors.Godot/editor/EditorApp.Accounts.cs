using Godot;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
namespace LibreKO.Editors.Desktop;
public partial class EditorApp {
 Control? previewControls,characterAccountRow;VBoxContainer? accountOverview;Button? openAccountButton;Label? characterAccountSummary;
 Label? cashSummary,authoritySummary,premiumSummary,vipSummary;
 static string Route(RecordKind kind)=>kind switch{RecordKind.Item=>"items",RecordKind.Character=>"characters",RecordKind.Account=>"accounts",_=>throw new InvalidOperationException("Unknown record type.")};
 RecordKind ActiveKind=>Module switch{0=>RecordKind.Item,1=>RecordKind.Character,2=>RecordKind.Account,_=>throw new InvalidOperationException("Unknown module.")};
 internal void SwitchModule(int module,int? recordId=null)=>ConfirmLeave(()=>{Session.Shell.RememberContext();ClearSelection();Module=module;PageNumber=0;search.Text="";Run(async()=>{await LoadList();if(recordId is {} id)await SelectRecord(id);else if(ids.Count>0)await SelectRecord(ids[0]);});});
 async Task LoadCharacterAccount(RecordDraft draft){if(characterAccountSummary==null)return;characterAccountSummary.Text="Loading account…";var a=await Client.SendAsync<RecordSnapshot>("accounts.get",new{id=FieldValueParser.ReadInt32(draft.Values["AccountId"])});if(Draft!=draft||!IsInstanceValid(characterAccountSummary))return;var cash=FieldValueParser.ReadInt32(a.Values["KnightCash"]);var authority=a.Values["Authority"]?.ToString();var name=authority switch{"0"=>"Game master","1"=>"Normal player","2"=>"Banned",_=>"Unknown role"};characterAccountSummary.Text=$"{a.Values["Login"]} · {cash:N0} KC · {name}";characterAccountSummary.TooltipText="Account "+a.Key.Id+" · shared cash and access · use Open linked account to edit";}
 async Task BuildAccountOverview(RecordDraft draft){
  if(accountOverview==null)return;Ui.Clear(accountOverview);cashSummary=authoritySummary=premiumSummary=vipSummary=null;
  var hero=Ui.Add(accountOverview,new PanelContainer());var heading=Ui.Column(hero);heading.AddThemeConstantOverride("separation",12);Ui.Label(heading,"POWER-UP STORE BALANCE",13,Ui.Muted);cashSummary=Ui.Label(heading,"",36,Ui.Accent);Ui.Label(heading,"Knight Cash · shared by every character on this account",13,Ui.Muted);
  var status=Ui.Group(accountOverview,"Account access");authoritySummary=Ui.Label(status,"",20);Ui.Label(status,draft.Original.IsOnline?"Account has an active session · database saving is disabled":"Account is offline · changes apply on next login",13,Ui.Muted);
  var services=Ui.Group(accountOverview,"Premium & VIP");premiumSummary=Ui.Label(services,"",14);vipSummary=Ui.Label(services,"",14);RefreshAccountSummary();
  var linked=Ui.Group(accountOverview,"Linked player characters");Ui.Label(linked,"Open a character to manage its equipment, items and gameplay fields.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
  var page=await Client.SendAsync<Page<RecordSummary>>("characters.list",new RecordQuery(Size:100,Filters:new(){["AccountId"]=draft.Original.Key.Id}));if(Draft!=draft||!IsInstanceValid(linked))return;
  foreach(var row in page.Rows){var id=row.Key.Id;var button=Ui.Button(linked,$"{id} · {row.Label} · level {row.Details["Level"]}",()=>SwitchModule(1,id));button.Alignment=HorizontalAlignment.Left;}
  if(page.Total==0)Ui.Label(linked,"No player characters on this account.",13,Ui.Muted);
 }
 void RefreshAccountSummary(){if(Module!=2||Draft==null||cashSummary==null||!IsInstanceValid(cashSummary))return;var v=Draft.Values;cashSummary.Text=long.TryParse(v["KnightCash"]?.ToString(),out var cash)?cash.ToString("N0")+" KC":"—";authoritySummary!.Text=CodeLabels.Describe(new Dictionary<string,string>{["0"]="Game master",["1"]="Normal player",["2"]="Banned"},v["Authority"]?.ToString()??"");premiumSummary!.Text="Premium type "+v["PremiumType"]+" · expires "+DateText(v["PremiumDate"]);vipSummary!.Text="VIP warehouse · expires "+DateText(v["VipVaultExpiry"]);}
 static string DateText(JsonNode? value)=>value==null?"Not set":DateTimeOffset.TryParse(value.ToString(),System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal|System.Globalization.DateTimeStyles.AdjustToUniversal,out var date)&&date.Year>1?date.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss")+" UTC":"Inactive";
 void AccountField(Node parent,FieldDescriptor field,RecordDraft draft){
  var row=Ui.Row(parent);Ui.Label(row,Ui.FieldName(field.Name),12,Ui.Muted).CustomMinimumSize=new(135,0);
  var pick=Ui.Add(row,new OptionButton{Name="Account"+field.Name,SizeFlagsHorizontal=SizeFlags.ExpandFill,FitToLongestItem=false});var entries=field.Choices!.ToArray();var current=draft.Values[field.Name]?.ToString()??"";
  foreach(var entry in entries)pick.AddItem(entry.Key+" · "+entry.Value,int.Parse(entry.Key));var index=Array.FindIndex(entries,e=>e.Key==current);if(index<0){pick.AddItem(current+" · Unknown",int.TryParse(current,out var unknown)?unknown:-1);index=pick.ItemCount-1;}pick.Select(index);
  pick.ItemSelected+=i=>{if(Draft!=draft||!Operations.CanMutate)return;draft.Set(field.Name,pick.GetItemId((int)i));RefreshAccountSummary();UpdateChrome();};fields[field.Name]=pick;
 }
}
