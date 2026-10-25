using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
namespace LibreKO.Editors.Desktop;
public partial class EditorApp {
 internal bool ContentReady;string ContentFailure="";
 DatabaseCompatibility? databaseCompatibility;CompatibilityReportBuilder compatibilityReport=new();
 ConfirmationDialog? compatibilityDialog;
 bool compatibilityStale;CancellationTokenSource? compatibilityCancellation;
 internal void InvalidateCompatibility()=>compatibilityStale=true;
 string compatibilitySummary="Not scanned";int compatibilityItems,compatibilityCharacters,compatibilityResources;
 ModuleCompatibility ModuleSupport(RecordKind kind)=>databaseCompatibility?.Modules.FirstOrDefault(m=>m.Kind==kind)??new(kind,!Connected,false,Connected?"Database preflight unavailable; use the matching editor helper.":"Database disconnected.");
 void ResetCompatibility(){
  compatibilityReport=new();if(databaseCompatibility!=null)compatibilityReport.AddRange(databaseCompatibility.Issues);
  if(!ContentReady)compatibilityReport.Add(new("content.unavailable",CompatibilitySeverity.Blocking,"Client content",Profile.AssetRoot,ContentFailure+" · Database records can still be managed when their schema is supported."));
  compatibilityItems=compatibilityCharacters=compatibilityResources=0;compatibilityStale=false;compatibilitySummary=Connected?"Schema checked · full scan not run":"Not scanned · connect a database for database checks";
 }
 internal void ShowCompatibility(){
  if(closing)return;if(compatibilityDialog!=null&&IsInstanceValid(compatibilityDialog)){compatibilityDialog.PopupCentered();return;}
  if(databaseCompatibility==null)ResetCompatibility();
  var d=Ui.Add(this,new ConfirmationDialog{Title="Compatibility · database and client",Exclusive=true,MinSize=new(1000,650),OkButtonText="Close"});compatibilityDialog=d;
  var content=Ui.Column(d);content.Position=new(14,36);content.CustomMinimumSize=new(970,0);content.AddThemeConstantOverride("separation",6);
  Ui.Heading(content,"DATABASE + CLIENT COMPATIBILITY",15).AddThemeColorOverride("font_color",Ui.Accent);
  CompatibilityView.AddAreas(content,Session,"content",d);
  var source=Ui.Label(content,"Client: "+Profile.AssetRoot,11,Ui.Muted);source.ClipText=true;source.TooltipText=source.Text;
  Ui.Label(content,$"Database: {databaseCompatibility?.Database??"Disconnected"} · adapter: LibreKO / 96 slots · renderer: {Engine.GetVersionInfo()["string"]}",11,Ui.Muted);
  var modules=Ui.Row(content);var moduleLabels=new Dictionary<RecordKind,Label>();foreach(var kind in Enum.GetValues<RecordKind>()){var label=Ui.Label(modules,"",12);label.SizeFlagsHorizontal=SizeFlags.ExpandFill;moduleLabels[kind]=label;}
  var note=Ui.Label(content,"Checks describe the supported formats. Custom code, enum meanings and same-size inventory changes cannot be certified. External changes require a rescan; content updates require restart.",11,Ui.Muted);note.AutowrapMode=TextServer.AutowrapMode.WordSmart;note.CustomMinimumSize=new(950,0);
  var actions=Ui.Row(content);Button scan=null!;Button schema=null!;Button stop=null!;
  scan=Ui.Button(actions,"Scan database + client",()=>{});Ui.Primary(scan);schema=Ui.Button(actions,"Check schema",()=>{});stop=Ui.Button(actions,"Stop scan",()=>compatibilityCancellation?.Cancel());stop.Disabled=true;
  Ui.Add(actions,new Control{SizeFlagsHorizontal=SizeFlags.ExpandFill});var filter=Ui.Add(actions,new OptionButton());foreach(var option in new[]{"All issues","Blocking","Warnings","Information"})filter.AddItem(option);
  var progress=Ui.Label(content,"",12,Ui.Accent);var counts=Ui.Label(content,"",11,Ui.Muted);counts.ClipText=true;
  var list=Ui.Add(content,new ItemList{CustomMinimumSize=new(950,205),SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill});
  var detail=Ui.Add(content,new RichTextLabel{CustomMinimumSize=new(950,90),BbcodeEnabled=false,ScrollActive=true,Text="Select an issue for its explanation. Scans do not change database records or client assets."});
  var footer=Ui.Row(content);var open=Ui.Button(footer,"Open selected record",()=>{});open.Disabled=true;var displayed=new List<CompatibilityIssue>();
  void Totals(){detail.Text="Issue totals (all scanned records; up to 50 examples per code, 500 overall):\n"+string.Join("\n",compatibilityReport.Counts.OrderBy(p=>p.Key).Select(p=>$"{p.Key}: {p.Value:N0}"))+"\n\nThis report does not certify custom renderer code or same-length inventory semantics.";}
  Ui.Button(footer,"Issue totals",Totals);
  void Refresh(){
   if(closing||!IsInstanceValid(d))return;
   foreach(var pair in moduleLabels){var support=ModuleSupport(pair.Key);pair.Value.Text=$"{pair.Key}: {(support.CanSave?"schema supports edits":support.CanBrowse?"read only":"unavailable")}";pair.Value.Modulate=support.CanSave?LibreKO.UiTheme.Good:Ui.Accent;pair.Value.TooltipText=support.Reason;}
   progress.Text=compatibilitySummary+(compatibilityStale?" · STALE: database changed":"");counts.Text=$"{compatibilityItems:N0} database items · {compatibilityCharacters:N0} inventories · {compatibilityResources:N0} resource paths · {compatibilityReport.Blocking:N0} blocking · {compatibilityReport.Warnings:N0} warnings · showing up to 500 examples";
   list.Clear();displayed.Clear();foreach(var issue in compatibilityReport.Examples){if(filter.Selected==1&&issue.Severity!=CompatibilitySeverity.Blocking||filter.Selected==2&&issue.Severity!=CompatibilitySeverity.Warning||filter.Selected==3&&issue.Severity!=CompatibilitySeverity.Information)continue;displayed.Add(issue);var index=list.AddItem($"{issue.Severity} · {issue.Code} · {issue.Target}");list.SetItemTooltip(index,issue.Message);}
   open.Disabled=true;
   Totals();
  }
  filter.ItemSelected+=_=>Refresh();list.ItemSelected+=index=>{var issue=displayed[(int)index];detail.Text=issue.Code+" · "+issue.Scope+"\n"+issue.Target+"\n\n"+issue.Message;open.Disabled=compatibilityCancellation!=null||issue.Record==null||!ModuleSupport(issue.Record.Kind).CanBrowse;};
  open.Pressed+=async()=>{var selected=list.GetSelectedItems();if(selected.Length==0)return;var record=displayed[selected[0]].Record;if(record==null)return;d.QueueFree();await Session.Shell.RequestWorkspaceAsync("content");SwitchModule((int)record.Kind,record.Id);};
  async Task Scan(bool schemaOnly){
   if(!Operations.CanMutate||compatibilityCancellation!=null)return;
   using var lease=Operations.Enter();using var cancel=new CancellationTokenSource();compatibilityCancellation=cancel;scan.Disabled=schema.Disabled=true;stop.Disabled=false;open.Disabled=true;
   try{await ScanCompatibility(schemaOnly,cancel.Token,()=>{if(!closing&&IsInstanceValid(progress))progress.Text=compatibilitySummary;});}
   catch(OperationCanceledException){if(Client.IsClosed){Connected=false;compatibilitySummary="Incomplete · database helper interrupted; reconnect before editing";}else compatibilitySummary="Cancelled · partial results; in-flight reads finished without modifying data";}
   catch(Exception e){compatibilitySummary="Incomplete · "+e.Message;compatibilityReport.Add(new("scan.incomplete",CompatibilitySeverity.Warning,"Scan","Database/client",e.Message));}
   finally{compatibilityCancellation=null;if(!closing&&IsInstanceValid(d)){scan.Disabled=schema.Disabled=false;stop.Disabled=true;Refresh();}UpdateChrome();}
  }
  scan.Pressed+=()=>_ =Scan(false);schema.Pressed+=()=>_ =Scan(true);
  void Close(){compatibilityCancellation?.Cancel();d.QueueFree();}
  d.Confirmed+=Close;d.Canceled+=Close;Refresh();d.PopupCentered(new(1040,690));
 }
 async Task ScanCompatibility(bool schemaOnly,CancellationToken cancel,Action progress){
  compatibilitySummary="Checking database structure…";progress();
  if(Connected)databaseCompatibility=await Client.SendAsync<DatabaseCompatibility>("compatibility.schema",new{});
  if(databaseCompatibility!=null)Session.AcceptContentCompatibility(databaseCompatibility);ResetCompatibility();if(schemaOnly){compatibilitySummary="Schema checked · full scan not run";return;}
  var assets=new ClientCompatibilityAudit(compatibilityReport);var ids=new HashSet<int>();
  if(Connected&&ModuleSupport(RecordKind.Item).CanBrowse){
   int? after=null;do{cancel.ThrowIfCancellationRequested();var batch=await Client.SendAsync<CompatibilityItemBatch>("compatibility.items",new{afterId=after});foreach(var item in batch.Rows){ids.Add(item.Id);if(ContentReady){compatibilityReport.AddRange(CompatibilityRules.Compare(item,assets.Item(item.Id)));assets.Icon(item.Id);}}compatibilityItems+=batch.Rows.Count;after=batch.NextId;compatibilitySummary=$"Comparing items… {compatibilityItems:N0}";progress();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}while(after!=null);
  }
  if(Connected&&ModuleSupport(RecordKind.Character).CanBrowse){
   int? after=null;do{cancel.ThrowIfCancellationRequested();var batch=await Client.SendAsync<CompatibilityInventoryBatch>("compatibility.inventories",new{afterId=after});compatibilityReport.AddRange(batch.Issues);compatibilityCharacters+=batch.Scanned;after=batch.NextId;compatibilitySummary=$"Checking inventory references… {compatibilityCharacters:N0}";progress();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}while(after!=null);
  }
  if(ContentReady){
   var clientIds=new HashSet<int>();var visited=0;
   foreach(var id in assets.ClientIds()){
    cancel.ThrowIfCancellationRequested();if(!clientIds.Add(id))continue;
    if(Connected&&ModuleSupport(RecordKind.Item).CanBrowse&&!ids.Contains(id))compatibilityReport.Add(new("catalog.client-only",CompatibilitySeverity.Information,"Items",id.ToString(),"Resolvable client item/variant ID is absent from the database. This may be an intentional deletion or an unused upgrade combination; the scan does not recreate it."));
    if(!ids.Contains(id))assets.Icon(id);
    if(++visited%512==0){compatibilitySummary=$"Checking client catalog… {visited:N0}";progress();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}
   }
   assets.Begin();var documents=0;while(assets.HasDocuments){cancel.ThrowIfCancellationRequested();assets.NextDocument();compatibilityResources=assets.Checked;if(++documents%4==0){compatibilitySummary=$"Checking models and effects… {compatibilityResources:N0} paths";progress();await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);}}
  }
  var skipped=new List<string>();if(!Connected){skipped.Add("database items and inventories (disconnected)");}else{if(!ModuleSupport(RecordKind.Item).CanBrowse)skipped.Add("database items (unsupported schema)");if(!ModuleSupport(RecordKind.Character).CanBrowse)skipped.Add("inventories (unsupported schema)");}if(!ContentReady)skipped.Add("client comparison and resources (content unavailable)");
  if(skipped.Count>0)compatibilityReport.Add(new("scan.skipped",CompatibilitySeverity.Warning,"Scan","Skipped checks",string.Join("; ",skipped)));
  compatibilityReport.Add(new("scan.limits",CompatibilitySeverity.Information,"Scan","Coverage","Compared saved item name, kind, slot, extension, damage, defense and durability. Resource existence is checked, including armor fallbacks for each supported preview race; arbitrary shader/rig behavior is not certified. Inventory checks recognize byte layouts, not unknown same-length semantic changes. Counts cover completed pages; scans do not lock the live database."));
  cancel.ThrowIfCancellationRequested();compatibilitySummary=$"Completed {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC · {compatibilityReport.Total:N0} findings";
  compatibilityStale=false;
 }
}
