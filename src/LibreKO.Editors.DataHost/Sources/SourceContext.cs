using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.References;
using LibreKO.Editors.Core.Sources;

namespace LibreKO.Editors.DataHost.Sources;

// Read-only source names. Format authoring/validation is owned by the source codecs.
public sealed class SourceContext
{
    SourceProfile? profile;
    SourceStore? store;
    readonly Dictionary<string, IReadOnlyList<NamedReference>> cache = [];
    readonly Dictionary<SourceRoot, string> rootIdentities = [];
    readonly Dictionary<SourceRoot, string> rootErrors = [];
    public SourceProfile? Profile => profile;
    public SourceStore? Store => store;
    public void Clear() { profile = null; store = null; cache.Clear(); rootIdentities.Clear(); rootErrors.Clear(); }

    public void Configure(SourceProfile selected, string workspaceRoot)
    {
        var next = new SourceStore(selected, workspaceRoot);
        var identities = new Dictionary<SourceRoot, string>();
        var errors = new Dictionary<SourceRoot, string>();
        foreach (var root in selected.Roots)
        {
            try { identities[root.Key] = SourceIdentity.Root(root.Value); }
            catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
            { errors[root.Key] = e.Message; }
        }
        profile = selected;
        store = next;
        cache.Clear(); rootIdentities.Clear(); rootErrors.Clear();
        foreach (var pair in identities) rootIdentities[pair.Key] = pair.Value;
        foreach (var pair in errors) rootErrors[pair.Key] = pair.Value;
    }

    public Task<Page<NamedReference>> LookupNamedAsync(NamedReferenceQuery query)
    {
        ReferenceRules.Validate(new(query.Kind, 0, query.Zone, query.IsMonster));
        var page = Math.Clamp(query.Page, 0, 1_000_000);
        var size = Math.Clamp(query.Size, 1, 100);
        if (!Available(query.SourceProfileId)) return Task.FromResult(new Page<NamedReference>([], 0, page, size));
        var rows = Get(query.Kind).Where(x => query.IsMonster == null || x.Key.IsMonster == query.IsMonster)
            .Where(x => query.Zone == null || x.Key.Zone == query.Zone);
        var text = query.Text.Trim();
        if (text != "") rows = int.TryParse(text, out var id) ? rows.Where(x => x.Key.Id == id)
            : rows.Where(x => x.Label.Contains(text, StringComparison.OrdinalIgnoreCase));
        var found = rows.OrderBy(x => x.Key.Id).ThenBy(x => x.Key.IsMonster).ToArray();
        return Task.FromResult(new Page<NamedReference>(found.Skip(page * size).Take(size).ToArray(), found.Length, page, size));
    }

    public Task<IReadOnlyList<NamedReference>> ResolveNamedAsync(NamedReferenceResolve query)
    {
        if (query.Keys.Count > 100) throw new InvalidOperationException("Resolve up to 100 typed references at once.");
        var found = new List<NamedReference>();
        foreach (var key in query.Keys.Distinct())
        {
            ReferenceRules.Validate(key);
            if (ReferenceRules.Special(key) is { } special) { found.Add(special); continue; }
            if (!Available(query.SourceProfileId)) { found.Add(new(key, "", "Source files", "unavailable")); continue; }
            try
            {
                var matches = Get(key.Kind).Where(x => x.Key.Id == key.Id && (key.IsMonster == null || x.Key.IsMonster == key.IsMonster)
                    && (key.Zone == null || x.Key.Zone == key.Zone)).ToArray();
                if (matches.Length == 0) found.Add(new(key, "", "Source files · " + profile!.Label, "missing"));
                else found.AddRange(matches.Select(x => matches.Length > 1 ? x with { State = "ambiguous" } : x));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or JsonException or FormatException or OverflowException)
            { found.Add(new(key, e.Message, "Source files · " + profile!.Label, "unavailable")); }
        }
        return Task.FromResult<IReadOnlyList<NamedReference>>(found);
    }

    bool Available(string? requested) => profile != null && (requested == null || requested == profile.Id);

