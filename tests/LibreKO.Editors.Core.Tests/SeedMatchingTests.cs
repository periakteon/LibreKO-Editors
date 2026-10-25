using System.Text;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;

public class SeedMatchingTests
{
    static JsonSourceDocument Open(SourceFixture f,SourceStore store,string file,string text)
    { f.Write(SourceRoot.ServerData,file,Encoding.UTF8.GetBytes(text)); var s=store.Read(f.Key(SourceRoot.ServerData,file)); return JsonSourceDocument.Open(s,store.Blobs.Read(s.Content!)); }
    [Fact]
    public void GeneratedIndicesNeverSelectAnAmbiguousSeedRow()
    {
        using var f=SourceFixture.Create(); var store=new SourceStore(f.Profile,f.WorkspaceRoot);
        var a=Open(f,store,"NpcPositions.zone2.json","[{\"Index\":1,\"NpcId\":\"9\",\"ZoneId\":2,\"X\":10,\"ActType\":1,\"Opaque\":\"a\"},{\"Index\":123,\"NpcId\":9,\"ZoneId\":2,\"X\":10,\"ActType\":1}]");
        var row=new WorldRowSnapshot(new(WorldTable.NpcPositions,999),new(){["Index"]=999,["NpcId"]=9,["ZoneId"]=2,["X"]=10,["ActType"]=1},"test");
        var before=a.Render(); var match=SeedRowMatcher.Match(row,[a]);
        Assert.Equal(SeedMatchState.Ambiguous,match.State); Assert.Equal(2,match.Candidates.Count); Assert.Equal(before,a.Render());
        a.Move(match.Candidates[0],1);
        Assert.Equal(SeedMatchState.Ambiguous,SeedRowMatcher.Match(row,[a]).State);
        Assert.Contains("renumber",SeedShardResolver.StructureWarning(WorldTable.NpcPositions),StringComparison.OrdinalIgnoreCase);
        var different=row with{Values=(JsonObject)row.Values.DeepClone()}; different.Values["X"]=12;
        var pending=SeedComparison.PrepareSeedUpdate(different,a,match.Candidates[0],store);
        Assert.Single(pending.Files); Assert.Equal(before,File.ReadAllBytes(SourcePaths.Resolve(f.Profile,a.Snapshot.Key,false)));
        var candidate=JsonNode.Parse(TextDocument.Decode(store.Blobs.Read(pending.Files[0].After!)).Text)!;
        Assert.Equal("a",candidate[1]!["Opaque"]!.GetValue<string>()); Assert.Equal(1,candidate[1]!["Index"]!.GetValue<int>());
        var db=SeedComparison.PrepareDatabaseUpdate(a,match.Candidates[0],row,row.Key);
        Assert.Equal(999,db.Rows[0].After!["Index"]!.GetValue<int>());
        f.Write(SourceRoot.ServerData,"NpcPositions.zone2.json","[]"u8.ToArray());
        Assert.Throws<InvalidOperationException>(()=>SeedComparison.PrepareSeedUpdate(row,a,match.Candidates[0],store));
    }
    [Fact]
    public void ShardsOverrideMonolithAndSortLikeStockLoader()
    {
        var paths=new[]{"NpcPositions.json","NpcPositions.zone2.json","npcpositions.ZONE10.json","Npcs.json"};
        Assert.Equal(new[]{"npcpositions.ZONE10.json","NpcPositions.zone2.json"},SeedShardResolver.Active(WorldTable.NpcPositions,paths));
        Assert.Equal(new[]{"Npcs.json"},SeedShardResolver.Active(WorldTable.Npcs,paths));
    }
}
