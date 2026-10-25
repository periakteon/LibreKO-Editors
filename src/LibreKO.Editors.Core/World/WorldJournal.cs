using System.Text.Json;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.World;
public sealed class WorldJournal(string databaseHistoryRoot) : IWorldJournal
{
    readonly string root = Path.Combine(databaseHistoryRoot, "world");
    readonly string scope = Path.GetFullPath(databaseHistoryRoot);
    string FileFor(Guid id, string state) => Path.Combine(root, id + "." + state + ".json");
    public FileStream Acquire()
    { Directory.CreateDirectory(root); return new FileStream(Path.Combine(root, "writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
    static void WriteNew(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid() + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        { JsonSerializer.Serialize(stream, value, value.GetType(), JsonConfig.Options); stream.Flush(true); }
        File.Move(temporary, path, false);
    }
    public void Prepare(WorldChange change)
    {
        WriteNew(FileFor(change.OperationId, "reserved"), new { change.OperationId });
        WriteNew(FileFor(change.OperationId, "pending"), change);
    }
    public void Commit(WorldChange change, IReadOnlyList<WorldRowSnapshot> after) => WriteNew(FileFor(change.OperationId, "committed"), new WorldHistoryEntry(change, after, scope));
    public void Abort(Guid id) => WriteNew(FileFor(id, "aborted"), new { operationId = id });
    public IReadOnlyList<WorldChange> Pending() => !Directory.Exists(root) ? [] : Directory.GetFiles(root, "*.pending.json")
        .Select(p => JsonSerializer.Deserialize<WorldChange>(File.ReadAllText(p), JsonConfig.Options)!)
        .Where(c => !File.Exists(FileFor(c.OperationId, "committed")) && !File.Exists(FileFor(c.OperationId, "aborted"))).ToArray();
    public (WorldChange Change, IReadOnlyList<WorldRowSnapshot> After) Read(Guid id)
    {
        var entry = JsonSerializer.Deserialize<WorldHistoryEntry>(File.ReadAllText(FileFor(id, "committed")), JsonConfig.Options) ?? throw new InvalidDataException("Invalid world history entry.");
        if (entry.ScopeId != scope || entry.Change.OperationId != id) throw new InvalidDataException("World history belongs to a different database scope or operation.");
        return (entry.Change, entry.After);
    }
    public IReadOnlyList<OperationLink> List()
    {
        OperationLink Link(WorldChange change, bool pending, string label) => new("World", change.OperationId, scope, label, pending,
            change.Rows.Select(r => new NavigationTarget("world", r.Key.Table.ToString(), new() { ["id"] = r.Key.Id, ["zone"] = r.Key.Zone, ["isMonster"] = r.Key.IsMonster }, scope)).ToArray());
        if (!Directory.Exists(root)) return [];
        var committed = Directory.GetFiles(root, "*.committed.json").Select(path =>
        {
            var id = Guid.Parse(Path.GetFileName(path).Split('.')[0]); var entry = Read(id);
            return Link(entry.Change, false, File.GetLastWriteTimeUtc(path).ToString("u") + " · " + entry.Change.Rows.Count + " world row changes");
        });
        return Pending().Select(c => Link(c, true, "Pending · reconcile world outcome")).Concat(committed.OrderByDescending(x => x.Label, StringComparer.Ordinal)).ToArray();
    }
}
