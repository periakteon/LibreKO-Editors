using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.References;

public class NamedReferenceTests
{
    [Fact]
    public void SpawnOffsetAndMonsterFirstAreExplicitConsumerRules()
    {
        Assert.True(ReferenceRules.ForSpawn(880009, 0).IsMonster);
        Assert.True(ReferenceRules.ForSpawn(880009, 99).IsMonster);
        Assert.False(ReferenceRules.ForSpawn(880009, 100).IsMonster);
        NamedReference[] candidates = [new(new("npcs", 880009, IsMonster: false), "Merchant", "Database", "ambiguous"),
            new(new("npcs", 880009, IsMonster: true), "Monster", "Database", "ambiguous")];
        Assert.Equal("Monster", ReferenceRules.ForMonsterFirst(880009, candidates)!.Label);
        Assert.Null(ReferenceRules.ForMonsterFirst(1, candidates));
    }

    [Fact]
    public void LabelsKeepIdsTypeAndMissingStateWithoutInventingNames()
    {
        Assert.Equal("880009 · Merchant · NPC", ReferenceLabels.Display(new(new("npcs", 880009, IsMonster: false), "Merchant", "Database", "resolved")));
        Assert.Equal("880009 · Missing reference · Monster", ReferenceLabels.Display(new(new("npcs", 880009, IsMonster: true), "", "Database", "missing")));
        Assert.Equal("71 · Lookup unavailable", ReferenceLabels.Display(new(new("zones", 71), "", "Database", "unavailable")));
        Assert.Equal("77 · Unknown code", ReferenceLabels.Enum(77, new Dictionary<string, string>()));
        Assert.Equal("900000000 · Noah (currency)", ReferenceLabels.Display(ReferenceRules.Special(new("items", 900000000))!));
        Assert.Equal("Experience (virtual reward)", ReferenceRules.Special(new("items", 900001000))?.Label);
        Assert.Equal("National Points (virtual reward)", ReferenceRules.Special(new("items", 900003000))?.Label);
        Assert.Equal("items", ReferenceRules.ForDrop(123450001)!.Kind);
        Assert.Equal("drop-pools", ReferenceRules.ForDrop(140)!.Kind);
        Assert.Null(ReferenceRules.ForDrop(20));
        Assert.Null(ReferenceRules.Special(new("zones", 0)));
    }
}
