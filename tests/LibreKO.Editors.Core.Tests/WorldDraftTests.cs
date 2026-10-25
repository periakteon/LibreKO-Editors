using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.Core.Maps;

public class WorldDraftTests
{
    static WorldRowSnapshot Npc(bool monster, string name) => new(new(WorldTable.Npcs, 880009, IsMonster: monster),
        new() { ["Id"] = 880009, ["IsMonster"] = monster, ["Name"] = name, ["NpcType"] = 231, ["Hp"] = 100 }, monster ? "monster-original" : "npc-original");

    [Fact]
    public void CompositeKeysAndDraftBatchRemainIndependent()
    {
        var npc = Npc(false, "Merchant"); var monster = Npc(true, "Monster");
        Assert.NotEqual(new WorldKey(WorldTable.Npcs, 880009, IsMonster: false), new WorldKey(WorldTable.Npcs, 880009, IsMonster: true));
        Assert.NotEqual(new WorldKey(WorldTable.GameEvents, 7, 21), new WorldKey(WorldTable.GameEvents, 7, 71));
        Assert.Equal(new MapPoint(-12, 3, 47), MapSpace.ToGodot(new(12, 3, 47)));
        Assert.Equal(new MapPoint(12, 3, 47), MapSpace.FromGodot(new(-12, 3, 47)));
        var draft = new WorldDraft([npc, monster]);
        var after = (JsonObject)npc.Values.DeepClone(); after["Name"] = "Edited merchant";
        var other = (JsonObject)monster.Values.DeepClone(); other["Hp"] = 200;
        draft.Apply([new(npc.Key, npc, after), new(monster.Key, monster, other)]);
        after["Name"] = "External mutation"; npc.Values["Name"] = "Caller mutation";
        Assert.Equal("Edited merchant", draft.Rows.Single(x => !x.Key.IsMonster!.Value).Values["Name"]!.ToString());
        draft.Undo(false);
        Assert.False(draft.IsDirty);
        Assert.Equal("Merchant", draft.Rows.Single(x => !x.Key.IsMonster!.Value).Values["Name"]!.ToString());
        draft.Undo(true); Assert.Equal(2, draft.Build(Guid.NewGuid()).Rows.Count);
        var exposed = draft.Rows[0].Values; exposed["Name"] = "Tampered accessor";
        Assert.DoesNotContain(draft.Rows, x => x.Values["Name"]!.ToString() == "Tampered accessor");
    }

    [Fact]
    public void ARejectedBatchDoesNotPartiallyChangeTheDraftAndKeysCannotBeEdited()
    {
        var row = Npc(false, "Merchant"); var draft = new WorldDraft([row]);
        Assert.Throws<InvalidOperationException>(() => draft.Set(row.Key, "IsMonster", true));
        var after = (JsonObject)row.Values.DeepClone(); after["Name"] = "Bad partial";
        Assert.Throws<InvalidOperationException>(() => draft.Apply([new(row.Key, row, after), new(row.Key, null, after)]));
        Assert.False(draft.IsDirty);
        Assert.Throws<InvalidOperationException>(() => WorldKeys.Validate(new(WorldTable.Npcs, 1)));
        Assert.Throws<InvalidOperationException>(() => WorldKeys.Validate(new(WorldTable.Npcs, 1, Zone: 21, IsMonster: true)));
        Assert.Throws<InvalidOperationException>(() => WorldKeys.Validate(new(WorldTable.PetLevels, 1, IsMonster: false)));
        Assert.Throws<InvalidOperationException>(() => WorldKeys.Validate(new(WorldTable.GameEvents, 1)));
    }

    [Fact]
    public void ValidationPreservesUnchangedUnknownValuesAndRejectsChangedRanges()
    {
        var row = Npc(false, "Merchant"); var draft = new WorldDraft([row]); draft.Set(row.Key, "Name", "New name");
        var fields = new WorldFieldDescriptor[]
        {
            new(new("Id", "Identity", FieldKind.Integer, false, 1, int.MaxValue), "ID", false, null),
            new(new("IsMonster", "Identity", FieldKind.Boolean, false), "Monster", false, null),
            new(new("Name", "Identity", FieldKind.Text, true), "Name", false, 100),
            new(new("NpcType", "Role", FieldKind.Enum, true, 0, 255, new Dictionary<string,string>{{"0","Monster"},{"21","Merchant"}}), "Role", false, null),
            new(new("Hp", "Stats", FieldKind.Integer, true, 0, int.MaxValue), "HP", false, null)
        };
        var catalog = new WorldCatalog([new(WorldTable.Npcs, ["Id", "IsMonster"], fields, new())]);
        Assert.Empty(WorldRules.Validate(draft.Build(Guid.NewGuid()), catalog));
        draft.Set(row.Key, "NpcType", 999);
        Assert.Contains(WorldRules.Validate(draft.Build(Guid.NewGuid()), catalog), x => x.Field.Contains("NpcType"));
        draft.Set(row.Key, "NpcType", 231); draft.Set(row.Key, "Hp", 9007199254740993L);
        Assert.Contains(WorldRules.Validate(draft.Build(Guid.NewGuid()), catalog), x => x.Field.Contains("Hp"));
    }

    [Fact]
    public void ExactPetExperienceAndBuildOutputsDoNotShareMutableNodes()
    {
        var key = new WorldKey(WorldTable.PetLevels, 7);
        var original = new WorldRowSnapshot(key, new() { ["Level"] = 7, ["Exp"] = 9007199254740993L }, "original");
        var draft = new WorldDraft([original]); draft.Set(key, "Exp", 9007199254740995L);
        var change = draft.Build(Guid.NewGuid());
        Assert.Equal(9007199254740993L, change.Rows[0].Before!.Values["Exp"]!.GetValue<long>());
        Assert.Equal(9007199254740995L, change.Rows[0].After!["Exp"]!.GetValue<long>());
        change.Rows[0].After!["Exp"] = 1;
        Assert.Equal(9007199254740995L, draft.Rows.Single().Values["Exp"]!.GetValue<long>());
    }

    [Fact]
    public void PatrolCoordinatesRoundTripOnlyWhenRepresentable()
    {
        MapPoint[] points = [new(12, 0, 47), new(-12, 0, 9999)];
        var encoded = PatrolPath.Encode(points);
        Assert.Equal((byte)2, encoded.Count); Assert.Equal("00120047-0129999", encoded.Path);
        Assert.Equal(points, PatrolPath.Decode(encoded.Count, encoded.Path));
        Assert.Throws<InvalidDataException>(() => PatrolPath.Encode([new(12.5f, 0, 47)]));
        Assert.Throws<InvalidDataException>(() => PatrolPath.Encode([new(10000, 0, 0)]));
        Assert.Throws<InvalidDataException>(() => PatrolPath.Encode([new(-1000, 0, 0)]));
        Assert.Throws<InvalidDataException>(() => PatrolPath.Decode(2, "00120047"));
        Assert.Throws<InvalidDataException>(() => MapSpace.ToGodot(new(float.NaN, 0, 0)));
    }
}
