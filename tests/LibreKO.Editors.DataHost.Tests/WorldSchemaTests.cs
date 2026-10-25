using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.DataHost.World;
using Microsoft.EntityFrameworkCore;

public class WorldSchemaTests
{
    [Fact]
    public void EveryPersistedMappedPropertyAndCompletePrimaryKeyIsExposed()
    {
        using var db = EventFixture.Open();
        var catalog = WorldSchema.Catalog();
        Assert.Equal(15, catalog.Tables.Count);
        foreach (var table in catalog.Tables)
        {
            var entity = db.Model.FindEntityType(WorldSchema.EntityType(table.Table))!;
            Assert.Equal(entity.GetProperties().Select(p => p.Name).Order(), table.Fields.Select(f => f.Field.Name).Order());
            Assert.Equal(entity.FindPrimaryKey()!.Properties.Select(p => p.Name), table.KeyFields);
            Assert.Equal(table.KeyFields, WorldKeys.Fields(table.Table));
        }
        var npc = catalog.Tables.Single(x => x.Table == WorldTable.Npcs);
        Assert.DoesNotContain(npc.Fields, x => x.Field.Name is "IsNpc" or "IsMerchant");
        Assert.Contains(npc.Fields, x => x.Field.Name == "Magic3");
        var pet = WorldSchema.Snapshot(WorldTable.PetLevels, new PetLevelData { Level = 7, Exp = 9007199254740993L });
        Assert.Equal(9007199254740993L, pet.Values["Exp"]!.GetValue<long>());
    }

    [Fact]
    public void MovementAndTrapSemanticsAreNotConfusedWithPrototypeIds()
    {
        var catalog = WorldSchema.Catalog();
        var spawn = catalog.Tables.Single(x => x.Table == WorldTable.NpcPositions);
        var ai = catalog.Tables.Single(x => x.Table == WorldTable.Npcs);
        Assert.Contains("Spawn", spawn.Fields.Single(x => x.Field.Name == "ActType").Label);
        Assert.Contains("AI", ai.Fields.Single(x => x.Field.Name == "ActType").Label);
        Assert.Null(ai.Fields.Single(x => x.Field.Name == "ActType").Field.Choices);
        Assert.Contains("Scripted", spawn.Fields.Single(x => x.Field.Name == "ActType").Field.Choices!["105"]);
        var trap = catalog.Tables.Single(x => x.Table == WorldTable.EventTriggers).Fields.Single(x => x.Field.Name == "NpcId");
        Assert.Equal("Trap number", trap.Label); Assert.Null(trap.Field.Reference);
        var role = catalog.Tables.Single(x => x.Table == WorldTable.EventTriggers).Fields.Single(x => x.Field.Name == "NpcType");
        Assert.NotNull(role.Field.Choices); Assert.Equal("Trade Merchant", role.Field.Choices["21"]);
        Assert.True(WorldRules.ReferenceFor(WorldTable.NpcPositions, "NpcId", new() { ["NpcId"] = 880009, ["ActType"] = 0 })!.IsMonster);
        Assert.False(WorldRules.ReferenceFor(WorldTable.NpcPositions, "NpcId", new() { ["NpcId"] = 880009, ["ActType"] = 100 })!.IsMonster);
    }

    [Fact]
    public void ChangedMapNamesCannotEscapeTheServerMapRoot()
    {
        var row = WorldSchema.Snapshot(WorldTable.ZoneInfos, new ZoneInfoData { ZoneNo = 210, MapName = "Fixture", SmdName = "old.smd" });
        var draft = new WorldDraft([row]); draft.Set(row.Key, "SmdName", "../outside.smd");
        Assert.Contains(WorldRules.Validate(draft.Build(Guid.NewGuid()), WorldSchema.Catalog()), issue => issue.Field.Contains("SmdName"));
    }

    [Fact]
    public void AnUnchangedMalformedPatrolIsPreservedButRewritingItRequiresRepair()
    {
        var row = WorldSchema.Snapshot(WorldTable.NpcPositions, new NpcPosData { Index = 17, ZoneId = 210, NpcId = 880009, DotCnt = 2, Path = "bad legacy path" });
        var draft = new WorldDraft([row]); draft.Set(row.Key, "RegTime", 15);
        Assert.Empty(WorldRules.Validate(draft.Build(Guid.NewGuid()), WorldSchema.Catalog()));
        draft.Set(row.Key, "Path", "00120047");
        Assert.Contains(WorldRules.Validate(draft.Build(Guid.NewGuid()), WorldSchema.Catalog()), x => x.Field.Contains("Path"));
    }
}
