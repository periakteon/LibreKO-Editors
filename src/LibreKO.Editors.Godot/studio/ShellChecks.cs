using Godot;

namespace LibreKO.Editors.Desktop;
public static class ShellChecks
{
    public static async Task Run(EditorShell shell, string mode)
    {
        if(mode=="help"){await HelpChecks.Run(shell);return;}
        if (mode == "browser-history") { await NavigationChecks.Run(shell); return; }
        if (mode == "loading-source-close") { await LoadingChecks.Run(shell); return; }
        if (mode == "world-navigation") { await WorldNavigationChecks.Run(shell); return; }
        if (mode == "item-origins") { await ItemOriginChecks.Run(shell); return; }
        if (mode == "npc-contents") { await NpcContentsChecks.Run(shell); return; }
        if (mode == "source-preview-close") { await SourcePreviewCloseChecks.Run(shell); return; }
        if(mode is "pool-stale" or "pool-replay" or "pool-users"){await PoolStateChecks.Run(shell,mode);return;}
        if(mode is "world-source-preview" or "world-mixed-save" or "world-authoring-shutdown"){await AuthoringChecks.Run(shell,mode);return;}
        if(mode=="world-client-build"){await BuildChecks.Run(shell);return;}
        if(mode=="world-verify-client-output"){await BuildChecks.VerifyOutput();return;}
        if(mode is "world-terrain" or "world-scenery" or "world-npc-visuals"){await WorldChecks.Run(shell,mode);return;}
        if(mode is "world-json" or "world-seed-compare" or "world-file-apply" or "world-source-cancel"){await WorldChecks.Run(shell,mode);return;}
        if (mode is "world-history" or "world-editor" or "world-drafts" or "world-spatial" or "world-gallery" or "world-smd" or "world-quests") { await WorldChecks.Run(shell, mode); return; }
        if (mode == "named-references") { await ReferenceChecks.Run(shell); return; }
        if (mode is "source-profiles" or "file-recovery") { await SourceChecks.Run(shell, mode); return; }
        if (mode == "shell-refresh")
        {
            await shell.RequestWorkspaceAsync("content");
            var returningContent = (EditorApp)shell.Active!;
            returningContent.Module = 0;
            await returningContent.SelectRecord(123450001);
            await returningContent.PropertiesReady;
            await shell.RequestWorkspaceAsync(null);
            var old = returningContent.Draft!;
            var input = returningContent.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().Single(l => l.PlaceholderText == "Damage");
            var before = old.Values["Damage"]!.ToString();
            var returning = shell.RequestWorkspaceAsync("content");
            var gated = !shell.Session.Operations.CanMutate && shell.GetViewport().GuiDisableInput;
            input.Text = "222";
            input.EmitSignal(LineEdit.SignalName.TextChanged, input.Text);
            var oldEdited = old.Values["Damage"]!.ToString() != before;
            await returning;
            if (!gated || oldEdited)
                throw new Exception("Returning Content refresh allowed a retained form edit");
            return;
        }

        if (mode == "shell-gallery")
        {
            await EventGalleries.Run(shell, true);
            return;
        }

        if (mode == "shell-compatibility")
        {
            if (!shell.Session.Connected)
                throw new Exception("Use the isolated fixture.");
            CompatibilityView.Show(shell.Session, "events");
            await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
            var dialog = shell.GetChildren().OfType<AcceptDialog>().Single();
            if (!dialog.FindChildren("*", "Label", true, false).OfType<Label>().Any(l => l.Text.Contains("EVENT TABLE")))
                throw new Exception("Event compatibility missing");
            dialog.QueueFree();
            shell.Session.NotifyDatabaseChange();
            if (!shell.Session.EventReportStale)
                throw new Exception("Event report stayed current after a write");
            await shell.Session.RefreshEventCompatibilityAsync();
            if (shell.Session.EventReportStale)
                throw new Exception("Event schema refresh stayed stale");
            var originalConnection = shell.Session.ConnectionString;
            if (!originalConnection.Contains("libreko_editor_test_", StringComparison.OrdinalIgnoreCase))
                throw new Exception("Use isolated schema fixtures.");
            var builder = new System.Data.Common.DbConnectionStringBuilder
            {
                ConnectionString = originalConnection
            };
            builder["Database"] = "libreko_editor_test_eventsonly";
            await shell.Session.ConnectAsync(builder.ConnectionString);
            if (shell.Session.ContentCompatibility!.Modules.Single(m => m.Kind == LibreKO.Editors.Contracts.RecordKind.Item).CanBrowse || !shell.Session.EventCompatibility!.Tables.Single(t => t.Table == LibreKO.Editors.Contracts.EventTable.TempleEventSchedules).CanBrowse)
                throw new Exception("Content incompatibility blocked Events");
            await shell.RequestWorkspaceAsync("events");
            builder["Database"] = "libreko_editor_test_contentonly";
            await shell.Session.ConnectAsync(builder.ConnectionString);
            if (!shell.Session.ContentCompatibility!.Modules.Single(m => m.Kind == LibreKO.Editors.Contracts.RecordKind.Item).CanBrowse || shell.Session.EventCompatibility!.Tables.Single(t => t.Table == LibreKO.Editors.Contracts.EventTable.TempleEventSchedules).CanBrowse)
                throw new Exception("Events incompatibility blocked Content");
            await shell.Session.ConnectAsync(originalConnection);
            await shell.RequestWorkspaceAsync(null);
            shell.ShowHistoryAreas();
            await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!shell.GetChildren().OfType<AcceptDialog>().Any(d => d.Title == "Choose history area"))
                throw new Exception("Home histories unavailable");
            return;
        }

