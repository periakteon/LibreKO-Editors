using Godot;

namespace LibreKO.Editors.Desktop;
public partial class EditorShell
{
    void BuildUi()
    {
        var bg = Ui.Add(this, new ColorRect { Color = Ui.Background, MouseFilter = MouseFilterEnum.Ignore });
        bg.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var margin = Ui.Add(this, new MarginContainer());
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        LibreKO.UiTheme.Margins(margin, 12, 10, 12, 8);
        layout = Ui.Column(margin);
        var frame = Ui.Add(layout, new PanelContainer());
        frame.AddThemeStyleboxOverride("panel", Ui.Surface(10));
        var chrome = Ui.Column(frame);
        var header = Ui.Row(chrome);
        header.AddChild(LibreKO.UiIcons.Image("system/combat-attack", new(22, 22), Ui.Accent));
        Ui.Heading(header, "LIBREKO", 20).AddThemeColorOverride("font_color", LibreKO.UiTheme.GoldBright);
        backButton = Ui.Button(header, "‹", () => _ = NavigateHistoryAsync(false));
        backButton.TooltipText = "Back to the previous screen · Alt+Left";
        forwardButton = Ui.Button(header, "›", () => _ = NavigateHistoryAsync(true));
        forwardButton.TooltipText = "Forward to the next screen · Alt+Right";
        switcher = Ui.Add(header, new OptionButton { CustomMinimumSize = new(170, 30) });
        switcher.AddItem("Editor home");
        switcher.AddItem("Content editor");
        switcher.AddItem("Events editor");
        switcher.AddItem("Map & NPC editor");
        switcher.ItemSelected += i => _ = RequestWorkspaceAsync(i == 0 ? null : WorkspaceRegistry.All[(int)i - 1].Id);
        databaseLabel = Ui.Label(header, "DISCONNECTED", 11, Ui.Muted);
        databaseLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        databaseLabel.CustomMinimumSize = new(85, 0);
        databaseLabel.ClipText = true;
        Ui.Add(header, new ActivityIndicator(Session.Loading, compact: true));
        var actions = Ui.Row(chrome);
        Ui.Button(actions, "Docker", () => _ = ShowDockerAsync());
        Ui.Button(actions, "Connection…", ShowConnectionDialog);
        Ui.Button(actions, "History", ShowHistoryAreas);
        Ui.Button(actions, "Compatibility", () => CompatibilityView.Show(Session, Active?.Id ?? ""));
        Ui.Button(actions, "Settings", ShowSettings);
        Ui.Button(actions,"Help",()=>HelpView.Show(Session)).TooltipText="Editing and step-by-step production guides · F1";
        Ui.Add(actions, new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
        undoButton = Ui.Button(actions, "Undo", () => Active?.Undo(false));
        redoButton = Ui.Button(actions, "Redo", () => Active?.Undo(true));
        saveButton = Ui.Button(actions, "Save to database", () =>
        {
            if (Active != null)
                _ = Active.SaveAsync();
        });
        Ui.Primary(saveButton);
        saveButton.Disabled = true;
        var body = Ui.Add(layout, new Control { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        home = Ui.Add(body, new MarginContainer());
        home.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        LibreKO.UiTheme.Margins((MarginContainer)home, 50, 55, 50, 30);
        var welcome = Ui.Column(home);
        Ui.Label(welcome, "WORKSPACE", 12, Ui.Accent);
        Ui.Heading(welcome, "LibreKO Editors", 36);
        Ui.Label(welcome, "Choose an editor to manage your game database.", 15, Ui.Muted);
        var cards = Ui.Add(welcome, new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        cards.AddThemeConstantOverride("h_separation", 16); cards.AddThemeConstantOverride("v_separation", 16);
        foreach (var editor in WorkspaceRegistry.All) AddHomeCard(cards, editor.Title, editor.Subtitle, editor.Description, editor.Id, editor.Icon);
        home.Resized += () => cards.Columns = home.Size.X < 960 ? 2 : 3;
        Ui.Add(welcome, new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        Ui.Label(welcome, "Your database connection is shared across editors. Changes save only when you choose Save to database.", 12, Ui.Muted);
        host = Ui.Add(body, new Control());
        host.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        host.MouseFilter = MouseFilterEnum.Ignore;
        status = Ui.Label(layout, "Select an editor · connect using Docker or Connection", 11, Ui.Muted);
        status.ClipText = true;
        GetTree().AutoAcceptQuit = false;
    }

    internal void AddHomeCard(Node parent, string title, string subtitle, string description, string id, string icon)
    {
        var panel = Ui.Add(parent, new PanelContainer { CustomMinimumSize = new(280, 255), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        panel.AddThemeStyleboxOverride("panel", Ui.Surface(24));
        var column = Ui.Column(panel);
        column.AddThemeConstantOverride("separation", 16);
        var emblem = Ui.Add(column, new PanelContainer { CustomMinimumSize = new(44, 44), SizeFlagsHorizontal = SizeFlags.ShrinkBegin });
        emblem.AddThemeStyleboxOverride("panel", Ui.Surface(8, true));
        Ui.Heading(emblem, id == "content" ? "DB" : id == "events" ? "EV" : "MAP", 16).AddThemeColorOverride("font_color", Ui.Accent);
        Ui.Heading(column, title, 24);
        Ui.Label(column, subtitle, 13, Ui.Accent).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Ui.Label(column, description, 13, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        var open = Ui.Button(column, "Open " + title, () => _ = RequestWorkspaceAsync(id));
        Ui.Primary(open);
    }
}
