using System.Buffers.Binary;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Maps;
namespace LibreKO.Editors.Core.Client;
public static class ClientHeightmapCodec
{
    public const float CellSize=4f;
    public static HeightField Decode(byte[] png)
    {
        var image=RgbaPngCodec.Decode(png);if(image.Width!=image.Height||image.Width is <2 or >4097)throw new InvalidDataException("Client heightmaps require a 2–4097 square RGBA8 grid.");
        var heights=new float[image.Width*image.Height];for(int i=0;i<heights.Length;i++)heights[i]=BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(image.Pixels.AsSpan(i*4)));
        var field=new HeightField(image.Width,CellSize,heights);HeightSurface.Validate(field);return field;
    }
    public static byte[] Encode(HeightField field)
    {
        HeightSurface.Validate(field);if(field.CellSize!=CellSize)throw new InvalidDataException("The current client heightmap format has a fixed 4-unit sample spacing.");
        var bytes=new byte[field.Heights.Length*4];for(int i=0;i<field.Heights.Length;i++)BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i*4),BitConverter.SingleToInt32Bits(field.Heights[i]));return RgbaPngCodec.Encode(new(field.Size,field.Size,bytes));
    }
}
