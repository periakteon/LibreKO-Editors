using Godot;
using System.Data.Common;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public partial class EditorApp {
 readonly Dictionary<int,Button> navigation=new();Button? cloneButton;Control? itemFilters;int highlightedModule=-1;
 void ShowConnectionDialog()=>Session.Shell.ShowConnectionDialog();
 void UpdateChrome(){
  if(closing)return;
  if(cloneButton!=null){cloneButton.Visible=Module==0;cloneButton.Disabled=Connected&&!ModuleSupport(RecordKind.Item).CanSave;}
  if(itemFilters!=null)itemFilters.Visible=Module==0;
  Race.Visible=Module==0;
  if(previewControls!=null)previewControls.Visible=Module!=2;Preview.Visible=Module!=2;if(previewCard!=null)previewCard.Visible=Module!=2;if(itemIconCard!=null)itemIconCard.Visible=Module==0;
  if(accountOverview!=null)accountOverview.Visible=Module==2;
  if(characterAccountRow!=null)characterAccountRow.Visible=Module==1&&Draft!=null;
  if(browserCaption!=null)browserCaption.Text=Module switch{0=>"ITEM LIBRARY",1=>"PLAYER CHARACTERS",_=>"ACCOUNTS"};search.PlaceholderText=Module==2?"Find account by login or ID":"Find by name or ID";
  if(highlightedModule==Module)return;highlightedModule=Module;Inspector.SetTabHidden(0,Module!=1);Inspector.CurrentTab=Module==1?0:1;
  foreach(var entry in navigation)Ui.Navigation(entry.Value,entry.Key==Module);
 }
}
