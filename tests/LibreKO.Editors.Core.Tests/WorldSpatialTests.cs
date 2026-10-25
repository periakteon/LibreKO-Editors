using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Maps;
using LibreKO.Editors.Core.World;
public class WorldSpatialTests
{
    static WorldRowSnapshot Spawn(int id,int x,int z)
    { var v=new JsonObject { ["Index"]=id,["LeftX"]=x,["TopZ"]=z,["ActType"]=5,["Direction"]=1234,["DotCnt"]=1,["Path"]="00120047" };return new(new(WorldTable.NpcPositions,id),v,RecordFingerprint.Compute(v)); }
    [Fact] public void SpatialEditsPreserveMapCoordinatesAndSquareSpawnSpread()
    {
        var point=new MapPoint(12,3,47); Assert.Equal(point,MapSpace.FromGodot(MapSpace.ToGodot(point)));
        Assert.Equal(15,SpawnGeometry.Bounds(20,30,5).Min.X);Assert.Equal(35,SpawnGeometry.Bounds(20,30,5).Max.Z);
        Assert.Equal(20,SpawnGeometry.Bounds(20,30,-5).Min.X);
        var rows=new[]{Spawn(1,12,47),Spawn(2,20,30)}; var draft=new WorldDraft(rows);var moved=WorldManipulationDraft.Move(rows,new(3,0,-2));draft.Apply(moved);
        Assert.Equal("15",draft.Rows[0].Values["LeftX"]!.ToString());Assert.Equal("45",draft.Rows[0].Values["TopZ"]!.ToString());
        Assert.Equal("00120047",draft.Rows[0].Values["Path"]!.ToString()); Assert.Equal("1234",draft.Rows[0].Values["Direction"]!.ToString());
        draft.Undo(false);Assert.False(draft.IsDirty);draft.Undo(true);Assert.True(draft.IsDirty);
        Assert.Throws<InvalidOperationException>(()=>WorldManipulationDraft.Move(rows,new(0.5f,0,0)));
        Assert.Throws<InvalidOperationException>(()=>WorldManipulationDraft.Move(rows,new(float.NaN,0,0)));
        var encoded=PatrolPath.Encode([new(12,0,47),new(20,0,35)]); var before=draft.Rows[0]; var after=(JsonObject)before.Values.DeepClone();after["Path"]=encoded.Path;after["DotCnt"]=encoded.Count;
        draft.Apply([new(before.Key,before,after)]);draft.Undo(false);Assert.Equal("00120047",draft.Rows[0].Values["Path"]!.ToString());
    }
    [Fact] public void HeightSamplingMatchesClientBilinearCoordinatesAndRejectsInvalidFields()
    {
        var field=new HeightField(2,4,[0,4,8,12]);Assert.Equal(6,HeightSurface.Sample(field,2,2));Assert.Null(HeightSurface.Sample(field,-1,0));
        Assert.Throws<InvalidDataException>(()=>HeightSurface.Validate(new(2,4,[0,1,float.NaN,2])));
        Assert.Throws<InvalidDataException>(()=>HeightSurface.Validate(new(3,4,[0,1,2])));
    }
    [Fact] public void CompletingAGesturePreservesOtherDraftEditsAndRefusesChangedCoordinates()
    {
        var original=Spawn(1,12,47);original.Values["RegTime"]=10;var gesture=WorldManipulationDraft.Move([original],new(3,0,2))[0];
        var currentValues=(JsonObject)original.Values.DeepClone();currentValues["RegTime"]=99;var current=original with{Values=currentValues};
        var merged=WorldManipulationDraft.Merge(current,gesture);Assert.Equal("99",merged.After!["RegTime"]!.ToString());Assert.Equal("15",merged.After["LeftX"]!.ToString());
        currentValues["LeftX"]=22;Assert.Throws<InvalidOperationException>(()=>WorldManipulationDraft.Merge(current,gesture));
    }
}
