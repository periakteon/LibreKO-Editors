using Godot;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Desktop;
public static class EventGalleries
{
    public static async Task Run(EditorShell shell, bool homeOnly)
    {
        if (!homeOnly && !shell.Session.Connected)
        {
            var databases = await shell.Session.Client.SendAsync<DockerDatabase[]>("docker.discover", new { });
            var selected = databases.First(d => d.Label.Contains("libreko-db"));
            var prepared = await shell.Session.Client.SendAsync<DockerDatabase>("docker.prepare", new { containerId = selected.ContainerId });
            await shell.Session.ConnectAsync(prepared.ConnectionString);
        }

        foreach (var size in new[]
        {
            new Vector2I(1500, 950),
            new Vector2I(1280, 800)
        }

        )
        {
            shell.GetWindow().Size = size;
            await shell.RequestWorkspaceAsync(null);
            await Capture(shell, "editor-home", size);
            if (homeOnly)
                continue;
            await shell.RequestWorkspaceAsync("events");
            var app = (EventsWorkspace)shell.Active!;
            foreach (var family in Enum.GetValues<EventFamily>())
            {
                await app.SelectFamilyAsync(family);
                if (family == EventFamily.Temple)
                    await app.SelectAsync(new(EventFamily.Temple, 5));
                await Capture(shell, "events-" + family.ToString().ToLowerInvariant(), size);
                if (family is EventFamily.CollectionRace or EventFamily.Temple)
                {
                    var tabs = app.FindChildren("*", "TabContainer", true, false).OfType<TabContainer>().FirstOrDefault();
                    if (tabs != null)
                        for (var i = 1; i < tabs.GetTabCount(); i++)
                        {
                            tabs.CurrentTab = i;
                            await Capture(shell, "events-" + family.ToString().ToLowerInvariant() + "-" + tabs.GetTabTitle(i).ToLowerInvariant(), size);
                        }
                }
            }
        }

        CompatibilityView.Show(shell.Session, "events");
        await Capture(shell, "events-compatibility", new(1280, 800));
        foreach (var d in shell.GetChildren().OfType<AcceptDialog>())
            d.QueueFree();
        await shell.ToSignal(shell.GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    internal static async Task Capture(EditorShell shell, string name, Vector2I size)
    {
        await shell.ToSignal(shell.GetTree().CreateTimer(.7), SceneTreeTimer.SignalName.Timeout);
        VerifyBounds(shell);
        if (DisplayServer.GetName() == "headless")
            return;
        var root = Path.Combine(shell.Session.Root, "artifacts");
        Directory.CreateDirectory(root);
        shell.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(root, name + (size.X == 1280 ? "-compact" : "") + ".png"));
    }

    static void VerifyBounds(EditorShell shell)
    {
        var bounds = shell.GetViewportRect();
        foreach (var button in shell.FindChildren("*", "Button", true, false).OfType<Button>().Where(b => b.IsVisibleInTree()))
        {
            bool scrolling = false;
            for (Node? parent = button.GetParent(); parent != null && parent != shell; parent = parent.GetParent())
                if (parent is ScrollContainer or Window)
                {
                    scrolling = true;
                    break;
                }

            if (scrolling)
                continue;
            var rect = button.GetGlobalRect();
            if (rect.Position.X < 0 || rect.End.X > bounds.End.X + 1 || rect.End.Y > bounds.End.Y + 1)
                throw new Exception("Clipped editor action: " + button.Text + " at " + bounds.Size);
        }
    }
}
