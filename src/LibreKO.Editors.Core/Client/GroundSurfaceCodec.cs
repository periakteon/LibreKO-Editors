using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Client;
public static class GroundSurfaceCodec
{
    public const int NoTile=65535;
    public static GroundSurface Decode(byte[] splat,byte[] atlas)
    {
        var image=RgbaPngCodec.Decode(splat);var tiles=RgbaPngCodec.Decode(atlas);
        if(image.Width!=image.Height||image.Width is <2 or >4097||tiles.Height%tiles.Width!=0||tiles.Height/tiles.Width is <1 or >8192)throw new InvalidDataException("Ground sources require a square splat and a vertical square-tile RGBA8 atlas (up to 8192 layers).");
        var cells=new GroundCell[image.Width*image.Height];for(int i=0;i<cells.Length;i++){int r=image.Pixels[i*4],g=image.Pixels[i*4+1],b=image.Pixels[i*4+2],a=image.Pixels[i*4+3],hi1=b>>3,hi2=a>>3;cells[i]=new(r==255&&hi1==0?NoTile:r|(hi1<<8),g==255&&hi2==0?NoTile:g|(hi2<<8),(byte)(b&7),(byte)(a&7));}
        return new(image.Width,tiles.Height/tiles.Width,tiles.Width,cells,tiles.Pixels);
    }
    public static byte[] EncodeSplat(GroundSurface surface)
    {
        if(surface.Size is <2 or >4097||surface.Cells.Length!=(long)surface.Size*surface.Size)throw new InvalidDataException("Invalid ground cell dimensions.");
        var bytes=new byte[surface.Cells.Length*4];for(int i=0;i<surface.Cells.Length;i++)
        {
            var c=surface.Cells[i];foreach(var layer in new[]{c.Primary,c.Secondary})if(layer!=NoTile&&(layer<0||layer>8191||layer==255))throw new InvalidDataException("Ground layers use 13 bits; layer255 is reserved by the client as NoTile (65535).");
            if(c.Direction1>7||c.Direction2>7)throw new InvalidDataException("Ground tile directions must be 0–7.");
            bytes[i*4]=(byte)(c.Primary==NoTile?255:c.Primary&255);bytes[i*4+1]=(byte)(c.Secondary==NoTile?255:c.Secondary&255);bytes[i*4+2]=(byte)((c.Primary==NoTile?0:c.Primary>>8<<3)|c.Direction1);bytes[i*4+3]=(byte)((c.Secondary==NoTile?0:c.Secondary>>8<<3)|c.Direction2);
        }
        return RgbaPngCodec.Encode(new(surface.Size,surface.Size,bytes));
    }
}
