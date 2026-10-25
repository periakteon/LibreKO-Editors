using Godot;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.References;
namespace LibreKO.Editors.Desktop;
public partial class EditorApp {
 readonly HashSet<string> invalidFields=new();
 internal InventoryPanel? InventoryView;int formsGeneration;
 internal Task PropertiesReady=Task.CompletedTask;
 internal void BuildProperties()=>PropertiesReady=closing?Task.CompletedTask:BuildPropertiesAsync();
 async Task BuildPropertiesAsync(){try{
  var generation=++formsGeneration;var databaseGeneration=Session.Generation;Ui.Clear(Properties);Ui.Clear(InventoryColumn);InventoryView=null;fields.Clear();invalidFields.Clear();UpdateChrome();var draft=Draft;
  if(draft==null){Ui.Label(Properties,"Connect a database to edit this record.",13,Ui.Muted);return;}
  var schema=await Client.SendAsync<FieldDescriptor[]>("schema",new{kind=draft.Original.Key.Kind});if(Draft!=draft||generation!=formsGeneration)return;
  if(Module==2){await BuildAccountOverview(draft);if(Draft!=draft||generation!=formsGeneration)return;}
  if(Module==1){
   InventoryView=Ui.Add(InventoryColumn,new InventoryPanel());InventoryView.Configure(this,draft);await InventoryView.LoadItems();if(Draft!=draft||generation!=formsGeneration)return;
   await LoadCharacterAccount(draft);if(Draft!=draft||generation!=formsGeneration)return;
   var appearance=Ui.Group(Properties,"Appearance preview");var colourRow=Ui.Row(appearance);Ui.Label(colourRow,"Hair colour",12,Ui.Muted).SizeFlagsHorizontal=SizeFlags.ExpandFill;var picker=Ui.Add(colourRow,new ColorPickerButton{Color=LibreKO.Domain.HairCode.ColourOf(FieldValueParser.ReadInt32(draft.Values["Hair"])),CustomMinimumSize=new(100,30)});
   picker.ColorChanged+=c=>{draft.Set("Hair",LibreKO.Domain.HairCode.Pack(LibreKO.Domain.HairCode.StyleOf(FieldValueParser.ReadInt32(draft.Values["Hair"])),c));if(fields.TryGetValue("Hair",out var f)&&f is LineEdit input)input.Text=draft.Values["Hair"]!.ToString();RefreshPreview();};
  }
  foreach(var group in schema.GroupBy(x=>Ui.Section(draft.Original.Key.Kind,x))){
   var section=Ui.Group(Properties,group.Key);
   if(group.Key is "Session & statistics" or "Session information"){section.Visible=false;section.GetParent().GetChild<Button>(0).Text="▸  "+group.Key.ToUpperInvariant();}
   foreach(var f in group){
    if(Module==2&&f.Writable&&f.Choices!=null&&f.Reference==null){AccountField(section,f,draft);continue;}
    if(f.Writable&&f.Kind==FieldKind.Boolean){var toggle=Ui.Add(section,new CheckButton{Text=Ui.FieldName(f.Name),ButtonPressed=bool.TryParse(draft.Values[f.Name]?.ToString(),out var enabled)&&enabled});toggle.Toggled+=value=>{if(Draft==draft&&Operations.CanMutate){draft.Set(f.Name,value);UpdateChrome();}};fields[f.Name]=toggle;continue;}
    var column=Ui.Column(section);var row=Ui.Row(column);var label=Ui.Label(row,Ui.FieldName(f.Name),12,Ui.Muted);label.TooltipText=f.Name;label.CustomMinimumSize=new(135,0);
    var value=draft.Values[f.Name]?.ToString();if(f.Kind==FieldKind.DateTime&&DateTimeOffset.TryParse(value,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.AssumeUniversal|System.Globalization.DateTimeStyles.AdjustToUniversal,out var date))value=date.Year>1?date.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss"):"";var input=Ui.Input(f.Kind==FieldKind.DateTime?column:row,f.Name,value??(f.Writable?"":"—"));input.Editable=f.Writable;input.CustomMinimumSize=new(90,0);if(f.Name=="VipVaultExpiry"&&value=="")input.PlaceholderText="Not active";fields[f.Name]=input;
    Label? meaning=null;
    if(f.Choices!=null||f.Reference!=null){
     meaning=Ui.Label(column,"",12,Ui.Accent);meaning.AutowrapMode=TextServer.AutowrapMode.WordSmart;meaning.CustomMinimumSize=new(250,0);
     if(f.Writable)Ui.Button(row,"Find…",()=>ShowReferencePicker(f.Reference,f.Choices,entry=>{input.Text=entry.Id.ToString();input.EmitSignal(LineEdit.SignalName.TextChanged,input.Text);},f.Reference=="bindpoints"?FieldValueParser.ReadInt32(draft.Values["MapId"]):null));
    }
    Task UpdateMeaning(){if(closing||meaning==null||!IsInstanceValid(meaning)||!meaning.IsInsideTree())return Task.CompletedTask;var text=input.Text;meaning.Text=f.Choices!=null?CodeLabels.Describe(f.Choices,text):text+" · Resolving…";
     if(f.Reference!=null&&f.Choices?.ContainsKey(text)!=true&&int.TryParse(text,out _))Session.ReferencePresenter.Bind(meaning,()=>int.TryParse(input.Text,out var id)?new(f.Reference,id,f.Reference=="bindpoints"?FieldValueParser.ReadInt32(draft.Values["MapId"]):null):null,()=>!closing&&Visible&&Draft==draft&&generation==formsGeneration&&databaseGeneration==Session.Generation,r=>{meaning.Text=ReferenceLabels.Display(r);meaning.TooltipText=meaning.Text+"\n"+r.Source;});return Task.CompletedTask;
    }
    if(f.Writable)input.TextChanged+=async text=>{if(closing||!Visible||!Operations.CanMutate||Draft!=draft||databaseGeneration!=Session.Generation||generation!=formsGeneration)return;try{draft.Set(f.Name,FieldValueParser.Parse(f,text));invalidFields.Remove(f.Name);input.RemoveThemeColorOverride("font_color");if(f.Group=="Appearance")RefreshPreview();RefreshAccountSummary();await UpdateMeaning();}catch(Exception e){if(closing||!IsInstanceValid(input))return;invalidFields.Add(f.Name);input.AddThemeColorOverride("font_color",new Color("#ff9f9f"));Status.Text=f.Name+": "+e.Message;}UpdateChrome();};
    if(f.Kind==FieldKind.DateTime){Ui.Label(column,"UTC · yyyy-MM-dd HH:mm:ss"+(f.Name!="VipVaultExpiry"?" · blank = not set":""),11,Ui.Muted);Ui.Button(row,"Now",()=>{input.Text=DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");input.EmitSignal(LineEdit.SignalName.TextChanged,input.Text);});if(f.Name!="VipVaultExpiry")Ui.Button(row,"Clear",()=>{input.Text="";input.EmitSignal(LineEdit.SignalName.TextChanged,input.Text);});}
    await UpdateMeaning();if(Draft!=draft||generation!=formsGeneration)return;
   }
  }
 }catch(Exception e){if(!closing&&IsInstanceValid(Status))Status.Text=e.Message;}}
 internal void ShowReferencePicker(string? kind,IReadOnlyDictionary<string,string>? choices,Action<ReferenceEntry> selected,int? context=null,InventoryItemFilter? inventory=null){
  if(closing)return;var databaseGeneration=Session.Generation;var ownerDraft=Draft;bool Current()=>!closing&&Visible&&databaseGeneration==Session.Generation&&ReferenceEquals(ownerDraft,Draft);
  if(kind==null){ReferencePicker.ShowCodes(this,choices!,selected,Current);return;}
  ReferencePicker.Show(this,Session,new(kind,Zone:context),entry=>selected(new(entry.Key.Id,entry.Label,entry.Details)),Current,inventory);
 }
 void CloneItem(){if(Module!=0||Draft==null||!Connected)return;var source=Draft;var dialog=Ui.Add(this,new ConfirmationDialog{Title="Clone database item",DialogText="Enter an unused positive item ID."});var input=Ui.Input(dialog,"New item ID");dialog.Confirmed+=()=>Run(async()=>{if(!int.TryParse(input.Text,out var id)||id<=0)throw new InvalidOperationException("Enter a positive item ID.");var values=(JsonObject)source.Values.DeepClone();values["Num"]=id;var before=new RecordSnapshot(new(RecordKind.Item,id),new(),"missing");var result=await Client.SendAsync<SaveResult>("items.save",new RecordChange(Guid.NewGuid(),before,values,Create:true));if(result.Status!=SaveStatus.Saved)throw new InvalidOperationException(result.Message??"Clone rejected.");Session.NotifyDatabaseChange();await LoadList();await SelectRecord(id);dialog.QueueFree();});dialog.PopupCentered(new(400,150));}
 void ShowSettings()=>Session.Shell.ShowSettings();
 void Reload()=>Session.Shell.ShowSettings();
}
