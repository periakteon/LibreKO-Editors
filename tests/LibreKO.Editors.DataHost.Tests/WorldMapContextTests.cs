using LibreKO.Editors.Contracts;
using LibreKO.Editors.DataHost.World;
[Collection("World database")]
public class WorldMapContextTests
{
    [Fact] public async Task ContextSeparatesExplicitRowsSharedStartsAndTerrainDependentSpawns()
    {
        await WorldFixture.Ensure(); var cs=await WorldFixture.Schema("worldcontext",true); var service=new WorldDatabase(cs,WorldFixture.Scratch(),new TestWorldJournal());
        var source=new WorldKey(WorldTable.ZoneInfos,213);var sibling=source with{Id=214};var cross=source with{Id=215};
        var a=WorldFixture.Values(source);a["MapName"]="Eslant I";a["SmdName"]="a.smd";
        var b=WorldFixture.Values(sibling);b["MapName"]="Eslant II";b["SmdName"]="A.SMD";
        var c=WorldFixture.Values(cross);c["MapName"]="Eslant III";c["SmdName"]="b.smd";
        var npc=new WorldKey(WorldTable.Npcs,880019,IsMonster:true); var spawn=new WorldKey(WorldTable.NpcPositions,885001);var pos=WorldFixture.Values(spawn);pos["ZoneId"]=213;
        var start=new WorldKey(WorldTable.StartPositions,213);
        Assert.Equal(SaveStatus.Saved,(await service.SaveAsync(WorldWriteTests.Change(new(source,null,a),new(sibling,null,b),new(cross,null,c),new(npc,null,WorldFixture.Values(npc)),new(spawn,null,pos),new(start,null,WorldFixture.Values(start))))).Status);
        var context=await service.MapContextAsync(214);Assert.Equal(213,context.StartSourceZone);Assert.Equal(213,context.SpawnSourceZone);Assert.Empty(context.TerrainCandidates);
        var different=await service.MapContextAsync(215);Assert.Null(different.StartSourceZone);Assert.Equal(215,different.SpawnSourceZone);Assert.Equal(new[]{213},different.TerrainCandidates);
        using var f=SourceFixture.Create();var store=new LibreKO.Editors.Core.Sources.SourceStore(f.Profile,f.WorkspaceRoot);
        f.Write(SourceRoot.ServerMaps,"a.smd",SmdFixtures.Create());f.Write(SourceRoot.ServerMaps,"b.smd",SmdFixtures.Create());
        var verified=await service.MapContextAsync(215,store);Assert.Equal(213,verified.SpawnSourceZone);Assert.Null(verified.StartSourceZone);Assert.Empty(verified.TerrainCandidates);
        var changed=LibreKO.Editors.Core.Maps.SmdCodec.Read(SmdFixtures.Create());changed.Heights.Heights[0]=99;
        f.Write(SourceRoot.ServerMaps,"b.smd",LibreKO.Editors.Core.Maps.SmdCodec.Write(changed));
        Assert.Equal(215,(await service.MapContextAsync(215,store)).SpawnSourceZone);
    }
}
