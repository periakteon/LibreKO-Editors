using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Events;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Editors.DataHost.Events;
public sealed partial class EventDatabase(string connectionString, string databaseHistoryRoot, IEventJournal? journal = null)
{
    readonly IEventJournal eventJournal = journal ?? new EventJournal(databaseHistoryRoot);
    public string HistoryRoot { get; } = databaseHistoryRoot;

    internal AppDbContext Open() => new(new DbContextOptionsBuilder<AppDbContext>().UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)).Options);
    public EventCatalog Catalog() => EventSchema.Catalog();
    static void Require(EventCompatibilityReport report, IEnumerable<EventTable> tables, bool write = false)
    {
        foreach (var table in tables)
        {
            var state = report.Tables.Single(t => t.Table == table);
            if (write ? !state.CanSave : !state.CanBrowse)
                throw new InvalidOperationException("Unsupported event database structure: " + table + " · " + state.Reason);
        }
    }

    static string DocumentVersion(IEnumerable<EventRowSnapshot> rows) => RecordFingerprint.Compute(new() { ["rows"] = new System.Text.Json.Nodes.JsonArray(rows.OrderBy(r => Core.Events.EventKeys.Order(r.Key), StringComparer.Ordinal).Select(r => (System.Text.Json.Nodes.JsonNode? )new System.Text.Json.Nodes.JsonObject { ["key"] = Core.Events.EventKeys.Order(r.Key), ["version"] = r.Version }).ToArray()) });
}
