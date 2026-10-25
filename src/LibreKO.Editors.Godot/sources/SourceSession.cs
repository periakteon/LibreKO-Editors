using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;

namespace LibreKO.Editors.Desktop;

public sealed class SourceSession : IDisposable
{
    readonly EditorSession session;
    CancellationTokenSource lifetime = new();
    readonly SemaphoreSlim registration = new(1);
    int registeredGeneration = -1;
    DataHostClient? registeredClient;
    bool disposed;
    public SourceProfile? Profile { get; private set; }
    public int Generation { get; private set; }
    public string? UnavailableReason { get; private set; }
    public SourceStore? Store { get; private set; }
    public BlobStore? Blobs => Store?.Blobs;
    public SourceValidationRegistry? Validators { get; private set; }
    public WorldCatalog? WorldCatalog { get; private set; }
    public SourceDraftRegistry? Drafts{get;private set;}
    public int Revision{get;private set;}
    internal Action<string,SourceFileKey>? ApplyCheckpoint;
    public bool HasUnsavedChanges=>Drafts?.IsDirty==true;
    public FileChange? CaptureDrafts()=>Drafts?.Capture();
    public FileHistory? History { get; private set; }
    public FileApply? Writer { get; private set; }
    public CancellationToken Cancellation => lifetime.Token;
    internal EditorSession Owner=>session;
    public SourcePreviewHost Previews{get;}

    public SourceSession(EditorSession session)
    {
        this.session = session;Previews=new(this);
        try
        {
            var profile = session.Profile.Sources?.SingleOrDefault(x => x.Id == session.Profile.ActiveSourceId);
            if (profile != null) Select(profile);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        { UnavailableReason = e.Message; }
    }

    // The shell guards dirty documents before selecting another profile.
    public void Select(SourceProfile? profile)
    {
        var store = profile == null ? null : new SourceStore(profile, session.Profile.WorkspaceRoot);
        Drafts?.Invalidate();
        Invalidate();
        Profile = profile;
        Store = store;
        Drafts=store==null?null:new(store);
        if(Drafts!=null)Drafts.Changed+=Previews.MarkChanged;
        Validators = store == null ? null : new(store.Blobs, profile!);
        WorldCatalog = null;
        if (store != null) ServerJsonCatalog.Register(Validators!, store.Blobs, () => WorldCatalog);
        if(store!=null)ServerJsonCatalog.RegisterIdentityValidation(Validators!,store,()=>WorldCatalog);
        if (store != null) LibreKO.Editors.Core.Maps.SmdValidation.Register(Validators!, store.Blobs);
        if(store!=null)LibreKO.Editors.Core.Client.ClientTerrainCatalog.Register(Validators!,store.Blobs);
        if(store!=null)LibreKO.Editors.Core.Client.ClientMapCatalog.Register(Validators!,store.Blobs,store);
        if(store!=null)LibreKO.Editors.Core.Client.NpcVisualCatalog.Register(Validators!,store);
        if(store!=null)
        {
            Validators!.Register("quest-source/1",QuestManifest.Accepts,t=>QuestManifest.ValidateText(t,store.Blobs));
            Validators.RegisterBatch("quest-graph/1",c=>c.Files.Any(f=>QuestManifest.Accepts(f.Before.Key)),async(c,ct)=>
            {
                var current=Generation;
                var audit=await session.Client.SendAsync<SourceValidationAudit>("sources.quest.audit",new{tickets=c.Files.Where(f=>f.After!=null).Select(f=>new SourceValidationTicket(f.Before.Key,f.After!)).ToArray(),deleted=c.Files.Where(f=>f.After==null).Select(f=>f.Before.Key).ToArray()},ct);
                if(current!=Generation)throw new OperationCanceledException("Source generation changed during compilation.");foreach(var dependency in audit.Dependencies)SourceReadScope.Record(dependency);return audit.Issues;
            });
        }
        History = profile == null ? null : new(profile, session.Profile.WorkspaceRoot);
        Writer = profile == null ? null : new(profile, session.Profile.WorkspaceRoot, Validators!);
        UnavailableReason = null;
    }

    public void Invalidate()
    {
        if (disposed) return;
        Previews.Invalidate();
        lifetime.Cancel(); lifetime.Dispose(); lifetime = new(); Generation++; registeredGeneration = -1;
        session.References?.Invalidate();
    }
    internal void ReportUnavailable(string message) => UnavailableReason = message;

    public async Task SelectAsync(SourceProfile? profile, CancellationToken ct = default)
    {
        Select(profile);
        session.RestoreClient();
        try { await EnsureRegisteredAsync(ct); }
        catch (Exception e) { UnavailableReason = e.Message; throw; }
    }

    public async Task EnsureRegisteredAsync(CancellationToken ct = default)
    {
        await registration.WaitAsync(ct);
        try
        {
            while (true)
            {
                if (disposed || session.IsClosing) throw new OperationCanceledException("Source session closed.");
                if (registeredGeneration == Generation && ReferenceEquals(registeredClient, session.Client) && !session.Client.IsClosed) return;
                var generation = Generation; var selected = Profile; var client = session.Client;
                if (client.IsClosed) throw new IOException("The helper is unavailable; reconnect or restore it to refresh source names.");
                if (selected == null) await client.SendAsync<System.Text.Json.JsonElement>("sources.clear", new { }, ct);
                else await client.SendAsync<System.Text.Json.JsonElement>("sources.configure", new { profile = selected, workspaceRoot = session.Profile.WorkspaceRoot }, ct);
                var catalog = await client.SendAsync<WorldCatalog>("world.catalog", new { }, ct);
                if (generation != Generation || !ReferenceEquals(client, session.Client)) continue;
                WorldCatalog = catalog;
                registeredGeneration = generation; registeredClient = client; return;
            }
        }
        finally { registration.Release(); }
    }

    public void Dispose() { if (disposed) return; disposed = true;Previews.Dispose(); Drafts?.Invalidate(); Generation++; lifetime.Cancel(); lifetime.Dispose(); }

    public async Task<FileApplyResult> ApplyAsync(FileChange change, CancellationToken ct = default)
    {
        if (Profile == null || Validators == null) throw new InvalidOperationException("Select a source profile first.");
        if(change.Files.Any(f=>QuestManifest.Accepts(f.Before.Key)))await EnsureRegisteredAsync(ct);
        var writer = ApplyCheckpoint==null?Writer!:new FileApply(Profile,session.Profile.WorkspaceRoot,Validators,ApplyCheckpoint); var generation = Generation;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Cancellation);
        var result = await Task.Run(() => writer.ApplyAsync(change, linked.Token));
        if(change.Files.Any(f=>QuestManifest.Accepts(f.Before.Key))&&session.Client.IsClosed)throw new IOException("Quest compiler helper closed. Source drafts are retained; restore the helper and review file history before retrying.");
        if (!disposed && generation == Generation && result.State is FileApplyState.Applied or FileApplyState.Partial or FileApplyState.Pending)
        {Revision++;registeredGeneration=-1;session.References.Invalidate();Drafts?.Acknowledge(change,result);}
        return result;
    }
}