    IReadOnlyList<NamedReference> Get(string kind)
    {
        var root = kind == "models" ? SourceRoot.ClientProject : kind == "quests" ? SourceRoot.ServerQuests : SourceRoot.ServerData;
        if (!profile!.Roots.ContainsKey(root)) throw new InvalidOperationException("This source root is not configured.");
        if (!rootIdentities.TryGetValue(root, out var identity)) throw new InvalidOperationException(rootErrors.GetValueOrDefault(root, "Source root is unavailable."));
        if (SourceIdentity.Root(profile.Roots[root]) != identity)
            throw new InvalidOperationException("Source root identity changed; select and rescan the source profile.");
        if (cache.TryGetValue(kind, out var existing)) return existing;
        IReadOnlyList<NamedReference> rows = kind switch
        {
            "models" => Models(),
            "quests" => Quests(),
            "shops" => Shops(),
            "drop-groups" => Drops(),
            "drop-pools" => Rows("MakeItemGroups").Select(r => Named(kind, Int(r.Row, "GroupNum"),
                $"Drop pool · {Text(r.Row, "Items").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Length} entries", r)).ToArray(),
            _ => Simple(kind)
        };
        if (cache.Sum(x => x.Value.Count) + rows.Count > 1_000_000) cache.Clear();
        cache[kind] = rows;
        return rows;
    }

    IReadOnlyList<NamedReference> Simple(string kind)
    {
        var (stem, id, name) = kind switch
        {
            "items" => ("Items", "Num", "Name"), "npcs" => ("Npcs", "Id", "Name"),
            "zones" => ("ZoneInfos", "ZoneNo", "MapName"), "magic" => ("Magic", "Id", "EnName"),
            "event-races" => ("CollectionRaces", "Id", "Name"), "event-lotteries" => ("LotteryEvents", "Id", "Name"),
            "summons" => ("MonsterSummons", "Index", "Name"),
            "exchange-services" => ("ItemExchangeExps", "Index", ""),
            "mining-exchanges" => ("MiningExchanges", "Index", ""),
            "mining-fishing" => ("MiningFishingItems", "Index", ""),
            "world-events" => ("GameEvents", "EventNum", ""),
            _ => throw new InvalidOperationException("This reference kind has no supported file-name adapter.")
        };
        return Rows(stem).Select(row => Named(kind, Int(row.Row, id), name == "" ? stem + " row" : Text(row.Row, name), row,
            monster: kind == "npcs" ? Bool(row.Row, "IsMonster") : null,
            zone: kind == "world-events" ? Int(row.Row, "ZoneNum") : null)).ToArray();
    }

    IReadOnlyList<NamedReference> Drops()
    {
        var users = Get("npcs");
        return Rows("NpcItems").Select(row =>
        {
            var id = Int(row.Row, "Index"); var monster = Bool(row.Row, "IsMonster");
            var matching = users.Where(x => x.Key.IsMonster == monster && x.Details?["ItemGroup"]?.ToString() == id.ToString(CultureInfo.InvariantCulture)).ToArray();
            var label = $"Drop group · {matching.Length} users";
            if (matching.Length > 0) label += " · " + string.Join(", ", matching.Take(4).Select(x => x.Key.Id + " " + x.Label));
            return Named("drop-groups", id, label, row, monster);
        }).ToArray();
    }

    IReadOnlyList<NamedReference> Shops() => Rows("ItemSells").GroupBy(x => Int(x.Row, "SellingGroup")).Select(group =>
    {
        var users = Get("npcs").Where(x => x.Details?["SellingGroup"]?.ToString() == group.Key.ToString(CultureInfo.InvariantCulture)).ToArray();
        return Named("shops", group.Key, $"Shop group · {group.Count()} entries · {users.Length} users" +
            (users.Length > 0 ? " · " + string.Join(", ", users.Take(4).Select(x => x.Key.Id + " " + x.Label)) : ""), group.First());
    }).ToArray();

    IReadOnlyList<NamedReference> Models()
    {
        var key = Key(SourceRoot.ClientProject, "assets/npcs/index.json");
        var document = ReadJson(key).AsObject();
        if (document.Count > 1_000_000) throw new InvalidDataException("Too many model mappings.");
        return document.Where(x=>int.TryParse(x.Key,NumberStyles.Integer,CultureInfo.InvariantCulture,out _)).Select(x => new NamedReference(new("models", int.Parse(x.Key, CultureInfo.InvariantCulture)), x.Value?.ToString() ?? "",
            "Client file · " + profile!.Label + " · " + key.RelativePath, "resolved", SourceTarget(key))).ToArray();
    }

