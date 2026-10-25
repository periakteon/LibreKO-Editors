using Godot;
using LibreKO.Editors.Core;

namespace LibreKO.Editors.Desktop;
public partial class EditorShell : Control
{
    public EditorSession Session { get; private set; } = null !;
    public EditorAssets Assets { get; private set; } = null !;

    internal readonly Dictionary<string, IEditorWorkspace> Workspaces = new();
    internal IEditorWorkspace? Active;
    VBoxContainer layout = null !;
    Control home = null !, host = null !;
    OptionButton switcher = null !;
    Button saveButton = null !, undoButton = null !, redoButton = null !;
    Label databaseLabel = null !, status = null !;
    bool navigating;
    internal bool NavigationLocksInput {get;private set;}
    internal string CurrentStatus=>status?.Text??"";
    int exitCode;
    public override async void _Ready()
    {
        try
        {
            var previewArgs=OS.GetCmdlineUserArgs();if(previewArgs.Contains("--source-preview")){var path=previewArgs.SkipWhile(a=>a!="--source-preview").Skip(1).FirstOrDefault()??throw new InvalidOperationException("Missing source preview path.");SourcePreviewRuntime.Start(this,path);return;}
            Theme = Ui.Theme();
            Session = new(this);
            Assets = new(Session);
            await Session.InitializeAsync();
            BuildUi();
            var connection = System.Environment.GetEnvironmentVariable(Session.Profile.ConnectionEnvironmentVariable);
            if (!string.IsNullOrEmpty(connection))
            {
                try
                {
                    await Session.ConnectAsync(connection);
                }
                catch (Exception e)
                {
                    SetStatus(e.Message);
                }
            }

            var args = OS.GetCmdlineUserArgs();
            if (args.Contains("--check"))
            {
                var mode = args.SkipWhile(a => a != "--check").Skip(1).FirstOrDefault() ?? "boot";
                await ShellChecks.Run(this, mode);
                if (Session.IsClosing)
                    return;
                GD.Print("CHECK " + mode + " PASS");
                await QuitAsync();
            }
            else if (args.Contains("--module"))
                await RequestWorkspaceAsync("content");
        }
        catch (Exception e)
        {
            GD.PrintErr(e);
            if (Session != null && !Session.IsClosing)
            {
                if (status != null)
                    SetStatus(e.Message);
                if (OS.GetCmdlineUserArgs().Contains("--check"))
                    await QuitAsync(1);
            }
        }
    }

    internal void SetStatus(string text)
    {
        if (status != null && IsInstanceValid(status))
            status.Text = text;
        if (Workspaces.GetValueOrDefault("content")is EditorApp content && content.Status != null && IsInstanceValid(content.Status))
            content.Status.Text = text;
    }

    public override void _Process(double delta)
    {
        if (Session == null || status == null)
            return;
        RememberContext();
        backButton.Disabled = navigating || !Session.Operations.CanMutate || NavigationHistory.Back == null;
        forwardButton.Disabled = navigating || !Session.Operations.CanMutate || NavigationHistory.Forward == null;
        saveButton.Disabled = Active?.CanSave != true || !Session.Operations.CanMutate;
        saveButton.Text = Active?.SaveLabel ?? "Save to database";
        saveButton.TooltipText = Active == null ? "Choose an editor" : string.Join("\n", Active.PendingDestinations);
        undoButton.Disabled = redoButton.Disabled = Active == null || !Session.Operations.CanMutate;
        if (Active is EditorApp content && content.Status != null)
            status.Text = content.Status.Text;
        databaseLabel.Text = Session.Connected ? "●  " + Session.DatabaseName : "●  DISCONNECTED";
        databaseLabel.Modulate = Session.Connected ? LibreKO.UiTheme.Good : Ui.Muted;
    }

    public override void _Input(InputEvent e)
    {
        if (Session == null || Session.IsClosing || e is not InputEventKey { Pressed: true, Echo: false } key)
            return;
        if(key.Keycode==Key.F1){GetViewport().SetInputAsHandled();HelpView.Show(Session);return;}
        if (key.AltPressed && key.Keycode is Key.Left or Key.Right)
        { GetViewport().SetInputAsHandled(); _ = NavigateHistoryAsync(key.Keycode == Key.Right); return; }
        if (key.Keycode == Key.Escape && !Session.Operations.CanMutate)
        {
            Session.Cancel();
            return;
        }
        if(NavigationLocksInput)return;
        if(Session.Operations.CanMutate&&Active is WorldWorkspace world)
        {
            if(key.Keycode==Key.F11){GetViewport().SetInputAsHandled();world.ToggleMapFullscreen();return;}
            if(key.Keycode==Key.Escape&&world.ExitMapExpansion()){GetViewport().SetInputAsHandled();return;}
        }

        if (!Session.Operations.CanMutate || !key.CtrlPressed || Active == null)
            return;
        if (key.Keycode == Key.S)
        {
            GetViewport().SetInputAsHandled();
            _ = Active.SaveAsync();
        }

        if (key.Keycode == Key.Z)
        {
            GetViewport().SetInputAsHandled();
            Active.Undo(key.ShiftPressed);
        }

        if (key.Keycode == Key.Y)
        {
            GetViewport().SetInputAsHandled();
            Active.Undo(true);
        }

        if (key.Keycode == Key.F)
        {
            GetViewport().SetInputAsHandled();
            Active.FocusSearch();
        }
    }

    public override async void _Notification(int what)
    {
        if (what != NotificationWMCloseRequest || Session == null || Session.IsClosing || navigating || !Session.Operations.CanMutate)
            return;
        navigating = true;
        try
        {
            if (await CanLeaveAsync())
                await QuitAsync();
        }
        finally
        {
            navigating = false;
        }
    }

    public async Task QuitAsync(int code = 0)
    {
        if (code != 0)
            exitCode = code;
        if (Session.IsClosing)
            return;
        Session.IsClosing = true;
        foreach (var workspace in Workspaces.Values)
            workspace.SetActive(false);
        var tree = GetTree();
        GetViewport().GuiDisableInput = true;
        await LibreKO.Shutdown.Begin(this, 200);
        tree.Quit(exitCode);
    }

    public override void _ExitTree() => Session?.Dispose();
}
