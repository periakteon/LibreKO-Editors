using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Client;

/// <summary>Raw RGBA8 bytes only: no colour conversion, alpha premultiplication or image quantisation.</summary>
public static class RgbaPngCodec
{
    static readonly byte[] signature=[137,80,78,71,13,10,26,10];
    static readonly uint[] crcTable=Enumerable.Range(0,256).Select(i=>{uint v=(uint)i;for(int bit=0;bit<8;bit++)v=(v&1)!=0?0xedb88320^(v>>1):v>>1;return v;}).ToArray();
    static int PixelLength(int width,int height)
    {if(width<=0||height<=0||(long)width*height>SourceLimits.MaximumFileBytes/4)throw new InvalidDataException("RGBA dimensions must be positive and pixel bytes are limited to 512 MiB.");return checked(width*height*4);}
    public static RgbaImage Decode(byte[] input)
    {
        SourceLimits.CheckedLength(input.LongLength);if(!input.AsSpan().StartsWith(signature))throw new InvalidDataException("PNG signature is invalid.");
        int offset=8,width=0,height=0;bool header=false,data=false,endData=false,ended=false;using var compressed=new MemoryStream();
        while(offset<input.Length)
        {
            if(input.Length-offset<12)throw new InvalidDataException("Truncated PNG chunk.");uint rawLength=BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(offset));if(rawLength>int.MaxValue||(long)offset+12+rawLength>input.Length)throw new InvalidDataException("PNG chunk length exceeds the source bounds.");int length=(int)rawLength;
            var type=input.AsSpan(offset+4,4);var bytes=input.AsSpan(offset+8,length);var crc=BinaryPrimitives.ReadUInt32BigEndian(input.AsSpan(offset+8+length));
            if(Crc(type,bytes)!=crc)throw new InvalidDataException("PNG chunk CRC mismatch.");var name=Encoding.ASCII.GetString(type);
            if(!header&&name!="IHDR")throw new InvalidDataException("IHDR must be the first PNG chunk.");
            switch(name)
            {
                case "IHDR":
                    if(header||length!=13)throw new InvalidDataException("Invalid or duplicate PNG header.");width=BinaryPrimitives.ReadInt32BigEndian(bytes);height=BinaryPrimitives.ReadInt32BigEndian(bytes[4..]);PixelLength(width,height);
                    if(bytes[8]!=8||bytes[9]!=6||bytes[10]!=0||bytes[11]!=0||bytes[12]!=0)throw new InvalidDataException("Only non-interlaced RGBA8 PNG is editable; other PNG encodings are read-only.");header=true;break;
                case "IDAT":
                    if(endData)throw new InvalidDataException("PNG IDAT chunks must be consecutive.");SourceLimits.CheckedLength(compressed.Length+length);compressed.Write(bytes);data=true;break;
                case "IEND":
                    if(!data||length!=0||offset+12!=input.Length)throw new InvalidDataException("Invalid PNG end or trailing bytes.");ended=true;break;
                default:
                    if(type.AnyOutsideAsciiLetters()||(type[2]&32)!=0||(type[0]&32)==0)throw new InvalidDataException("Unsupported critical or invalid PNG chunk: "+name);
                    if(data)endData=true;break;
            }
            offset=checked(offset+12+length);if(ended)break;
        }
        if(!ended||compressed.Length<6)throw new InvalidDataException("PNG image data/end is missing.");
        var pixels=new byte[PixelLength(width,height)];int stride=checked(width*4);var row=new byte[stride+1];compressed.Position=0;
        try
        {
            using var zip=new ZLibStream(compressed,CompressionMode.Decompress,true);
            for(int y=0;y<height;y++)
            {
                zip.ReadExactly(row);byte filter=row[0];if(filter>4)throw new InvalidDataException("Unsupported PNG row filter.");int at=y*stride;
                for(int x=0;x<stride;x++)
                {
                    int a=x>=4?pixels[at+x-4]:0,b=y>0?pixels[at+x-stride]:0,c=x>=4&&y>0?pixels[at+x-stride-4]:0;
                    int prediction=filter switch{0=>0,1=>a,2=>b,3=>(a+b)/2,_=>Paeth(a,b,c)};pixels[at+x]=unchecked((byte)(row[x+1]+prediction));
                }
            }
            if(zip.ReadByte()!=-1)throw new InvalidDataException("PNG inflation exceeds the exact expected scanline length.");
        }
        catch(Exception e)when(e is IOException or InvalidDataException){throw new InvalidDataException("Invalid PNG/zlib scanlines: "+e.Message,e);}
        return new(width,height,pixels);
    }
    public static byte[] Encode(RgbaImage image)
    {
        if(image.Pixels.Length!=PixelLength(image.Width,image.Height))throw new InvalidDataException("RGBA pixel length does not match its dimensions.");
        using var output=new MemoryStream();output.Write(signature);var header=new byte[13];BinaryPrimitives.WriteInt32BigEndian(header,image.Width);BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4),image.Height);header[8]=8;header[9]=6;Chunk(output,"IHDR",header);
        using var compressed=new MemoryStream();using(var zip=new ZLibStream(compressed,CompressionLevel.Optimal,true)){int stride=image.Width*4;for(int y=0;y<image.Height;y++){zip.WriteByte(0);zip.Write(image.Pixels,y*stride,stride);}}
        Chunk(output,"IDAT",compressed.ToArray());Chunk(output,"IEND",[]);SourceLimits.CheckedLength(output.Length);return output.ToArray();
    }
    static int Paeth(int a,int b,int c){int p=a+b-c,pa=Math.Abs(p-a),pb=Math.Abs(p-b),pc=Math.Abs(p-c);return pa<=pb&&pa<=pc?a:pb<=pc?b:c;}
    static uint Crc(ReadOnlySpan<byte> type,ReadOnlySpan<byte> data){uint crc=uint.MaxValue;foreach(var b in type)crc=crcTable[(crc^b)&255]^(crc>>8);foreach(var b in data)crc=crcTable[(crc^b)&255]^(crc>>8);return ~crc;}
    static bool AnyOutsideAsciiLetters(this ReadOnlySpan<byte> bytes){foreach(var b in bytes)if(b is not (>=65 and <=90) and not (>=97 and <=122))return true;return false;}
    static void Chunk(Stream stream,string name,byte[] data){Span<byte> length=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(length,data.Length);stream.Write(length);var type=Encoding.ASCII.GetBytes(name);stream.Write(type);stream.Write(data);BinaryPrimitives.WriteUInt32BigEndian(length,Crc(type,data));stream.Write(length);}
}
