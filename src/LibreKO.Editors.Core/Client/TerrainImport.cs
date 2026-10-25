namespace LibreKO.Editors.Core.Client;
public static class TerrainImport
{
    public static void Validate(byte[] heights,byte[] splat,byte[] tiles)
    {
        var height=ClientHeightmapCodec.Decode(heights);var ground=GroundSurfaceCodec.Decode(splat,tiles);
        if(height.Size!=ground.Size)throw new InvalidDataException("Imported height and splat grids must have the same dimensions.");
        if(ground.Cells.Any(c=>new[]{c.Primary,c.Secondary}.Any(t=>t!=GroundSurfaceCodec.NoTile&&t>=ground.TileCount)))throw new InvalidDataException("Imported ground refers to a tile outside its atlas.");
    }
}