    IReadOnlyList<NamedReference> Quests()
    {
        var manifest = ReadJson(Key(SourceRoot.ServerQuests, "quest-manifest.json")).AsArray();
        if (manifest.Count > 100_000) throw new InvalidDataException("Too many quest source files.");
        var result = new List<NamedReference>();
        foreach (var entry in manifest)
        {
            var path = entry?.GetValue<string>() ?? throw new InvalidDataException("Invalid quest manifest path.");
            var key = Key(SourceRoot.ServerQuests, path);
            var text = ReadText(key);
            foreach (Match match in Regex.Matches(text, "^Quest\\s+(?<id>[0-9]+)\\s+\"(?<name>[^\"\\r\\n]*)\"", RegexOptions.Multiline, TimeSpan.FromSeconds(1)))
                result.Add(new(new("quests", int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture)), match.Groups["name"].Value,
                    "Server file · " + profile!.Label + " · " + path, "resolved", SourceTarget(key)));
        }
        return result;
    }

    NamedReference Named(string kind, int id, string label, SourceRow row, bool? monster = null, int? zone = null)
    {
        var details = new JsonObject { ["sourceFile"] = row.Path, ["sourceRow"] = row.Index };
        foreach (var name in new[] { "ItemGroup", "SellingGroup", "ModelId", "IconId", "Num", "Name" })
            if (Property(row.Row, name) is { } value) details[name] = value.DeepClone();
        return new(new(kind, id, zone, monster), label, "Server file · " + profile!.Label + " · " + row.Path, "resolved",
            SourceTarget(Key(SourceRoot.ServerData, row.Path), row.Index), details);
    }

    IEnumerable<SourceRow> Rows(string stem)
    {
        var root = profile!.Roots[SourceRoot.ServerData];
        var files = stem == "Items" ? Directory.GetFiles(root, "Items.slot*.json") : [];
        if (files.Length == 0) files = [Path.Combine(root, stem + ".json")];
        Array.Sort(files, StringComparer.OrdinalIgnoreCase);
        var count = 0;
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var key = Key(SourceRoot.ServerData, relative);
            if (!File.Exists(SourcePaths.Resolve(profile, key, false))) continue;
            var array = ReadJson(key).AsArray();
            for (var i = 0; i < array.Count; i++)
            {
                if (++count > 1_000_000) throw new InvalidDataException("Source name rows exceed one million.");
                yield return new(array[i]?.AsObject() ?? throw new InvalidDataException("Invalid source row."), relative, i);
            }
        }
    }

    SourceFileKey Key(SourceRoot root, string relative) => new(profile!.Id, root, rootIdentities[root], relative);
    NavigationTarget SourceTarget(SourceFileKey key, int? row = null)
    {
        var target = JsonSerializer.SerializeToNode(key, JsonConfig.Options)!.AsObject();
        if (row != null) target["row"] = row.Value;
        return new("world", "source", target, profile!.Id);
    }
    JsonNode ReadJson(SourceFileKey key) => JsonNode.Parse(ReadText(key), documentOptions: new() { MaxDepth = 64 })
        ?? throw new InvalidDataException("Empty JSON source.");
    string ReadText(SourceFileKey key)
    {
        var snapshot = store!.Read(key);
        if (snapshot.Content == null) throw new FileNotFoundException("Source name file is missing: " + key.RelativePath);
        using var stream = new MemoryStream(store.Blobs.Read(snapshot.Content));
        using var reader = new StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
    static JsonNode? Property(JsonObject row, string name) => row.TryGetPropertyValue(name, out var value) ? value
        : row.FirstOrDefault(x => x.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;
    static string Text(JsonObject row, string name) => Property(row, name)?.ToString() ?? "";
    static int Int(JsonObject row, string name) => int.Parse(Text(row, name), CultureInfo.InvariantCulture);
    static bool Bool(JsonObject row, string name) => Text(row, name).ToLowerInvariant() switch
    { "true" or "1" => true, "false" or "0" => false, _ => throw new InvalidDataException("Invalid source NPC/monster type discriminator.") };
    sealed record SourceRow(JsonObject Row, string Path, int Index);
}
