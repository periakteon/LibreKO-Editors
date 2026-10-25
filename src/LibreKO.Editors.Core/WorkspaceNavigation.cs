namespace LibreKO.Editors.Core;
public enum LeaveDecision { Save, Discard, Cancel }
public static class WorkspaceNavigation {
 public static async Task<bool> CanLeaveAsync(bool dirty,LeaveDecision choice,Func<Task<bool>> save,Action discard){if(!dirty)return true;if(choice==LeaveDecision.Cancel)return false;if(choice==LeaveDecision.Save)return await save();discard();return true;}
}
