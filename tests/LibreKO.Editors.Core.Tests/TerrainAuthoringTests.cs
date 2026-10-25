using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Maps;
using LibreKO.Editors.Core.Sources;
public class TerrainAuthoringTests
{
    [Fact]
    public void TerrainImportRequiresCompleteCompatibleDimensionsAndReferencedTiles()
    {
        byte[] heights=ClientHeightmapCodec.Encode(new(3,4,new float[9]));var surface=new GroundSurface(3,1,1,Enumerable.Repeat(new GroundCell(0,65535,0,0),9).ToArray(),[8,9,10,255]);
        byte[] splat=GroundSurfaceCodec.EncodeSplat(surface),tiles=RgbaPngCodec.Encode(new(1,1,surface.Atlas));
        TerrainImport.Validate(heights,splat,tiles);
        Assert.Throws<InvalidDataException>(()=>TerrainImport.Validate(ClientHeightmapCodec.Encode(new(2,4,new float[4])),splat,tiles));
        Assert.Throws<InvalidDataException>(()=>TerrainImport.Validate(heights,GroundSurfaceCodec.EncodeSplat(surface with{Cells=Enumerable.Repeat(new GroundCell(1,65535,0,0),9).ToArray()}),tiles));
    }
    [Fact]
    public void BrushesUseOriginalStrokeSamplesAndPreserveUntouchedBits()
    {
        var original=new HeightField(3,4,[BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),1,2,3,40,5,6,7,8]);
        Assert.Equal(42,TerrainBrush.Apply(original,new("raise",1,1,0,2,0)).Heights[4]);Assert.Equal(38,TerrainBrush.Apply(original,new("lower",1,1,0,2,0)).Heights[4]);
        Assert.Equal(9,TerrainBrush.Apply(original,new("flatten",1,1,0,1,9)).Heights[4]);
        var stroke=new TerrainStroke("smooth",1,1,0,1,0);var once=TerrainBrush.Apply(original,stroke);var repeated=TerrainBrush.ApplyStroke(original,[stroke,stroke,stroke]);Assert.Equal(once.Heights,repeated.Heights);
        Assert.Equal(BitConverter.SingleToInt32Bits(original.Heights[0]),BitConverter.SingleToInt32Bits(once.Heights[0]));Assert.Equal(40,original.Heights[4]);
        var region=TerrainBrush.ExportRegion(original,1,1,2,2);var imported=TerrainBrush.ImportRegion(original,0,0,2,2,region);Assert.Equal(40,imported.Heights[0]);Assert.Equal(8,imported.Heights[4]);
        Assert.Throws<InvalidDataException>(()=>TerrainBrush.ImportRegion(original,2,2,2,2,region));
    }
    [Fact]
    public void LinkedChangesRequireVerifiedMappingDimensionsAndSpacing()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var original=new HeightField(3,4,[0,1,2,3,4,5,6,7,8]);
        var server=SmdCodec.Read(AlignedSmd());var serverKey=f.Key(SourceRoot.ServerMaps,"fixture.smd");var clientKey=f.Key(SourceRoot.ClientProject,"assets/terrain/fixture/heightmap.png");
        f.Write(SourceRoot.ServerMaps,"fixture.smd",SmdCodec.Write(server));f.Write(SourceRoot.ClientProject,clientKey.RelativePath,ClientHeightmapCodec.Encode(original));var smd=store.Read(serverKey);var client=store.Read(clientKey);
        var proposed=TerrainBrush.Apply(original,new("raise",1,2,0,10,0));var result=TerrainAlignment.Prepare(server,smd,client,proposed,store);Assert.Empty(result.Issues);Assert.Equal(2,result.Change.Files.Count);
        var changedServer=SmdCodec.Read(store.Blobs.Read(result.Change.Files.Single(x=>x.Before.Key.Root==SourceRoot.ServerMaps).After!));Assert.Equal(proposed.Heights,SmdHeightAdapter.ToCanonical(changedServer).Heights);Assert.Equal(smd.Sha256,store.Read(smd.Key).Sha256);
        Assert.NotEmpty(TerrainAlignment.Prepare(server,smd,client,proposed with{CellSize=8},store).Issues);
        var wrong=client with{Key=client.Key with{RelativePath="assets/terrain/wrong/heightmap.png"}};Assert.NotEmpty(TerrainAlignment.Prepare(server,smd,wrong,proposed,store).Issues);
    }
    public static byte[] AlignedSmd()
    {
        using var bytes=new MemoryStream();using var w=new BinaryWriter(bytes);w.Write(3);w.Write(4f);for(int x=0;x<3;x++)for(int z=0;z<3;z++)w.Write((float)(z*3+x));w.Write(8f);w.Write(8f);w.Write(0);w.Write(0u);w.Write(0);for(int i=0;i<9;i++)w.Write((short)1);w.Write(0);w.Write(0);return bytes.ToArray();
    }
}
