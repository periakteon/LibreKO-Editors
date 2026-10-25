using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Desktop;

public sealed class ReferenceCache
{
    readonly Func<IEditorDataClient> client;
    readonly Func<string> scope;
    readonly Func<ReferenceIdentity, NamedReference?>? fallback;
    readonly Func<NamedReferenceResolve, Task>? prepare;
    readonly object sync = new();
    readonly Dictionary<RequestKey, IReadOnlyList<NamedReference>> cache = [];
    readonly Dictionary<RequestKey, TaskCompletionSource<IReadOnlyList<NamedReference>>> requests = [];
    readonly List<RequestKey> pending = [];
    bool scheduled;
    long epoch;
    public string Scope => scope() + "/" + epoch;

    public ReferenceCache(EditorSession session) : this(() => session.Client,
        () => $"{session.Generation}/{session.HistoryRoot}/{session.Connected}/{session.Client.IsClosed}/{session.Sources.Generation}", key => PackedFallback(session, key),
        request => request.SourceProfileId != null || request.Keys.Any(k => k.Kind is "models" or "shops" or "quests" or "exchange-services" or "mining-exchanges" or "mining-fishing")
            ? session.Sources.EnsureRegisteredAsync() : Task.CompletedTask) { }
    public ReferenceCache(Func<IEditorDataClient> client, Func<string> scope, Func<ReferenceIdentity, NamedReference?>? fallback = null,
        Func<NamedReferenceResolve, Task>? prepare = null)
    { this.client = client; this.scope = scope; this.fallback = fallback; this.prepare = prepare; }
    public void Invalidate() { lock (sync) { epoch++; cache.Clear(); } }

    public async Task<IReadOnlyList<NamedReference>> ResolveAsync(ReferenceIdentity key, string? sourceProfileId, CancellationToken ct)
    {
        Task<IReadOnlyList<NamedReference>> task;
        lock (sync)
        {
            var request = new RequestKey(Scope, sourceProfileId, key);
            if (cache.TryGetValue(request, out var found)) return found;
            if (!requests.TryGetValue(request, out var completion))
            {
                if (requests.Count >= 4096) throw new InvalidOperationException("Too many pending name lookups.");
                completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                requests[request] = completion; pending.Add(request);
            }
            task = completion.Task;
            if (!scheduled) { scheduled = true; _ = FlushAsync(); }
        }
        // Abandoning a label must not cancel the shared helper's in-flight IPC frame.
        return await task.WaitAsync(ct);
    }

    public async Task<IReadOnlyList<NamedReference>> ResolveBatchAsync(IReadOnlyList<ReferenceIdentity> keys,
        string? sourceProfileId = null, CancellationToken ct = default)
    {
        var result = await Task.WhenAll(keys.Distinct().Select(k => ResolveAsync(k, sourceProfileId, ct)));
        return result.SelectMany(x => x).DistinctBy(x => (x.Key, x.Label, x.State)).ToArray();
    }

    async Task FlushAsync()
    {
        await Task.Yield();
        RequestKey[] batch;
        lock (sync) { batch = pending.ToArray(); pending.Clear(); scheduled = false; }
        foreach (var group in batch.GroupBy(x => (x.Scope, x.Profile)))
            foreach (var chunk in group.Chunk(100))
            {
                try
                {
                    if (group.Key.Scope != Scope) throw new OperationCanceledException("Name lookup scope changed.");
                    var requestPayload = new NamedReferenceResolve(chunk.Select(x => x.Key).ToArray(), group.Key.Profile);
                    if (prepare != null) await prepare(requestPayload);
                    if (group.Key.Scope != Scope) throw new OperationCanceledException("Name lookup scope changed.");
                    var rows = await client().SendAsync<NamedReference[]>("references.named.resolve", requestPayload);
                    lock (sync)
                    {
                        if (group.Key.Scope != Scope) throw new OperationCanceledException("Name lookup scope changed.");
                        if (cache.Count + chunk.Length > 4096) cache.Clear();
                        foreach (var request in chunk)
                        {
                            var found = rows.Where(x => x.Key.Kind == request.Key.Kind && x.Key.Id == request.Key.Id &&
                                x.Key.Zone == request.Key.Zone && (request.Key.IsMonster == null || x.Key.IsMonster == request.Key.IsMonster)).ToArray();
                            IReadOnlyList<NamedReference> result = found.Length == 0 ? [new(request.Key, "", "Lookup", "missing")] : found;
                            if (request.Profile == null && result.All(x => x.State is "missing" or "unavailable") && fallback?.Invoke(request.Key) is { } packed)
                                result = [packed];
                            cache[request] = result;
                            if (requests.Remove(request, out var completion)) completion.TrySetResult(result);
                        }
                    }
                }
                catch (Exception e)
                {
                    lock (sync)
                        foreach (var request in chunk)
                            if (requests.Remove(request, out var completion)) completion.TrySetException(e);
                }
            }
    }
    static NamedReference? PackedFallback(EditorSession session, ReferenceIdentity key)
    {
        if (key.Kind == "zones")
        {
            var zone = LibreKO.ZoneCatalog.All.FirstOrDefault(x => x.Id == key.Id);
            return zone.Id == key.Id && zone.Name != null ? new(key, zone.Name + " (client fallback)", "Bundled client catalog fallback", "fallback") : null;
        }
        if (key.Kind != "models" || !session.Shell.Assets.Available || !Godot.FileAccess.FileExists("res://assets/npcs/index.json")) return null;
        var stem = session.Shell.Assets.Json("assets/npcs/index.json")[key.Id.ToString()]?.ToString();
        return string.IsNullOrWhiteSpace(stem) ? null : new(key, stem + " (client fallback)", "Installed client fallback · assets/npcs/index.json", "fallback");
    }
    sealed record RequestKey(string Scope, string? Profile, ReferenceIdentity Key);
}
