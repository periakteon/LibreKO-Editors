using Godot;
using System.Text.Json;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;

namespace LibreKO.Editors.Desktop;
public sealed class EditorSession : IDisposable
{
    public EditorShell Shell { get; }
    public string Root { get; }
    public string ProfilePath { get; }
    public WorkspaceProfile Profile { get; set; }
    public SourceSession Sources { get; }
    public ReferenceCache References { get; }
    public ReferencePresenter ReferencePresenter { get; }
    public DataHostClient Client { get; private set; } = null !;
    public OperationGate Operations { get; } = new();
    public ActivityTracker Loading { get; } = new();
    public bool ContentReportStale { get; private set; }
    public bool EventReportStale { get; private set; }
    public bool WorldReportStale { get; internal set; } = true;
    public bool Connected { get; set; }
    public bool IsClosing { get; internal set; }
    public int Generation { get; private set; }
    public string HistoryRoot { get; private set; } = "";
    public string ConnectionString { get; set; } = "";
    public string DatabaseName { get; private set; } = "";
    public EventCompatibilityReport? EventCompatibility { get; private set; }
    public WorldCompatibilityReport? WorldCompatibility { get; private set; }
    public DatabaseCompatibility? ContentCompatibility { get; private set; }

    CancellationTokenSource? operationCancel;
    public EditorSession(EditorShell shell)
    {
        Shell = shell;
        Root = System.Environment.GetEnvironmentVariable("LIBREKO_EDITOR_HOME") ?? (OS.HasFeature("editor") ? Path.GetFullPath(ProjectSettings.GlobalizePath("res://../../")) : OS.GetExecutablePath().GetBaseDir());
        ProfilePath = System.Environment.GetEnvironmentVariable("LIBREKO_EDITOR_PROFILE") ?? Path.Combine(Root, "profiles/local.json");
        Profile = ProfileStore.Load(ProfilePath) ?? new(Path.GetFullPath(Path.Combine(Root, "../LibreKO - Official Build with packs")), Path.Combine(Root, "workspace"));
        Sources = new(this);
        References = new(this);
        ReferencePresenter = new(References);
    }

    public async Task InitializeAsync()
    {
        RestoreClient();
        await Client.SendAsync<JsonElement>("ping", new { });
        try { await Sources.EnsureRegisteredAsync(); }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
        { Sources.ReportUnavailable(e.Message); }
    }

    public void RestoreClient()
    {
        if (Client != null && !Client.IsClosed)
            return;
        if (Client != null) { Connected = false; Generation++; Shell.InvalidateWorkspaces(); }
        var host = Path.Combine(Root, "data/LibreKO.Editors.DataHost.dll");
        if (!File.Exists(host))
            host = Path.Combine(Root, "src/LibreKO.Editors.DataHost/bin/Debug/net10.0/LibreKO.Editors.DataHost.dll");
        Client = new(host)
        {
            Loading = Loading,
            OperationCancellation = operationCancel?.Token ?? default
        };
    }

    public async Task ConnectAsync(string connectionString)
    {
        RestoreClient();
        Connected = false;
        ConnectionString = connectionString;
        Generation++;
        Shell.InvalidateWorkspaces();
        try
        {
            var state = await Client.SendAsync<ConnectionState>("connect", new { connectionString, workspaceRoot = Profile.WorkspaceRoot });
            HistoryRoot = state.HistoryRoot;
            WorldCompatibility = null; WorldReportStale = true;
            DatabaseName = state.Database;
            ContentCompatibility = state.Compatibility;
            try
            {
                EventCompatibility = await Client.SendAsync<EventCompatibilityReport>("events.compatibility", new { });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                if (Client.IsClosed)
                    throw;
                EventCompatibility = null;
            }

            Connected = true;
            ContentReportStale = EventReportStale = false;
        }
        catch
        {
            Connected = false;
            HistoryRoot = "";
            DatabaseName = "";
            ContentCompatibility = null;
            EventCompatibility = null;
            throw;
        }
        finally
        {
            Shell.InvalidateWorkspaces();
        }
    }

    public Task<bool> RunAsync(Func<Task> action) => RunAsync(OperationKind.Database, _ => action());

    public async Task<bool> RunAsync(OperationKind kind, Func<CancellationToken, Task> action)
    {
        if (IsClosing || !Operations.CanMutate)
            return false;
        using var lease = Operations.Enter();
        using var activity = Loading.Begin("operation", kind == OperationKind.Database ? "Loading database data…" : "Processing source files…");
        using var cancel = new CancellationTokenSource();
        operationCancel = cancel;
        Client.OperationCancellation = kind == OperationKind.Database ? cancel.Token : default;
        Shell.GetViewport().GuiDisableInput = true;
        try
        {
            Shell.SetStatus("Working… Esc to cancel");
            await action(cancel.Token);
            if(Shell.CurrentStatus=="Working… Esc to cancel")Shell.SetStatus("Ready");
            return true;
        }
        catch (OperationCanceledException)
        {
            if (kind == OperationKind.Database || Client.IsClosed)
            {
                Connected = false;
                Generation++;
                Shell.InvalidateWorkspaces();
                Shell.SetStatus("Operation interrupted · reconnect and check history before retrying.");
            }
            else Shell.SetStatus("Source operation interrupted · review file history before retrying.");
            return false;
        }
        catch (Exception e)
        {
            if (Client.IsClosed)
            {
                Connected = false;
                Generation++;
                Shell.InvalidateWorkspaces();
                Shell.SetStatus("Database helper disconnected. Reconnect and reconcile pending history before retrying. " + e.Message);
                return false;
            }

            Shell.SetStatus(e.Message);
            return false;
        }
        finally
        {
            if (Client != null && !Client.IsClosed)
                Client.OperationCancellation = default;
            operationCancel = null;
            if (GodotObject.IsInstanceValid(Shell) && !IsClosing)
                Shell.GetViewport().GuiDisableInput = Shell.NavigationLocksInput;
        }
    }

    public void NotifyDatabaseChange()
    {
        References.Invalidate();
        ContentReportStale = EventReportStale = WorldReportStale = true;
        if (Shell.Workspaces.GetValueOrDefault("content")is EditorApp content)
            content.InvalidateCompatibility();
    }

    public void AcceptContentCompatibility(DatabaseCompatibility report)
    {
        ContentCompatibility = report;
        ContentReportStale = false;
    }

    public async Task RefreshEventCompatibilityAsync()
    {
        EventCompatibility = await Client.SendAsync<EventCompatibilityReport>("events.compatibility", new { });
        EventReportStale = false;
    }
    public async Task RefreshWorldCompatibilityAsync()
    { WorldCompatibility = await Client.SendAsync<WorldCompatibilityReport>("world.compatibility", new { }); WorldReportStale = false; }

    public void Cancel() => operationCancel?.Cancel();
    public void Dispose()
    {
        Sources.Dispose();
        Client?.Dispose();
    }
}