        if (mode.StartsWith("events-", StringComparison.Ordinal))
        {
            await EventsChecks.Run(shell, mode);
            return;
        }

        if (mode == "boot")
        {
            if (shell.Active != null)
                throw new Exception("Startup must show the editor chooser.");
            return;
        }

        if (mode is "shell-navigation" or "shell-shutdown")
        {
            var client = shell.Session.Client;
            await shell.RequestWorkspaceAsync("content");
            var workspace = (EditorApp)shell.Active!;
            if (shell.Session.Connected && workspace.Draft != null)
            {
                workspace.Draft.Set("Num", -77);
                var navigation = shell.RequestWorkspaceAsync(null);
                var dialog = shell.GetChildren().OfType<ConfirmationDialog>().Single();
                dialog.GuiDisableInput = true;
                dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
                while (!shell.Session.Operations.CanMutate)
                    await shell.ToSignal(shell.GetTree().CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
                if (navigation.IsCompleted || shell.Active != workspace)
                    throw new Exception("A rejected save allowed navigation.");
                dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
                await navigation;
                if (shell.Active != workspace || !workspace.Draft.IsDirty)
                    throw new Exception("Cancel discarded a draft.");
                workspace.Discard();
            }

            await shell.RequestWorkspaceAsync(null);
            if (shell.Active != null || !ReferenceEquals(client, shell.Session.Client))
                throw new Exception("Home navigation replaced the shared session.");
            if (workspace.Draft != null)
            {
                workspace.Draft.Set("Num", -88);
                shell._Input(new InputEventKey { Pressed = true, CtrlPressed = true, Keycode = Key.Z });
                if (workspace.Draft.Values["Num"]?.ToString() != "-88")
                    throw new Exception("An inactive editor handled Undo.");
                workspace.Discard();
            }

            await shell.RequestWorkspaceAsync("content");
            if (!ReferenceEquals(client, shell.Session.Client))
                throw new Exception("Switching reopened the helper.");
            if (mode == "shell-shutdown")
            {
                GD.Print("CHECK shell-shutdown PASS");
                await shell.QuitAsync();
            }

            return;
        }

        await shell.RequestWorkspaceAsync("content");
        var content = (EditorApp)shell.Workspaces["content"];
        await content.PropertiesReady;
        await EditorChecks.Run(content, mode);
    }
}
