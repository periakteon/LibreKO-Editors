using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Maps;
using LibreKO.Game.World;
public class SmdMovementTests
{
    [Fact]
    public void TerrainTransposeTrianglesAndMovementUseStockSemantics()
    {
        var doc=SmdCodec.Read(SmdFixtures.Create()); var heights=SmdHeightAdapter.ToCanonical(doc);
        heights.Heights[2*5+1]=777; SmdHeightAdapter.ApplyCanonical(doc,heights);
        SmdSpatialEdits.MoveWarp(doc,0,new(24,3,16)); SmdSpatialEdits.MoveRegene(doc,0,new(6,7,8)); SmdSpatialEdits.SetEventTile(doc,1,2,0);
        var path=Path.Combine(Path.GetTempPath(),"libreko-smd-movement-"+Guid.NewGuid()+".smd"); File.WriteAllBytes(path,SmdCodec.Write(doc)); var stock=SmdFile.Load(path)!;
        foreach(var (sampleX,sampleZ) in new[]{(1f,2f),(10f,19f),(20f,9f),(23f,22f),(-1f,2f),(32f,2f)})
        {
            var expectedHeight=SmdHeightAdapter.Sample(doc,sampleX,sampleZ);
            Assert.Equal(expectedHeight,stock.GetHeight(sampleX,sampleZ),3);
        }
        Assert.False(stock.IsWalkable(1,2)); Assert.Equal(24f,stock.Warps[7].X); Assert.Equal(8f,stock.RegeneEvents[0].PosZ);
    }
}
