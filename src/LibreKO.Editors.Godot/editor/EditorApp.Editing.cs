using Godot;
using LibreKO.Editors.Core;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public partial class EditorApp {
 internal OperationGate Operations=>Session.Operations;

 internal string HistoryRoot=>Session.HistoryRoot;
 internal bool HasUnsavedChanges=>Draft?.IsDirty==true;
 Label? dirtyLabel;double summaryElapsed;
 public override void _Process(double delta){summaryElapsed+=delta;if(closing||summaryElapsed<0.25||dirtyLabel==null)return;summaryElapsed=0;UpdateChrome();var change=Draft?.BuildChange(Guid.Empty);dirtyLabel.Text=HasUnsavedChanges?"● Unsaved changes":"No unsaved changes";dirtyLabel.TooltipText=HasUnsavedChanges?string.Join(", ",change?.ChangedValues.Select(x=>Ui.FieldName(x.Key))??[])+(change?.Inventory!=null?" · inventory":""):"All changes saved";dirtyLabel.Modulate=HasUnsavedChanges?Ui.Accent:Ui.Muted;}
 internal void RequestSelection(int id){if(Operations.CanMutate)ConfirmLeave(()=>Run(()=>SelectRecord(id)));}
 void ClearSelection(){Draft=null;Record=null;Inventory=null;SelectedId=0;Ui.Clear(Properties);Ui.Clear(InventoryColumn);InventoryView=null;Preview.ShowModel(null);Icon.Texture=null;TitleLabel.Text="Select a record";Info.Text="";}
 void UndoDraft(bool redo){if(!Operations.CanMutate)return;if(redo)Draft?.Redo();else Draft?.Undo();Inventory=Draft?.Inventory?.ToList();BuildProperties();RefreshPreview();}
 void ShowLeaveDialog(Action action){
  var d=Ui.Add(this,new ConfirmationDialog{Title="Unsaved database changes",DialogText="Save the current record before continuing?",OkButtonText="Save",CancelButtonText="Cancel"});d.AddButton("Discard",false,"discard");
  d.Confirmed+=()=>{d.Hide();Run(async()=>{if(HasUnsavedChanges)await SaveRecord();if(HasUnsavedChanges){d.PopupCentered();return;}Callable.From(action).CallDeferred();d.QueueFree();});};
  d.CustomAction+=name=>{if(name=="discard"){ClearSelection();action();d.QueueFree();}};d.Canceled+=()=>d.QueueFree();d.PopupCentered();
 }
 void ShowRecoveryWarning(){if(HistoryRoot.Length==0)return;var count=new OperationJournal(HistoryRoot).Pending().Count;if(count>0)Status.Text+=" · RECOVERY REQUIRED: "+count+" pending operation(s); open Database history.";}
 Task ShowDockerConnections()=>Session.Shell.ShowDockerAsync();
 void RestoreClient()=>Session.RestoreClient();
}
