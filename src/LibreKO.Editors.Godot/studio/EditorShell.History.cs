using Godot;
using LibreKO.Editors.Core;
using System.Text.Json.Nodes;

namespace LibreKO.Editors.Desktop;
public partial class EditorShell
{
    internal BrowserHistory NavigationHistory { get; } = new();
    bool replaying;
    Button backButton = null!, forwardButton = null!;
    string NavigationScope => $"{Session.Generation}:{Session.HistoryRoot}:{Session.Sources.Profile?.Id}:{Session.Sources.Generation}";
    WorkspaceLocation CurrentLocation() => Active?.CaptureLocation() ?? new(null, new JsonObject { ["identity"] = "home" });
    void EnsureHistoryScope()
    { if (NavigationHistory.Scope != NavigationScope) { NavigationHistory.Reset(NavigationScope); NavigationHistory.Visit(CurrentLocation()); } }
    internal void RememberContext()
    {
        if (Session == null || replaying || navigating || !Session.Operations.CanMutate) return;
        EnsureHistoryScope(); var location = CurrentLocation(); var previous = NavigationHistory.Current;
        if (previous != null && previous.EditorId == location.EditorId && previous.State["identity"]?.ToString() == location.State["identity"]?.ToString()) NavigationHistory.UpdateCurrent(location);
    }
    internal void RecordLocation()
    {
        if (Session == null || replaying || navigating) return;
        EnsureHistoryScope(); var location = CurrentLocation(); var previous = NavigationHistory.Current;
        if (previous != null && previous.EditorId == location.EditorId && previous.State["identity"]?.ToString() == location.State["identity"]?.ToString()) NavigationHistory.UpdateCurrent(location);
        else NavigationHistory.Visit(location);
    }
    internal async Task NavigateHistoryAsync(bool forward)
    {
        if (navigating || replaying || !Session.Operations.CanMutate || Session.IsClosing) return;
        RememberContext(); EnsureHistoryScope(); var destination = forward ? NavigationHistory.Forward : NavigationHistory.Back;
        if (destination == null) return;
        var origin = CurrentLocation(); var originalWorkspace = Active;
        var scope = NavigationScope; navigating = true;
        try
        {
            if (!await CanLeaveAsync() || scope != NavigationScope) return;
            NavigationLocksInput=true;GetViewport().GuiDisableInput=true;
            replaying = true;
            var next = destination.EditorId == null ? null : await EnsureWorkspaceAsync(destination.EditorId);
            Active?.SetActive(false); Active = next; home.Visible = next == null; next?.SetActive(true);
            bool success = next == null || await Session.RunAsync(() => next.RestoreLocationAsync(destination));
            if (success && scope == NavigationScope) NavigationHistory.CommitMove(destination,forward);
            else if (scope == NavigationScope)
            {
                Active?.SetActive(false); Active = originalWorkspace; home.Visible = Active == null; Active?.SetActive(true);
                if (Active != null && Session.Operations.CanMutate) await Session.RunAsync(() => Active.RestoreLocationAsync(origin));
            }
        }
        catch (Exception e) { SetStatus("Navigation unavailable · " + e.Message); }
        finally
        {
            switcher.Select(Active == null ? 0 : WorkspaceRegistry.All.ToList().FindIndex(x => x.Id == Active.Id) + 1);
            navigating = false; replaying = false;NavigationLocksInput=false;GetViewport().GuiDisableInput=false;
        }
    }
    async Task RestoreOriginAsync(IEditorWorkspace? workspace,WorkspaceLocation location)
    {
        replaying=true;
        try
        {
            Active?.SetActive(false);Active=workspace;home.Visible=Active==null;Active?.SetActive(true);
            if(Active!=null&&Session.Operations.CanMutate)await Session.RunAsync(()=>Active.RestoreLocationAsync(location));
        }
        finally{replaying=false;}
    }
}
