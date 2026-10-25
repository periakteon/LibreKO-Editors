using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Maps;
public class SmdSpatialTests
{
    [Fact]
    public void AsymmetricTerrainAndRelocatedCollisionMatchStockQueries()
    {
        var doc=SmdCodec.Read(SmdFixtures.Create()); var canonical=SmdHeightAdapter.ToCanonical(doc);
        Assert.Equal(120f,canonical.Heights[2*5+1]); canonical.Heights[2*5+1]=987;
        SmdHeightAdapter.ApplyCanonical(doc,canonical); Assert.Equal(987f,doc.Heights.Heights[1*5+2]);
        SmdSpatialEdits.MoveWarp(doc,0,new(18,19,20)); SmdSpatialEdits.MoveRegene(doc,0,new(10,11,12)); SmdSpatialEdits.SetEventTile(doc,1,2,0);
        Assert.Equal(new MapPoint(18,19,20),doc.Warps[0].Position); Assert.Equal(0,doc.EventTiles[1*5+2]);
        for(int v=0;v<doc.CollisionVertices.Length;v+=3) {doc.CollisionVertices[v]+=18;doc.CollisionVertices[v+2]+=18;}
        Assert.NotEmpty(SmdCollisionIndex.Rebuild(doc,null));
        var knownShapeFaces=new Dictionary<int,IReadOnlyList<int>>{[27]=new[]{0}};
        Assert.Empty(SmdCollisionIndex.Rebuild(doc,knownShapeFaces));
        Assert.Empty(doc.Cells.Single(c=>c.X==0&&c.Z==0).Subcells.SelectMany(x=>x));
        var target=doc.Cells.Single(c=>c.X==1&&c.Z==1); Assert.Equal(new ushort[]{27},target.Shapes); Assert.Equal(new uint[]{0,1,2},target.Subcells[5]);
        var saved=SmdCodec.Read(SmdCodec.Write(doc)); Assert.Equal(doc.CollisionVertices,saved.CollisionVertices);
        Assert.Equal(doc.Tail,saved.Tail); Assert.Equal(doc.Objects,saved.Objects);
        doc.CollisionVertices[0]++; Assert.NotEmpty(SmdValidation.Validate(doc));
    }
    [Fact]
    public void CompleteSectionImportAndOneGestureUndoAreGuarded()
    {
        var doc=SmdCodec.Read(SmdFixtures.Create()); var donor=SmdCodec.Read(SmdFixtures.Create());
        SmdCollisionIndex.ImportSection(doc,donor); Assert.Empty(SmdValidation.Validate(doc));
        var draft=new SmdDraft(doc); draft.Edit(d=>{SmdSpatialEdits.MoveWarp(d,0,new(22,2,24));SmdSpatialEdits.SetEventTile(d,2,3,0);});
        Assert.True(draft.IsDirty); Assert.True(draft.Undo()); Assert.False(draft.IsDirty); Assert.True(draft.Redo());
        Assert.Equal(new MapPoint(22,2,24),draft.Document.Warps[0].Position);
        Assert.Throws<InvalidDataException>(()=>SmdSpatialEdits.MoveWarp(doc,0,new(float.NaN,0,0)));
        Assert.Throws<ArgumentOutOfRangeException>(()=>SmdSpatialEdits.SetEventTile(doc,99,0,0));
    }
}
