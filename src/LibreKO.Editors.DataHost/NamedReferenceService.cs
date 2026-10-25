using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.References;
using LibreKO.Editors.DataHost.Sources;

namespace LibreKO.Editors.DataHost;

public sealed class NamedReferenceService(EditorDatabase? database, SourceContext sources)
{
    static bool FileOnly(string kind) => kind is "models" or "shops" or "quests" or "exchange-services" or "mining-exchanges" or "mining-fishing";
    public Task<Page<NamedReference>> LookupNamedAsync(NamedReferenceQuery query)
    {
        if (query.SourceProfileId != null || FileOnly(query.Kind)) return sources.LookupNamedAsync(query);
        return database?.LookupNamedAsync(query) ?? Task.FromResult(new Page<NamedReference>([], 0, query.Page, Math.Clamp(query.Size, 1, 100)));
    }

    public async Task<IReadOnlyList<NamedReference>> ResolveNamedAsync(NamedReferenceResolve query)
    {
        if (query.Keys.Count > 100) throw new InvalidOperationException("Resolve up to 100 typed references at once.");
        var results = new List<NamedReference>();
        foreach (var group in query.Keys.GroupBy(x => query.SourceProfileId != null || FileOnly(x.Kind)))
        {
            var request = query with { Keys = group.ToArray() };
            if (group.Key) results.AddRange(await sources.ResolveNamedAsync(request));
            else if (database != null) results.AddRange(await database.ResolveNamedAsync(request));
            else foreach (var key in group)
            {
                ReferenceRules.Validate(key);
                results.Add(ReferenceRules.Special(key) ?? new(key, "", "Database", "unavailable"));
            }
        }
        return results;
    }
}
