using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.References;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Editors.DataHost;

public sealed partial class EditorDatabase
{
    public Task<Page<NamedReference>> LookupNamedAsync(NamedReferenceQuery query) => LookupNamed(query);

    async Task<Page<NamedReference>> LookupNamed(NamedReferenceQuery query, IReadOnlyList<int>? ids = null)
    {
        ReferenceRules.Validate(new(query.Kind, 0, query.Zone, query.IsMonster));
        var page = Math.Clamp(query.Page, 0, 1_000_000);
        var size = Math.Clamp(query.Size, 1, 100);
        var text = query.Text.Trim();
        var numeric = int.TryParse(text, out var number);
        await using var db = Open();
        NamedReference Named(ReferenceIdentity key, string label, string? table = null, JsonObject? details = null)
            => new(key, label, "Database", "resolved", table == null ? null : new("world", table,
                new() { ["id"] = key.Id, ["isMonster"] = key.IsMonster, ["zone"] = key.Zone }, historyRoot), details);

        if (query.Kind == "npcs")
        {
            var rows = db.Npcs.AsNoTracking().AsQueryable();
            if (query.IsMonster is bool monster) rows = rows.Where(x => x.IsMonster == monster);
            if (ids != null) rows = rows.Where(x => ids.Contains(x.Id));
            else if (text != "") rows = numeric ? rows.Where(x => x.Id == number) : rows.Where(x => x.Name.Contains(text));
            var total = await rows.CountAsync();
            var found = await rows.OrderBy(x => x.Id).ThenBy(x => x.IsMonster).Skip(page * size).Take(size).Select(x => new { x.Id, x.IsMonster, x.Name }).ToArrayAsync();
            return new(found.Select(x => Named(new("npcs", x.Id, IsMonster: x.IsMonster), x.Name, "Npcs")).ToArray(), total, page, size);
        }
        if (query.Kind == "items")
        {
            var rows = db.Items.AsNoTracking().AsQueryable();
            if (ids != null) rows = rows.Where(x => ids.Contains(x.Num));
            else if (text != "") rows = numeric ? rows.Where(x => x.Num == number) : rows.Where(x => x.Name.Contains(text));
            var total = await rows.CountAsync();
            var found = await rows.OrderBy(x => x.Num).Skip(page * size).Take(size).Select(x => new { x.Num, x.Name }).ToArrayAsync();
            var details = new Dictionary<int, JsonObject>();
            if (found.Length > 0 && (await CheckCompatibilityAsync()).Modules.Single(x => x.Kind == RecordKind.Item).CanBrowse)
            {
                var keys = found.Select(x => x.Num).ToArray();
                foreach (var item in await db.Items.AsNoTracking().Where(x => keys.Contains(x.Num)).ToArrayAsync()) details[item.Num] = Snapshot(item).Values;
            }
            return new(found.Select(x => new NamedReference(new("items", x.Num), x.Name, "Database", "resolved",
                new("content", "items", new() { ["id"] = x.Num }, historyRoot), details.GetValueOrDefault(x.Num))).ToArray(), total, page, size);
        }
        if (query.Kind == "drop-groups")
        {
            var rows = db.NpcItems.AsNoTracking().AsQueryable();
            if (query.IsMonster is bool monster) rows = rows.Where(x => x.IsMonster == monster);
            if (ids != null) rows = rows.Where(x => ids.Contains(x.Index));
            else if (numeric) rows = rows.Where(x => x.Index == number);
            else if (text != "") rows = rows.Where(x => db.Npcs.Any(n => n.ItemGroup == x.Index && n.IsMonster == x.IsMonster && n.Name.Contains(text)));
            var total = await rows.CountAsync();
            var found = await rows.OrderBy(x => x.Index).ThenBy(x => x.IsMonster).Skip(page * size).Take(size).Select(x => new { x.Index, x.IsMonster }).ToArrayAsync();
            var groupIds = found.Select(x => x.Index).ToArray();
            var users = await db.Npcs.AsNoTracking().Where(x => groupIds.Contains(x.ItemGroup)).Select(x => new { x.Id, x.Name, x.ItemGroup, x.IsMonster }).ToArrayAsync();
            return new(found.Select(x =>
            {
                var matching = users.Where(n => n.ItemGroup == x.Index && n.IsMonster == x.IsMonster).ToArray();
                var summary = string.Join(", ", matching.Take(4).Select(n => $"{n.Id} {n.Name}"));
                return Named(new("drop-groups", x.Index, IsMonster: x.IsMonster), $"Drop group · {matching.Length} users" + (summary == "" ? "" : " · " + summary), "NpcItems");
            }).ToArray(), total, page, size);
        }
        if (query.Kind == "drop-pools")
        {
            var rows = db.MakeItemGroups.AsNoTracking().AsQueryable();
            if (ids != null) rows = rows.Where(x => ids.Contains(x.GroupNum));
            else if (numeric) rows = rows.Where(x => x.GroupNum == number);
            else if (text != "") rows = rows.Where(x => x.Items.Contains(text));
            var total = await rows.CountAsync();
            var found = await rows.OrderBy(x => x.GroupNum).Skip(page * size).Take(size).ToArrayAsync();
            return new(found.Select(x => Named(new("drop-pools", x.GroupNum), $"Drop pool · {x.GetItems().Length} entries", "MakeItemGroups")).ToArray(), total, page, size);
        }
        if (query.Kind == "summons")
        {
            var rows = db.MonsterSummons.AsNoTracking().AsQueryable();
            if (ids != null) rows = rows.Where(x => ids.Contains(x.Index));
            else if (numeric) rows = rows.Where(x => x.Index == number);
            else if (text != "") rows = rows.Where(x => x.Name.Contains(text));
            var total = await rows.CountAsync();
            var found = await rows.OrderBy(x => x.Index).Skip(page * size).Take(size).Select(x => new { x.Index, x.Name, x.Sid }).ToArrayAsync();
            return new(found.Select(x => Named(new("summons", x.Index), $"{x.Name} · prototype {x.Sid} (monster-first)", "MonsterSummons")).ToArray(), total, page, size);
        }
        var old = await LookupAsync(new(query.Kind, text, page, size, Context: query.Zone), ids);
        return new(old.Rows.Select(x =>
        {
            var key = new ReferenceIdentity(query.Kind, x.Id, query.Zone);
            var target = query.Kind is "items" or "accounts" ? new NavigationTarget("content", query.Kind, new() { ["id"] = x.Id }, historyRoot)
                : query.Kind == "zones" ? new NavigationTarget("world", "ZoneInfos", new() { ["id"] = x.Id }, historyRoot) : null;
            return new NamedReference(key, x.Label, "Database", "resolved", target, x.Details);
        }).ToArray(), old.Total, page, size);
    }

