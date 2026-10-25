using System.Text;
using System.Text.Json;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Events;
public sealed class EventJournal(string databaseHistoryRoot) : IEventJournal
{
    readonly string root = Path.Combine(databaseHistoryRoot, "events");
    string FileFor(Guid id, string state) => Path.Combine(root, id + "." + state + ".json");
    static void WriteNew(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid() + ".tmp";
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, JsonConfig.Options));
            stream.Write(bytes);
            stream.Flush(true);
        }

        File.Move(temporary, path, false);
    }

    public void Prepare(EventChange change)
    {
        WriteNew(FileFor(change.OperationId, "reserved"), new { operationId = change.OperationId });
        WriteNew(FileFor(change.OperationId, "pending"), change);
    }

    public void Commit(EventChange change, EventDocumentSnapshot? after) => WriteNew(FileFor(change.OperationId, "committed"), new EventHistoryEntry(change, after));
    public void Abort(Guid id) => WriteNew(FileFor(id, "aborted"), new { operationId = id });
    bool Resolved(Guid id) => File.Exists(FileFor(id, "committed")) || File.Exists(FileFor(id, "aborted"));
    public IReadOnlyList<EventChange> Pending() => Directory.Exists(root) ? Directory.GetFiles(root, "*.pending.json").Select(p => JsonSerializer.Deserialize<EventChange>(File.ReadAllText(p), JsonConfig.Options)!).Where(c => !Resolved(c.OperationId)).ToArray() : [];
    public IReadOnlyList<EventHistorySummary> List()
    {
        if (!Directory.Exists(root))
            return[];
        var saved = Directory.GetFiles(root, "*.committed.json").Select(p =>
        {
            var entry = JsonSerializer.Deserialize<EventHistoryEntry>(File.ReadAllText(p), JsonConfig.Options)!;
            return new EventHistorySummary(entry.Change.OperationId, entry.Change.Before.Key, File.GetLastWriteTimeUtc(p).ToString("yyyy-MM-dd HH:mm:ss 'UTC'") + " · " + entry.Change.Rows.Count + " row changes", false);
        });
        return Pending().Select(c => new EventHistorySummary(c.OperationId, c.Before.Key, "Pending · reconcile outcome", true)).Concat(saved.OrderByDescending(s => s.Label, StringComparer.Ordinal)).ToArray();
    }

    public EventHistoryEntry Read(Guid id)
    {
        var path = FileFor(id, "committed");
        if (!File.Exists(path))
            throw new FileNotFoundException("Event history entry was not found for the selected database.");
        return JsonSerializer.Deserialize<EventHistoryEntry>(File.ReadAllText(path), JsonConfig.Options)!;
    }
}
