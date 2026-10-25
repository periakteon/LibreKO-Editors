using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.World;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Editors.DataHost.World;

public sealed partial class WorldDatabase(string connectionString, string databaseHistoryRoot, IWorldJournal? journal = null)
{
    readonly IWorldJournal worldJournal = journal ?? new WorldJournal(databaseHistoryRoot);
    public string HistoryRoot { get; } = databaseHistoryRoot;
    internal AppDbContext Open() => new(new DbContextOptionsBuilder<AppDbContext>().UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)).Options);
    public WorldCatalog Catalog() => WorldSchema.Catalog();
    internal static void Require(WorldCompatibilityReport report, IEnumerable<WorldTable> tables, bool write = false)
    {
        foreach (var table in tables.Distinct())
        {
            var state = report.Tables.Single(t => t.Table == table);
            if (write ? !state.CanSave : !state.CanBrowse) throw new InvalidOperationException("Unsupported world database section: " + table + " · " + state.Reason);
        }
    }
    public async Task<WorldMembership> MembershipAsync(WorldKey owner, string relation)
    {
        await using var db = Open();
        return await WorldDependencies.CaptureAsync(db, owner, relation);
    }
}