    public async Task<IReadOnlyList<NamedReference>> ResolveNamedAsync(NamedReferenceResolve query)
    {
        if (query.Keys.Count > 100) throw new InvalidOperationException("Resolve up to 100 typed references at once.");
        foreach (var key in query.Keys) ReferenceRules.Validate(key);
        var found = new List<NamedReference>();
        foreach (var group in query.Keys.Distinct().GroupBy(x => (x.Kind, x.Zone, x.IsMonster)))
        {
            var keys = group.ToArray();
            var normal = keys.Where(x => ReferenceRules.Special(x) == null).ToArray();
            var rows = new List<NamedReference>();
            if (normal.Length > 0)
            {
                var ids = normal.Select(x => x.Id).ToArray();
                for (var page = 0; ; page++)
                {
                    var batch = await LookupNamed(new(group.Key.Kind, Page: page, Size: 100, Zone: group.Key.Zone, IsMonster: group.Key.IsMonster), ids);
                    rows.AddRange(batch.Rows);
                    if ((page + 1) * 100 >= batch.Total) break;
                }
            }
            foreach (var key in keys)
            {
                if (ReferenceRules.Special(key) is { } special) { found.Add(special); continue; }
                var candidates = rows.Where(x => x.Key.Id == key.Id).ToArray();
                if (candidates.Length == 0) found.Add(new(key, "", "Database", "missing"));
                else found.AddRange(candidates.Select(x => candidates.Length > 1 ? x with { State = "ambiguous" } : x));
            }
        }
        return found.DistinctBy(x => (x.Key, x.Label, x.State)).ToArray();
    }
}
