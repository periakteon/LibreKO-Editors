using Godot;
using LibreKO.Editors.Core;
using LibreKO.Editors.Contracts;
using System.Text.Json;

namespace LibreKO.Editors.Desktop;
public partial class EditorShell
{
    public bool CanOpenTarget(NavigationTarget? target) => target != null && target.EditorId is "content" or "events" or "world";

    public async Task OpenTargetAsync(NavigationTarget target)
    {
        if (navigating || replaying || !Session.Operations.CanMutate || Session.IsClosing) return;
        if (target.ScopeId != Session.HistoryRoot && target.ScopeId != Session.Sources.Profile?.Id)
        { SetStatus("This reference belongs to a different database/source profile."); return; }
        if (!CanOpenTarget(target)) { SetStatus("This related editor is not available yet."); return; }
        RememberContext();var origin=CurrentLocation();var originalWorkspace=Active;var scope=NavigationScope;bool attempted=false,success=false;navigating = true;
        try
        {
        if (!await CanLeaveAsync()) return;
        if (target.ScopeId != Session.HistoryRoot && target.ScopeId != Session.Sources.Profile?.Id) return;
        attempted=true;
        NavigationLocksInput=true;GetViewport().GuiDisableInput=true;
        var next = await EnsureWorkspaceAsync(target.EditorId);
        Active?.SetActive(false); Active = next; home.Visible = false; next.SetActive(true);
        if (Active is EditorApp content && target.EditorId == "content")
            success=await Session.RunAsync(async () =>
            {
                content.Module = target.Kind switch { "items" => 0, "characters" => 1, "accounts" => 2, _ => throw new InvalidOperationException("Unsupported Content reference target.") };
                await content.LoadList();
                await content.SelectRecord(target.Key["id"]!.GetValue<int>());
                await content.PropertiesReady;
                if(target.Key["tab"] is {} tab)content.Inspector.CurrentTab=tab.GetValue<int>();
            });
        if (Active is WorldWorkspace world && target.EditorId == "world")
        {
            if (Enum.TryParse<WorldTable>(target.Kind, out var table))
            {var key=new WorldKey(table, target.Key["id"]!.GetValue<int>(), target.Key["zone"]?.GetValue<int>(), target.Key["isMonster"]?.GetValue<bool>());world.ShowDatabase();await world.SelectAsync(key);success=world.Selected==key&&world.Draft?.Rows.Any(r=>r.Key==key)==true;}
            else if(target.Kind=="source")
            {
                var key=target.Key.Deserialize<SourceFileKey>(JsonConfig.Options)??throw new InvalidOperationException("Invalid source target.");
                world.ShowSources();await world.SourceView!.OpenAsync(key);if(target.Key["row"] is {} ordinal)world.SourceView.SelectSourceRow(ordinal.GetValue<int>());
                success=true;
            }
            else SetStatus("Choose a source profile to inspect the related server/client files.");
        }
        if (Active is EventsWorkspace events && target.EditorId == "events")
            success=await Session.RunAsync(async () =>
            {
                if (!Enum.TryParse<EventFamily>(target.Kind, out var family)) throw new InvalidOperationException("Unsupported Events reference target.");
                events.Family = family;
                await events.RefreshAsync();
                await events.SelectAsync(new(family, target.Key["id"]!.GetValue<int>(), target.Key["zone"]?.GetValue<int>()));
            });
        }
        catch (Exception e) { SetStatus("Cannot open reference · " + e.Message); }
        finally
        {
            if(attempted&&!success&&scope==NavigationScope)
            {
                var failure=status.Text;await RestoreOriginAsync(originalWorkspace,origin);SetStatus(failure);
            }
            switcher.Select(Active == null ? 0 : WorkspaceRegistry.All.ToList().FindIndex(x => x.Id == Active.Id) + 1);
            navigating = false;NavigationLocksInput=false;GetViewport().GuiDisableInput=false;if(success)RecordLocation();
        }
    }
    internal async Task<IEditorWorkspace> EnsureWorkspaceAsync(string id)
    {
        if (Workspaces.TryGetValue(id, out var existing))
            return existing;
        IEditorWorkspace workspace = WorkspaceRegistry.Create(id);
        Workspaces[id] = workspace;
        host.AddChild(workspace.View);
        workspace.View.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        workspace.SetActive(false);
        await workspace.InitializeAsync(Session);
        return workspace;
    }

