using Godot;
using System.Data.Common;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;

namespace LibreKO.Editors.Desktop;
public partial class EditorShell
{
    internal void ShowConnectionDialog()
    {
        var d = Ui.Add(this, new ConfirmationDialog { Title = "Database connection", Exclusive = true, DialogHideOnOk = false, MinSize = new(620, 440), OkButtonText = "Connect" });
        var panel = Ui.Column(d);
        panel.Position = new(14, 38);
        panel.CustomMinimumSize = new(585, 0);
        Ui.Heading(panel, "MYSQL / MARIADB", 14);
        Ui.Label(panel, "Shared by every editor · credentials stay in memory", 12, Ui.Muted);
        var values = new DbConnectionStringBuilder();
        try
        {
            values.ConnectionString = Session.ConnectionString;
        }
        catch
        {
        }

        string Value(string fallback, params string[] names)
        {
            foreach (var name in names)
                if (values.TryGetValue(name, out var v))
                    return v.ToString() ?? fallback;
            return fallback;
        }

        var row = Ui.Row(panel);
        var host = Ui.Field(row, "Server", "Host", Value("127.0.0.1", "Server", "Host"));
        var port = Ui.Field(row, "Port", "Port", Value("3306", "Port"));
        var database = Ui.Field(panel, "Database", "Database", Value("", "Database"));
        var user = Ui.Field(panel, "User", "User", Value("", "User ID", "User", "Uid"));
        var pass = Ui.Field(panel, "Password", "Password", Value("", "Password", "Pwd"));
        pass.Secret = true;
        var advanced = Ui.Add(panel, new CheckButton { Text = "Advanced connection string" });
        var raw = Ui.Input(panel, "Connection string", Session.ConnectionString);
        raw.Secret = true;
        raw.Visible = false;
        advanced.Toggled += value => raw.Visible = value;
        var error = Ui.Label(panel, "", 12, Ui.Accent);
        d.Confirmed += async () =>
        {
            if (!Session.Operations.CanMutate)
                return;
            try
            {
                if (!uint.TryParse(port.Text, out var p) || p is < 1 or > 65535)
                    throw new InvalidOperationException("Enter a valid port.");
                var cs = advanced.ButtonPressed ? raw.Text : new DbConnectionStringBuilder
                {
                    ["Server"] = host.Text,
                    ["Port"] = p,
                    ["Database"] = database.Text,
                    ["User ID"] = user.Text,
                    ["Password"] = pass.Text,
                    ["Pooling"] = false
                }.ConnectionString;
                d.Hide();
                await ReconnectAsync(cs);
                if (Session.Connected)
                    d.QueueFree();
                else
                {
                    error.Text = "Connection failed. Check the details and try again.";
                    d.PopupCentered();
                }
            }
            catch (Exception e)
            {
                error.Text = e.Message;
                d.PopupCentered();
            }
        };
        d.Canceled += () => d.QueueFree();
        d.PopupCentered();
    }

    internal async Task ShowDockerAsync()
    {
        if (!await CanLeaveAsync())
            return;
        await Session.RunAsync(async () =>
        {
            Session.RestoreClient();
            var rows = await Session.Client.SendAsync<DockerDatabase[]>("docker.discover", new { });
            if (rows.Length == 0)
            {
                SetStatus("No running MySQL/MariaDB containers found.");
                return;
            }

            var d = Ui.Add(this, new AcceptDialog { Title = "Running Docker databases", MinSize = new(800, 220) });
            var content = Ui.Column(d);
            content.Position = new(14, 40);
            Ui.Label(content, "Select a running database · shared across editors", 13, Ui.Accent);
            foreach (var row in rows)
            {
                var selected = row;
                Ui.Button(content, row.Label, () => _ = Session.RunAsync(async () =>
                {
                    Session.Connected = false;
                    InvalidateWorkspaces();
                    var prepared = await Session.Client.SendAsync<DockerDatabase>("docker.prepare", new { containerId = selected.ContainerId });
                    Session.ConnectionString = prepared.ConnectionString;
                    d.QueueFree();
                    if (prepared.NeedsPassword)
                        ShowConnectionDialog();
                    else
                    {
                        await Session.ConnectAsync(prepared.ConnectionString);
                        if (Active != null)
                            await Active.RefreshAsync();
                    }
                }));
            }

            d.PopupCentered();
        });
    }

