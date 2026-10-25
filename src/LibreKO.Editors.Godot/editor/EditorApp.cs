using Godot;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using GFile=Godot.FileAccess;
namespace LibreKO.Editors.Desktop;
public partial class EditorApp:Control {
 internal PreviewViewport Preview=null!;internal RecordDraft? Draft;internal RecordSnapshot? Record;internal List<InventorySlotDto>? Inventory;internal int Module;internal int SelectedId;internal JsonObject Items=new(),Bodies=new(),Faces=new(),Weapons=new(),Armor=new();
 internal ItemList List=null!;readonly List<int> ids=new();internal VBoxContainer Properties=null!,InventoryColumn=null!;internal TabContainer Inspector=null!;internal Label Status=null!,TitleLabel=null!,Info=null!;internal TextureRect Icon=null!;internal LineEdit search=null!;internal OptionButton Race=null!,Animations=null!;internal int PageNumber;bool ready;readonly Dictionary<string,Control> fields=new();string profilePath="";JsonObject filters=new();
 void BuildError(string message){var v=Ui.Column(this);v.Position=new(20,30);Ui.Label(v,"LibreKO Editors — configuration required",24);Status=Ui.Label(v,message);Ui.Label(v,"Select the installed packed build in Settings.");Ui.Button(v,"Open settings",ShowSettings);}
 internal async void Run(Func<Task> action){if(!closing)await Session.RunAsync(action);}
 internal async Task Connect(){await Session.ConnectAsync(Session.ConnectionString);await RefreshAsync();}
 internal async Task LoadList(){ids.Clear();List.Clear();var text=search.Text.Trim();if(Module!=0&&!Connected){Status.Text="Connect to browse player and account records.";Ui.Clear(Properties);return;}
  if(Connected&&!ModuleSupport(ActiveKind).CanBrowse){Status.Text=ModuleSupport(ActiveKind).Reason;Ui.Clear(Properties);Ui.Label(Properties,"This database structure is not supported for this section. Open Compatibility for details.",13,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;return;}
  if(Connected){var page=await Client.SendAsync<Page<RecordSummary>>(Route(ActiveKind)+".list",new RecordQuery(text,PageNumber,50,Module==0?filters:null));foreach(var row in page.Rows){ids.Add(row.Key.Id);List.AddItem(row.Key.Id+"   "+row.Label,Module==0?LibreKO.ItemIcons.Get(row.Key.Id):null);}Status.Text=$"{page.Total:N0} records · page {PageNumber+1}";if(pageCaption!=null)pageCaption.Text=Status.Text;}
  else{var rows=Items.Where(x=>int.TryParse(x.Key,out _)).Select(x=>(Id:int.Parse(x.Key),Name:x.Value?["name"]?.ToString()??""));var filtered=rows.Where(x=>text==""||x.Id.ToString().Contains(text)||x.Name.Contains(text,StringComparison.OrdinalIgnoreCase)).OrderBy(x=>x.Id).ToArray();foreach(var row in filtered.Skip(PageNumber*50).Take(50)){ids.Add(row.Id);List.AddItem(row.Id+"   "+row.Name,Module==0?LibreKO.ItemIcons.Get(row.Id):null);}Status.Text=$"{filtered.Length:N0} local definitions · database disconnected";if(pageCaption!=null)pageCaption.Text=$"{filtered.Length:N0} client definitions · page {PageNumber+1}";}
 }
 internal async Task SelectRecord(int id){Session.Shell.RememberContext();SelectedId=id;Icon.Visible=Module==0;if(Module!=0)Icon.Texture=null;var selectedIndex=ids.IndexOf(id);if(selectedIndex>=0)List.Select(selectedIndex);else List.DeselectAll();Race.Disabled=Module!=0;Draft=null;Record=null;Inventory=null;
  if(Connected&&!ModuleSupport(ActiveKind).CanBrowse){SelectedId=0;Status.Text=ModuleSupport(ActiveKind).Reason;return;}
  if(Connected){Record=await Client.SendAsync<RecordSnapshot>(Route(ActiveKind)+".get",new{id});if(Record?.Values==null)throw new InvalidOperationException("Record no longer exists.");Draft=new(Record);Inventory=Record.Inventory?.ToList();}
  TitleLabel.Text=Record?.Values["Name"]?.ToString()??Record?.Values["Login"]?.ToString()??Items[id.ToString()]?["name"]?.ToString()??"Item "+id;Info.Text=Module==2?"Account "+id+" · "+(Record!.IsOnline?"Active session · saving disabled":"Offline account · shared access, cash and services"):Module==1?("Nation "+Record!.Nation+" · "+(Record.IsOnline?"Online · saving disabled":"Offline player · appearance and equipment")):"ID "+id+" · drag to rotate · scroll to zoom";BuildProperties();RefreshPreview();UpdateChrome();RefreshOrigins();Status.Text=Connected?"Record loaded.":"Local visual catalogue · connect for gameplay values.";Session.Shell.RecordLocation();
 }
 internal void RefreshPreview(){if(Module==2){Preview.ShowModel(null);return;}try{
  if(!ContentReady){Preview.ShowModel(null);Info.Text="Client preview unavailable · see Compatibility";return;}
  Node3D? model=null;int race=Race.GetSelectedId();if(Module==1&&Draft!=null){var v=Draft.Values;race=FieldValueParser.ReadInt32(v["Race"]);int[] gear=new int[17];var mapping=LibreKO.Domain.InventoryConstants.VisualSlots;for(int i=0;i<17;i++)gear[i]=Inventory?.FirstOrDefault(s=>s.Index==mapping[i])?.ItemId??0;model=PreviewViewport.Character(race,FieldValueParser.ReadInt32(v["Face"]),FieldValueParser.ReadInt32(v["Hair"]),gear);}
  else if(SelectedId>0){model=PreviewEffects.Weapon(SelectedId);if(model==null){var item=LibreKO.Domain.ItemData.Get(SelectedId);var slot=item?.Slot??-1;int visual=slot switch{5=>0,6=>1,7=>2,8=>3,9=>4,_=>-1};if(visual>=0){var gear=new int[17];gear[visual]=SelectedId;model=PreviewViewport.Character(race,0,0,gear);}}Icon.Texture=LibreKO.ItemIcons.Get(SelectedId);}

  Preview.ShowModel(model);Animations.Clear();if(Preview.Animation is {} animation)foreach(var name in animation.GetAnimationList())if(name!="RESET")Animations.AddItem(name);Animations.Disabled=Animations.ItemCount==0;if(Animations.ItemCount==0)Animations.AddItem("No animation clips");if(!Animations.Disabled){var index=Enumerable.Range(0,Animations.ItemCount).FirstOrDefault(i=>Animations.GetItemText(i) is "basic" or "breath" or "idle");Animations.Select(index);Preview.Play(Animations.GetItemText(index));}if(model==null)Info.Text="This item has no 3D resource; its icon is shown.";
 }catch(Exception e){Status.Text="Preview: "+e.Message;}}
 internal Node3D? LoadModel(string path)=>ResourceLoader.Load<PackedScene>("res://"+path)?.Instantiate<Node3D>();
 internal async Task SaveRecord(){
  if(Draft==null||!Connected)return;if(!ModuleSupport(ActiveKind).CanSave)throw new InvalidOperationException("Compatibility prevents saving this section. Review the report.");if(invalidFields.Count>0)throw new InvalidOperationException("Correct invalid fields before saving.");
  var change=Draft.BuildChange(Guid.NewGuid());if(change.ChangedValues.Count==0&&change.Inventory==null){Status.Text="No database changes.";return;}
  var result=await Client.SendAsync<SaveResult>(Route(ActiveKind)+".save",change);Status.Text=result.Message??result.Status.ToString();
  if(result.Status==SaveStatus.Saved&&result.Record!=null){Session.NotifyDatabaseChange();Record=result.Record;Draft=new(Record);Inventory=Draft.Inventory?.ToList();BuildProperties();RefreshPreview();ShowRecoveryWarning();}
  else if(result.Issues!=null)Status.Text=string.Join("; ",result.Issues.Select(x=>x.Field+": "+x.Message));
 }
 void ConfirmLeave(Action action){if(closing||!Operations.CanMutate)return;if(!HasUnsavedChanges){action();return;}ShowLeaveDialog(action);}
}
