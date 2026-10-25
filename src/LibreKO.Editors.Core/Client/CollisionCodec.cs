using System.Buffers.Binary;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Maps;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Client;
public static class CollisionCodec
{
    public static IReadOnlyList<MapPoint> Read(byte[] bytes)
    {
        SourceLimits.CheckedLength(bytes.LongLength);
        if(bytes.Length<8||!bytes.AsSpan(0,4).SequenceEqual("KOCL"u8))throw new InvalidDataException("KOCL header is missing.");
        uint count=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4));if(count>SmdCodec.MaximumFaces||8L+count*36L!=bytes.Length)throw new InvalidDataException("KOCL count/length is invalid or exceeds four million triangles.");
        var points=new MapPoint[checked((int)count*3)];int at=8;
        for(int i=0;i<points.Length;i++){points[i]=new(Float(),Float(),Float());if(!Finite(points[i]))throw new InvalidDataException("Collision coordinates must be finite.");}return points;
        float Float(){float value=BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(at)));at+=4;return value;}
    }
    public static byte[] Write(IReadOnlyList<MapPoint> vertices)
    {
        if(vertices.Count%3!=0||vertices.Count/3>SmdCodec.MaximumFaces)throw new InvalidDataException("Collision requires complete triangles within the four million triangle limit.");
        var bytes=new byte[checked(8+vertices.Count*12)];"KOCL"u8.CopyTo(bytes);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),vertices.Count/3);int at=8;
        foreach(var p in vertices){if(!Finite(p))throw new InvalidDataException("Collision coordinates must be finite.");Put(p.X);Put(p.Y);Put(p.Z);}return bytes;
        void Put(float value){BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at),BitConverter.SingleToInt32Bits(value));at+=4;}
    }
    internal static bool Finite(MapPoint p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
}