    public async Task RequestWorkspaceAsync(string? editorId)
    {
        if (navigating || Session.IsClosing || !Session.Operations.CanMutate)
            return;
        RememberContext();
        var origin=CurrentLocation();var originalWorkspace=Active;var scope=NavigationScope;bool attempted=false,success=false;
        navigating = true;
        try
        {
            if (!await CanLeaveAsync())
                return;
            NavigationLocksInput=true;GetViewport().GuiDisableInput=true;attempted=true;
            var next = editorId == null ? null : await EnsureWorkspaceAsync(editorId);
            Active?.SetActive(false);
            Active = next;
            home.Visible = next == null;
            next?.SetActive(true);
            success=next==null||await Session.RunAsync(next.RefreshAsync);
            switcher.Select(editorId == null ? 0 : WorkspaceRegistry.All.ToList().FindIndex(x => x.Id == editorId) + 1);
        }
        catch (Exception e)
        {
            SetStatus(e.Message);
        }
        finally
        {
            if(attempted&&!success&&scope==NavigationScope){var failure=status.Text;await RestoreOriginAsync(originalWorkspace,origin);SetStatus(failure);}
            switcher.Select(Active == null ? 0 : WorkspaceRegistry.All.ToList().FindIndex(x => x.Id == Active.Id) + 1);
            navigating = false;
            NavigationLocksInput=false;GetViewport().GuiDisableInput=false;if(success)RecordLocation();
        }
    }

    internal Task<bool> CanLeaveAsync()
    {
        if (Active?.HasUnsavedChanges != true)
            return Task.FromResult(true);
        var source = Active;
        var completion = new TaskCompletionSource<bool>();
        var d = Ui.Add(this, new ConfirmationDialog { Title = "Unsaved changes", DialogText = "Save changes before continuing?\n\nDestinations:\n" + string.Join("\n", source.PendingDestinations), OkButtonText = source.SaveLabel, CancelButtonText = "Cancel", DialogHideOnOk = false });
        d.AddButton("Discard", false, "discard");
        bool choosing = false;
        async Task Choose(LeaveDecision choice)
        {
            if (choosing)
                return;
            choosing = true;
            try
            {
                var allowed = await WorkspaceNavigation.CanLeaveAsync(source.HasUnsavedChanges, choice, source.SaveAsync, source.Discard);
                if (!allowed && choice == LeaveDecision.Save)
                {
                    choosing = false;
                    return;
                }

                d.Hide();
                d.QueueFree();
                completion.TrySetResult(allowed);
            }
            catch (Exception e)
            {
                SetStatus(e.Message);
                choosing = false;
            }
        }

        d.Confirmed += () => _ = Choose(LeaveDecision.Save);
        d.CustomAction += action =>
        {
            if (action == "discard")
                _ = Choose(LeaveDecision.Discard);
        };
        d.Canceled += () => _ = Choose(LeaveDecision.Cancel);
        d.PopupCentered(new(460, 170));
        return completion.Task;
    }

    internal async Task ReconnectAsync(string connection)
    {
        if (!await CanLeaveAsync())
            return;
        await Session.RunAsync(async () =>
        {
            await Session.ConnectAsync(connection);
            if (Active != null)
                await Active.RefreshAsync();
            SetStatus("Database connected · " + Session.DatabaseName);
        });
    }

    internal void InvalidateWorkspaces()
    {
        foreach (var workspace in Workspaces.Values)
            workspace.InvalidateDatabaseState();
    }
}
