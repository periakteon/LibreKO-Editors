using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
namespace LibreKO.Editors.Desktop;
public partial class EditorApp:IEditorWorkspace {
 internal EditorSession Session=null!;internal DataHostClient Client=>Session.Client;internal WorkspaceProfile Profile{get=>Session.Profile;set=>Session.Profile=value;}internal string Root=>Session.Root;internal bool Connected{get=>Session.Connected;set=>Session.Connected=value;}
 public string Id=>"content";public Control View=>this;bool IEditorWorkspace.HasUnsavedChanges=>HasUnsavedChanges;public bool CanSave=>Draft?.IsDirty==true&&Connected&&SelectedId!=0&&Record?.IsOnline!=true&&invalidFields.Count==0&&ModuleSupport(ActiveKind).CanSave;
 public async Task InitializeAsync(EditorSession session){Session=session;profilePath=session.ProfilePath;session.Shell.Assets.EnsureLoaded();ContentReady=session.Shell.Assets.Available;ContentFailure=session.Shell.Assets.Failure;var assets=session.Shell.Assets;Items=assets.Json("assets/items/items.json");Bodies=assets.Json("assets/characters/index.json");Faces=assets.Json("assets/characters/faces.json");Weapons=assets.Json("assets/items/weapon/index.json");Armor=assets.Json("assets/items/armor/index.json");var args=OS.GetCmdlineUserArgs();if(args.Contains("--module")&&int.TryParse(args.SkipWhile(x=>x!="--module").Skip(1).FirstOrDefault(),out var module))Module=Math.Clamp(module,0,2);BuildUi();ready=true;InvalidateDatabaseState();await Task.CompletedTask;}
 public async Task RefreshAsync(){if(Session.Operations.CanMutate){await Session.RunAsync(RefreshCoreAsync);return;}await RefreshCoreAsync();}
 async Task RefreshCoreAsync(){await LoadList();if(SelectedId!=0){var id=SelectedId;await SelectRecord(id);}else if(ids.Count>0){List.Select(0);await SelectRecord(ids[0]);}await PropertiesReady;ShowRecoveryWarning();}
 public async Task<bool> SaveAsync(){var ok=await Session.RunAsync(SaveRecord);return ok&&!HasUnsavedChanges;}
 public void Discard(){if(Record!=null){Draft=new(Record);Inventory=Draft.Inventory?.ToList();BuildProperties();RefreshPreview();}}
 public void Undo(bool redo)=>UndoDraft(redo);public void FocusSearch()=>search.GrabFocus();public void SetActive(bool active){Visible=active;SetProcess(active);if(!active){formsGeneration++;} }
 public void InvalidateDatabaseState(){databaseCompatibility=Session.ContentCompatibility;if(Properties!=null){ClearSelection();ResetCompatibility();}}
 void IEditorWorkspace.ShowHistory()=>ShowHistory();void IEditorWorkspace.ShowCompatibility()=>ShowCompatibility();
}
