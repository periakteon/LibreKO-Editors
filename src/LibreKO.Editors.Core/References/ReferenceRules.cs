using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.References;

public static class ReferenceRules
{
    public static ReferenceIdentity ForSpawn(int id, byte actType) => new("npcs", id, IsMonster: actType < 100);
    public static ReferenceIdentity? ForDrop(int value) => value >= 100_000_000 ? new("items", value)
        : value >= 100 ? new("drop-pools", value) : null;

    public static NamedReference? ForMonsterFirst(int id, IReadOnlyList<NamedReference> candidates)
    {
        var matches = candidates.Where(x => x.Key.Kind == "npcs" && x.Key.Id == id && x.State is "resolved" or "ambiguous").ToArray();
        var monsters = matches.Where(x => x.Key.IsMonster == true).ToArray();
        if (monsters.Length != 0) return monsters.Length == 1 ? monsters[0] : null;
        var npcs = matches.Where(x => x.Key.IsMonster == false).ToArray();
        return npcs.Length == 1 ? npcs[0] : null;
    }

    public static NamedReference? Special(ReferenceIdentity key)
    {
        if (key.Kind != "items") return null;
        var label = key.Id switch { 0 => "None", 900_000_000 => "Noah (currency)", 900_001_000 => "Experience (virtual reward)",
            900_003_000 => "National Points (virtual reward)", _ => null };
        return label == null ? null : new(key, label, "Server constant", "resolved");
    }

    public static void Validate(ReferenceIdentity key)
    {
        if (key.IsMonster != null && key.Kind is not ("npcs" or "drop-groups"))
            throw new InvalidOperationException("This reference kind has no NPC/monster discriminator.");
        if (key.Zone != null && key.Kind is not ("bindpoints" or "world-events"))
            throw new InvalidOperationException("This reference kind has no zone key component.");
        if (key.Kind == "bindpoints" && key.Zone == null)
            throw new InvalidOperationException("Choose a zone before looking up bind points.");
    }
}
