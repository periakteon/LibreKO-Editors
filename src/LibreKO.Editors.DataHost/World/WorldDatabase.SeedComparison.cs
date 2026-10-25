using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;

namespace LibreKO.Editors.DataHost.World;

public sealed partial class WorldDatabase
{
    // Comparison has no journal, transaction or mutation side effects.
    public async Task<SeedMatch> CompareSeedAsync(WorldKey key,IReadOnlyList<JsonSourceDocument> documents)
    {
        var current=await GetAsync(key) ?? throw new InvalidOperationException("Database record no longer exists.");
        return SeedRowMatcher.Match(current,documents);
    }
}