    internal void ShowSettings()
    {
        var d = Ui.Add(this, new AcceptDialog { Title = "Workspace settings", MinSize = new(780, 430) });
        var content = Ui.Column(d);
        content.Position = new(14, 40);
        content.CustomMinimumSize = new(740, 0);
        var asset = Ui.Field(content, "Installed client content", "Folder containing knightonline.pck", Session.Profile.AssetRoot);
        var zone = Ui.Field(content, "Server schedule timezone (display only)", "Unspecified · for example Europe/Berlin", Session.Profile.ServerScheduleTimeZone ?? "");
        Ui.Label(content, "Collection Race and Lottery schedules use UTC. Other event schedules use server time.", 12, Ui.Muted);
        Ui.Label(content, "Database history: " + Session.Profile.WorkspaceRoot, 12, Ui.Muted);
        Ui.Button(content, "Source profiles…", () => { d.Hide(); SourceSettingsView.Show(Session, () => { if (IsInstanceValid(d)) d.PopupCentered(); }); });
        Ui.Button(content, "Source file history…", () => { d.Hide(); FileHistoryView.Show(Session, () => { if (IsInstanceValid(d)) d.PopupCentered(); }); });
        Ui.Button(content, "Save settings and restart", async () =>
        {
            if (!await CanLeaveAsync())
                return;
            Session.Profile = Session.Profile with
            {
                AssetRoot = asset.Text,
                ServerScheduleTimeZone = string.IsNullOrWhiteSpace(zone.Text) ? null : zone.Text.Trim()
            };
            ProfileStore.Save(Session.ProfilePath, Session.Profile);
            var args = OS.HasFeature("editor") ? new[]
            {
                "--path",
                ProjectSettings.GlobalizePath("res://")
            }

            : Array.Empty<string>();
            if (OS.CreateProcess(OS.GetExecutablePath(), args) < 0)
            {
                SetStatus("Could not restart the editor.");
                return;
            }

            await QuitAsync();
        });
        d.PopupCentered();
    }

    internal void ShowReferencePicker(string kind, Action<ReferenceEntry> choose, Func<bool> current)
        => ReferencePicker.Show(this, Session, new(kind), entry => choose(new(entry.Key.Id, entry.Label, entry.Details)), current);
    internal void ShowEventCompatibility() => CompatibilityView.Show(Session, "events");
    internal void ShowHistoryAreas()
    {
        if (Active != null)
        {
            Active.ShowHistory();
            return;
        }

        var d = Ui.Add(this, new AcceptDialog { Title = "Choose history area", MinSize = new(540, 300) });
        var body = Ui.Column(d);
        body.Position = new(14, 38);
        Ui.Label(body, "Choose database history or the active source profile's file history.", 12, Ui.Muted);
        Ui.Button(body, "Content · items, characters and accounts", async () =>
        {
            d.QueueFree();
            await RequestWorkspaceAsync("content");
            Active?.ShowHistory();
        });
        Ui.Button(body, "Events · schedules, rewards and world data", () =>
        {
            d.QueueFree();
            _ = EventHistoryView.OpenAsync(Session);
        });
        Ui.Button(body, "Source files · apply and recovery", () => { d.Hide(); d.QueueFree(); FileHistoryView.Show(Session); });
        Ui.Button(body, "Map & NPC · database operations", () => { d.QueueFree(); _ = new HistoryRouter(Session).ShowAsync("world"); });
        d.PopupCentered();
    }
}
