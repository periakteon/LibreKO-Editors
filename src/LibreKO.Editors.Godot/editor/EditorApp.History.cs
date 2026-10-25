using System.Text.Json;
using Godot;
using System.Text.Json.Nodes;
using LibreKO.Editors.Core;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public partial class EditorApp {
 void ShowHistory(){
  if(!Connected||HistoryRoot.Length==0){Status.Text="Connect a database to view its history.";return;}
  var root=HistoryRoot;var dialog=Ui.Add(this,new AcceptDialog{Title="Database history and recovery",MinSize=new(940,650)});var content=Ui.Column(dialog);content.Position=new(15,40);content.CustomMinimumSize=new(890,0);
  void Display(){
   Ui.Clear(content);var journal=new OperationJournal(root);Ui.Label(content,"History for the connected database · credentials are never recorded",12,Ui.Muted);var list=Ui.Add(content,new ItemList{CustomMinimumSize=new(880,310)});
   var dir=System.IO.Path.Combine(root,"changes");var files=System.IO.Directory.Exists(dir)?System.IO.Directory.GetFiles(dir,"*.json").OrderByDescending(System.IO.File.GetLastWriteTimeUtc).ToArray():[];
   foreach(var f in files){var saved=JsonNode.Parse(System.IO.File.ReadAllText(f))!;var change=saved["change"]!.Deserialize<RecordChange>(JsonConfig.Options)!;list.AddItem($"{change.Before.Key.Kind} {change.Before.Key.Id} · {saved["result"]?["values"]?["Name"]??saved["result"]?["values"]?["Login"]} · "+string.Join(", ",change.ChangedValues.Select(x=>Ui.FieldName(x.Key)))+(change.Inventory!=null?" · inventory":""));}
   var preview=Ui.Add(content,new TextEdit{Editable=false,CustomMinimumSize=new(880,110),WrapMode=TextEdit.LineWrappingMode.Boundary});
   var historyGeneration=Session.Generation;int reviewSerial=0;
   list.ItemSelected+=async index=>{var request=++reviewSerial;try{var saved=JsonNode.Parse(System.IO.File.ReadAllText(files[(int)index]))!;var change=saved["change"]!.Deserialize<RecordChange>(JsonConfig.Options)!;var after=saved["result"]!.Deserialize<RecordSnapshot>(JsonConfig.Options)!;var text=string.Join("\n",change.ChangedValues.Select(x=>Ui.FieldName(x.Key)+": "+change.Before.Values[x.Key]+" → "+x.Value));var names="Before:\n"+await ReferencePresenter.DescribeFieldsAsync(Session,after.Key.Kind,change.Before.Values)+"\nAfter:\n"+await ReferencePresenter.DescribeFieldsAsync(Session,after.Key.Kind,after.Values);names+="\nInventory:\n"+await ReferencePresenter.DescribeInventoryAsync(Session,change.Before.Inventory,after.Inventory);if(!closing&&IsInstanceValid(preview)&&preview.IsInsideTree()&&historyGeneration==Session.Generation&&request==reviewSerial)preview.Text=text+"\n\nCurrent database reference names:\n"+names;}catch(Exception e){if(IsInstanceValid(preview)&&request==reviewSerial)preview.Text=e.Message;}};
   foreach(var pending in journal.Pending()){var c=pending;var row=Ui.Row(content);Ui.Label(row,$"{c.Before.Key.Kind} {c.Before.Key.Id} · pending {c.OperationId}",12);Ui.Button(row,"Reconcile",()=>Run(async()=>{RequireSameDatabase();var current=await Client.SendAsync<RecordSnapshot>(Route(c.Before.Key.Kind)+".get",new{id=c.Before.Key.Id});var state=RecoveryResolver.Resolve(c,current);if(state==RecoveryDisposition.Applied)journal.Commit(c,current);if(state==RecoveryDisposition.NotApplied)journal.Abort(c.OperationId);Status.Text="Operation: "+state;Display();}));}
   var actions=Ui.Row(content);Ui.Button(actions,"Replay selected",()=>Replay(false));Ui.Button(actions,"Reverse selected",()=>Replay(true));
   void RequireSameDatabase(){if(!Connected||root!=HistoryRoot)throw new InvalidOperationException("Reconnect the database this history belongs to.");}
   void Replay(bool reverse){ConfirmLeave(()=>Run(async()=>{RequireSameDatabase();if(list.GetSelectedItems().Length==0)return;var saved=JsonNode.Parse(System.IO.File.ReadAllText(files[list.GetSelectedItems()[0]]))!;var original=saved["change"]!.Deserialize<RecordChange>(JsonConfig.Options)!;var after=saved["result"]!.Deserialize<RecordSnapshot>(JsonConfig.Options)!;var current=await Client.SendAsync<RecordSnapshot>(Route(original.Before.Key.Kind)+".get",new{id=original.Before.Key.Id});var result=await Client.SendAsync<SaveResult>(Route(original.Before.Key.Kind)+".save",ChangeReplay.Prepare(original,current,after,reverse));Status.Text=result.Message??result.Status.ToString();if(result.Status==SaveStatus.Saved){Session.NotifyDatabaseChange();ClearSelection();await LoadList();}Display();}));}
  }
  Display();dialog.PopupCentered();
 }
}
