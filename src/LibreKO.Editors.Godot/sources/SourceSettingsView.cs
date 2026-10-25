using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Sources;

namespace LibreKO.Editors.Desktop;

public static class SourceSettingsView
{
    public static void Show(EditorSession session, Action? closed = null)
    {
        var d = Ui.Add(session.Shell, new ConfirmationDialog { Title = "Source profiles", MinSize = new(960, 710),
            OkButtonText = "Use reviewed profile", CancelButtonText = "Close", DialogHideOnOk = false });
        var panel = Ui.Column(d); panel.Position = new(16, 40); panel.CustomMinimumSize = new(920, 620);
        Ui.Heading(panel, "AUTHORING SOURCES", 17);
        Ui.Label(panel, "Database, server files and client files have separate save outcomes.", 12, Ui.Muted);
        var profiles = session.Profile.Sources?.ToArray() ?? [];
        var picker = Ui.Add(panel, new OptionButton()); picker.AddItem("New source profile");
        foreach (var profile in profiles) picker.AddItem(profile.Label + " · " + profile.Id[..Math.Min(8, profile.Id.Length)]);
        var label = Ui.Field(panel, "Profile name", "A name for these local sources");
        var scroll = Ui.Add(panel, new ScrollContainer { CustomMinimumSize = new(920, 295), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled });
        var fields = Ui.Column(scroll); fields.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        var inputs = new Dictionary<SourceRoot, LineEdit>();
        foreach (var root in Enum.GetValues<SourceRoot>())
        {
            var row = Ui.Row(fields); var name = Ui.Label(row, Label(root), 12, Ui.Muted); name.CustomMinimumSize = new(175, 0);
            var input = Ui.Input(row, "Optional local directory"); input.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            inputs[root] = input;
            Ui.Button(row, "Browse…", () =>
            {
                var chooser = Ui.Add(d, new FileDialog { Title = Label(root), Access = FileDialog.AccessEnum.Filesystem,
                    FileMode = FileDialog.FileModeEnum.OpenDir, UseNativeDialog = true });
                chooser.CurrentDir = Directory.Exists(input.Text) ? input.Text : System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
                chooser.DirSelected += path => { input.Text = path; chooser.QueueFree(); };
                chooser.Canceled += () => chooser.QueueFree(); chooser.PopupCentered(new(900, 600));
            });
        }
        Ui.Heading(fields,"CLIENT BUILD TOOLS",12);
        var toolInputs=new Dictionary<string,LineEdit>();
        foreach(var title in new[]{"Godot Mono editor",".NET SDK executable","Windows Mono export template","Python (optional retail import)"})
        {
            var row=Ui.Row(fields);Ui.Label(row,title,12,Ui.Muted).CustomMinimumSize=new(225,0);var input=Ui.Input(row,"Optional absolute executable path");toolInputs[title]=input;
            Ui.Button(row,"Browse…",()=>{var chooser=Ui.Add(d,new FileDialog{Title=title,Access=FileDialog.AccessEnum.Filesystem,FileMode=FileDialog.FileModeEnum.OpenFile,UseNativeDialog=true,Filters=["*.exe ; Executable"]});chooser.FileSelected+=path=>{input.Text=path;chooser.QueueFree();};chooser.Canceled+=()=>chooser.QueueFree();chooser.PopupCenteredRatio(.75f);});
        }
        var actions = Ui.Row(panel);
        SourceProfile? selected = null, reviewed = null;
        Dictionary<SourceRoot, string?>? reviewedIdentities = null;
        string? Identity(string path) { try { return SourceIdentity.Root(path); } catch { return null; } }
        var newId = Guid.NewGuid().ToString("N");
        SourceProfile Candidate() => (selected ?? new SourceProfile(newId, "", new Dictionary<SourceRoot, string>())) with
        { Label = label.Text.Trim(), Roots = inputs.Where(x => !string.IsNullOrWhiteSpace(x.Value.Text)).ToDictionary(x => x.Key, x => Path.GetFullPath(x.Value.Text.Trim())),
          Tools=toolInputs.Values.All(x=>string.IsNullOrWhiteSpace(x.Text))?null:new AuthoringTools(Value("Godot Mono editor"),Value(".NET SDK executable"),Value("Python (optional retail import)"),Value("Windows Mono export template")) };
        string? Value(string title)=>string.IsNullOrWhiteSpace(toolInputs[title].Text)?null:Path.GetFullPath(toolInputs[title].Text.Trim());
        var report = Ui.Add(panel, new TextEdit { Editable = false, CustomMinimumSize = new(920, 150), WrapMode = TextEdit.LineWrappingMode.Boundary });
        void Load(int index)
        {
            selected = index == 0 ? null : profiles[index - 1]; newId = Guid.NewGuid().ToString("N"); reviewed = null;
            label.Text = selected?.Label ?? "Local LibreKO sources";
            foreach (var input in inputs) input.Value.Text = selected?.Roots.GetValueOrDefault(input.Key) ?? "";
            toolInputs["Godot Mono editor"].Text=selected?.Tools?.GodotEditor??"";toolInputs[".NET SDK executable"].Text=selected?.Tools?.DotnetSdk??"";toolInputs["Windows Mono export template"].Text=selected?.Tools?.ExportTemplate??"";toolInputs["Python (optional retail import)"].Text=selected?.Tools?.Python??"";
            report.Text = session.Sources.UnavailableReason ?? "Select existing directories, review their identities, then use the profile. No game file is changed by this dialog.";
        }
        picker.ItemSelected += index => Load((int)index);
        Ui.Button(actions, "Find local sources", () =>
        {
            foreach (var pair in Discover(session.Root)) if (inputs[pair.Key].Text == "") inputs[pair.Key].Text = pair.Value;
            reviewed = null; report.Text = "Detected directories are suggestions. Review the selected roots before using them.";
        });
        Ui.Button(actions,"Find build tools",()=>
        {
            string sdk=Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),"dotnet","dotnet.exe");string template=Path.Combine(session.Profile.AssetRoot,"knightonline.exe");string? engine=OS.HasFeature("editor")?OS.GetExecutablePath():null;
            if(engine==null){var downloads=Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),"Downloads");if(Directory.Exists(downloads))foreach(var folder in Directory.EnumerateDirectories(downloads,"Godot*")){engine=Directory.EnumerateFiles(folder,"Godot*_console.exe",SearchOption.TopDirectoryOnly).FirstOrDefault();if(engine!=null)break;}}
            foreach(var pair in new[]{("Godot Mono editor",engine),(".NET SDK executable",sdk),("Windows Mono export template",template)})if(toolInputs[pair.Item1].Text==""&&File.Exists(pair.Item2))toolInputs[pair.Item1].Text=pair.Item2;
            reviewed=null;report.Text="Detected tool paths are suggestions. Client builds require Godot4.7.2 Mono, a compatible .NET SDK and Windows Mono export template. Python is optional for retail imports. Ordinary editing remains available without build tools.";
        });
        Ui.Button(actions, "Review roots", () =>
        {
            try
            {
                var candidate = Candidate(); if (candidate.Label == "") throw new InvalidOperationException("Enter a profile name.");
                SourcePaths.Validate(candidate, session.Profile.WorkspaceRoot);
                string RootStatus(KeyValuePair<SourceRoot, string> root)
                {
                    try { return Label(root.Key) + " · " + root.Value + "\nIdentity " + SourceIdentity.Root(root.Value); }
                    catch (Exception e) { return Label(root.Key) + " · unavailable: " + e.Message; }
                }
                report.Text = string.Join("\n", candidate.Roots.Select(RootStatus)) +
                    "\n\nPipeline and KO inputs are read-only. Client writes require supported asset codecs. Builds use a separate output directory.";
                reviewed = candidate;
                reviewedIdentities = candidate.Roots.ToDictionary(x => x.Key, x => Identity(x.Value));
            }
            catch (Exception e) { reviewed = null; report.Text = e.Message; }
        });
        Ui.Button(actions, "File history", () => { d.Hide(); FileHistoryView.Show(session, () => { if (GodotObject.IsInstanceValid(d)) d.PopupCentered(); }); });
        void Close() { d.Hide(); d.QueueFree(); closed?.Invoke(); }
        async Task Use(SourceProfile? candidate)
        {
            d.Hide();
            if (!await session.Shell.CanLeaveAsync()) { d.PopupCentered(); return; }
            var ok = await session.RunAsync(OperationKind.Source, async ct =>
            {
                if (candidate != null && (reviewedIdentities == null || candidate.Roots.Any(x => Identity(x.Value) != reviewedIdentities.GetValueOrDefault(x.Key))))
                    throw new InvalidOperationException("A source root changed after review. Review its identity again before using it.");
                await session.Sources.SelectAsync(candidate, ct);
                var saved = session.Profile.Sources?.Where(x => x.Id != candidate?.Id).ToList() ?? [];
                if (candidate != null) saved.Add(candidate);
                session.Profile = session.Profile with { Sources = saved.Count == 0 ? null : saved, ActiveSourceId = candidate?.Id };
                ProfileStore.Save(session.ProfilePath, session.Profile);
            });
            if (ok) { Close(); if (session.Shell.Active != null) await session.Shell.Active.RefreshAsync(); }
            else if (GodotObject.IsInstanceValid(d)) d.PopupCentered();
        }
        Ui.Button(actions, "Disable file editing", () => { _ = Use(null); });
        d.Confirmed += () =>
        {
            try
            {
                if (reviewed == null || System.Text.Json.JsonSerializer.Serialize(Candidate(), JsonConfig.Options) != System.Text.Json.JsonSerializer.Serialize(reviewed, JsonConfig.Options))
                    throw new InvalidOperationException("Review the current roots before selecting this profile.");
                if (reviewedIdentities == null || reviewed.Roots.Any(x => Identity(x.Value) != reviewedIdentities.GetValueOrDefault(x.Key)))
                    throw new InvalidOperationException("A source root changed after review. Review its identity again before using it.");
                _ = Use(reviewed);
            }
            catch (Exception e) { report.Text = e.Message; }
        };
        d.Canceled += Close;
        var active = Array.FindIndex(profiles, x => x.Id == session.Profile.ActiveSourceId) + 1;
        picker.Select(active); Load(active); d.PopupCentered();
    }

    public static IReadOnlyDictionary<SourceRoot, string> Discover(string editorRoot)
    {
        var result = new Dictionary<SourceRoot, string>();
        for (var parent = new DirectoryInfo(editorRoot); parent != null; parent = parent.Parent)
        {
            var game = Directory.Exists(Path.Combine(parent.FullName, "LibreKO", "Client")) ? Path.Combine(parent.FullName, "LibreKO") : parent.FullName;
            foreach (var pair in new Dictionary<SourceRoot, string>
            {
                [SourceRoot.ServerData] = Path.Combine(game, "Server/LibreKO.Game/Seed/Data"),
                [SourceRoot.ServerMaps] = Path.Combine(game, "Server/LibreKO.Game/Map"),
                [SourceRoot.ServerQuests] = Path.Combine(game, "Server/LibreKO.Game/Quests"),
                [SourceRoot.ClientProject] = Path.Combine(game, "Client"),
                [SourceRoot.Pipeline] = Path.Combine(parent.FullName, "LibreKO-Assets")
            }) if (!result.ContainsKey(pair.Key) && Directory.Exists(pair.Value)) result[pair.Key] = pair.Value;
            if (result.Count == 5) break;
        }
        return result;
    }

    static string Label(SourceRoot root) => root switch
    { SourceRoot.ServerData => "Server JSON data", SourceRoot.ServerMaps => "Server SMD maps", SourceRoot.ServerQuests => "Server quest scripts",
        SourceRoot.ClientProject => "Client source project", SourceRoot.Pipeline => "Asset pipeline (read-only)", SourceRoot.KoInput => "KO input (read-only)", _ => "Separate build output" };
}
