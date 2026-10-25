using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
public class RasterCodecTests
{
    [Fact]
    public void FloatTerrainAndHighBitSplatRoundTripExactly()
    {
        var floats=new[]{BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),BitConverter.Int32BitsToSingle(0x3f800001),float.Epsilon,1f,2f,3f,4f,5f,6f};
        var field=new HeightField(3,4,floats);var height=ClientHeightmapCodec.Decode(ClientHeightmapCodec.Encode(field));
        Assert.Equal(floats.Select(BitConverter.SingleToInt32Bits),height.Heights.Select(BitConverter.SingleToInt32Bits));
        var atlas=RgbaPngCodec.Encode(new(1,8192,new byte[8192*4]));
        var surface=new GroundSurface(2,8192,1,[new(458,8191,6,7),new(65535,0,7,0),new(0,65535,0,5),new(256,257,3,4)],new byte[8192*4]);
        var encodedSplat=GroundSurfaceCodec.EncodeSplat(surface);var decoded=GroundSurfaceCodec.Decode(encodedSplat,atlas);
        Assert.Equal(new GroundCell(458,8191,6,7),decoded.Cells[0]);Assert.Equal(14,RgbaPngCodec.Decode(encodedSplat).Pixels[2]);Assert.Equal(surface.Cells,decoded.Cells);
        Assert.Throws<InvalidDataException>(()=>GroundSurfaceCodec.EncodeSplat(surface with{Cells=[new(255,0,0,0),new(0,0,0,0),new(0,0,0,0),new(0,0,0,0)]}));
        var bad=field with{Heights=floats.Select(x=>float.NaN).ToArray()};Assert.Throws<InvalidDataException>(()=>ClientHeightmapCodec.Encode(bad));
    }
    [Fact]
    public void EveryPngFilterAndSplitIdatDecodesIndependentFixture()
    {
        byte[] filtered=[0,0,10,20,30,40,50,60,70, 1,1,11,21,31,40,40,40,40, 2,1,1,1,1,1,1,1,1, 3,2,7,12,17,21,21,21,21, 4,1,1,1,1,1,1,1,1];
        var fixture=PngFixture.Make(2,5,filtered,split:true);var decoded=RgbaPngCodec.Decode(fixture);
        var expected=Enumerable.Range(0,5).SelectMany(y=>Enumerable.Range(0,8).Select(x=>(byte)(x*10+y))).ToArray();Assert.Equal(expected,decoded.Pixels);
        Assert.Equal(expected,RgbaPngCodec.Decode(RgbaPngCodec.Encode(decoded)).Pixels);
        var crc=fixture.ToArray();crc[29]^=1;Assert.Throws<InvalidDataException>(()=>RgbaPngCodec.Decode(crc));
        var length=fixture.ToArray();BinaryPrimitives.WriteUInt32BigEndian(length.AsSpan(8),uint.MaxValue);Assert.Throws<InvalidDataException>(()=>RgbaPngCodec.Decode(length));
        Assert.Throws<InvalidDataException>(()=>RgbaPngCodec.Decode(PngFixture.Make(2,5,filtered.Concat(new byte[]{1}).ToArray())));
        Assert.Throws<InvalidDataException>(()=>RgbaPngCodec.Decode(PngFixture.Make(2,5,filtered[..^1])));
        Assert.Throws<InvalidDataException>(()=>RgbaPngCodec.Decode(PngFixture.Make(int.MaxValue,2,[])));
        Assert.Throws<InvalidDataException>(()=>RgbaPngCodec.Decode(PngFixture.Make(2,5,filtered,interlace:1)));
        Assert.Throws<InvalidDataException>(()=>RgbaPngCodec.Decode(PngFixture.Make(2,5,filtered,color:2)));
    }
}
public static class PngFixture
{
    public static byte[] Make(int width,int height,byte[] filtered,bool split=false,byte interlace=0,byte color=6)
    {
        using var output=new MemoryStream();output.Write(new byte[]{137,80,78,71,13,10,26,10});var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),height);header[8]=8;header[9]=color;header[12]=interlace;Chunk(output,"IHDR",header);
        using var data=new MemoryStream();using(var zip=new ZLibStream(data,CompressionLevel.SmallestSize,true))zip.Write(filtered);var compressed=data.ToArray();
        if(split){var middle=compressed.Length/2;Chunk(output,"IDAT",compressed[..middle]);Chunk(output,"IDAT",compressed[middle..]);}else Chunk(output,"IDAT",compressed);Chunk(output,"IEND",[]);return output.ToArray();
    }
    static void Chunk(Stream output,string name,byte[] bytes)
    {
        var length=new byte[4];BinaryPrimitives.WriteInt32BigEndian(length,bytes.Length);output.Write(length);var type=Encoding.ASCII.GetBytes(name);output.Write(type);output.Write(bytes);
        uint crc=0xffffffff;foreach(var b in type.Concat(bytes)){crc^=b;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?0xedb88320^(crc>>1):crc>>1;}BinaryPrimitives.WriteUInt32BigEndian(length,~crc);output.Write(length);
    }
}
