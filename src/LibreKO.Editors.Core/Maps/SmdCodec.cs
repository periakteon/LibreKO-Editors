using System.Buffers.Binary;
using System.Text;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;

namespace LibreKO.Editors.Core.Maps;

public static class SmdCodec
{
    public const int MaximumFaces=4_000_000;
    public static SmdDocument Read(byte[] bytes)
    {
        SourceLimits.CheckedLength(bytes.LongLength); var doc=new SmdDocument{Original=bytes.ToArray()};
        using var stream=new MemoryStream(bytes,false); using var r=new BinaryReader(stream);
        int Offset()=>checked((int)stream.Position);
        void Need(long length) { if(length<0 || length>stream.Length-stream.Position) throw new EndOfStreamException(); }
        int Count(int maximum) { int n=r.ReadInt32(); if(n<0 || n>maximum) throw new InvalidDataException("Invalid or unsupported SMD section count."); return n; }
        try
        {
            int size=r.ReadInt32(); float unit=r.ReadSingle();
            if(size is <2 or >4097 || !float.IsFinite(unit)||unit<=0) throw new InvalidDataException("SMD terrain requires 2–4097 samples and a finite positive unit distance.");
            int count=checked(size*size); Need(count*4L); var heights=new float[count]; for(int i=0;i<count;i++) heights[i]=r.ReadSingle();
            doc.Heights=new(size,unit,heights); doc.OriginalHeights=new(size,unit,heights.ToArray()); doc.Sections["Terrain"]=new(0,Offset(),true,true);
            int start=Offset(); doc.MapWidth=r.ReadSingle(); doc.MapHeight=r.ReadSingle();
            float expected=(size-1)*unit;
            if(!float.IsFinite(doc.MapWidth)||!float.IsFinite(doc.MapHeight)||doc.MapWidth<=0||doc.MapHeight<=0||Math.Abs(doc.MapWidth-expected)>0.01f) throw new InvalidDataException("SMD collision dimensions do not match terrain.");
            int faces=Count(MaximumFaces); Need(faces*36L); doc.CollisionVertices=new float[checked(faces*9)]; for(int i=0;i<doc.CollisionVertices.Length;i++) doc.CollisionVertices[i]=r.ReadSingle();
            doc.InitialVertices=doc.CollisionVertices.ToArray();
            var nx=(long)Math.Ceiling(doc.MapWidth/16d); var nz=(long)Math.Ceiling(doc.MapHeight/16d);
            if(nx*nz>SourceLimits.MaximumJsonRows) throw new InvalidDataException("SMD collision cells exceed the supported bound.");
            for(int z=0;z<nz;z++) for(int x=0;x<nx;x++)
            {
                uint exists=r.ReadUInt32(); if(exists==0) { doc.Cells.Add(new(x,z,0,[],Enumerable.Range(0,16).Select(_=>Array.Empty<uint>()).ToArray())); continue; }
                int shapes=Count(ushort.MaxValue+1); Need(shapes*2L); var ids=new ushort[shapes]; for(int i=0;i<shapes;i++) ids[i]=r.ReadUInt16();
                var sub=new List<uint[]>();
                for(int i=0;i<16;i++) { int polys=Count(MaximumFaces); Need(polys*12L); var indices=new uint[checked(polys*3)]; for(int j=0;j<indices.Length;j++) indices[j]=r.ReadUInt32(); sub.Add(indices); }
                doc.Cells.Add(new(x,z,exists,ids,sub));
            }
            doc.Sections["Collision"]=new(start,Offset()-start,true,true,faces);
            if(!Records("Objects",24,doc.Objects,()=>new SmdObject(r.ReadInt32(),r.ReadInt16(),r.ReadInt16(),r.ReadInt16(),r.ReadInt16(),Point(r)))) return Finish();
            start=Offset(); Need(count*2L); doc.EventTiles=new short[count]; for(int i=0;i<count;i++) doc.EventTiles[i]=r.ReadInt16(); doc.InitialTiles=doc.EventTiles.ToArray(); doc.Sections["Tiles"]=new(start,Offset()-start,true,true,count);
            if(!Records("Regene",20,doc.Regene,()=>{var p=Point(r);float az=r.ReadSingle(),ax=r.ReadSingle();return new(p,ax,az);})) return Finish();
            Records("Warps",320,doc.Warps,()=>
            {
                short id=r.ReadInt16(); string name=Fixed(r,32),announcement=Fixed(r,256); r.ReadUInt16(); uint fee=r.ReadUInt32(); short zone=r.ReadInt16(); r.ReadUInt16(); var p=Point(r); float radius=r.ReadSingle(); short nation=r.ReadInt16(); r.ReadUInt16(); return new(id,name,announcement,fee,zone,p,radius,nation);
            });
        }
        catch(EndOfStreamException)
        { if(doc.Heights==null) throw new InvalidDataException("SMD terrain is truncated."); doc.Diagnostics.Add("Truncated SMD: only independently bounded known records can be edited; missing bytes are retained as an opaque tail."); }
        return Finish();
        bool Records<T>(string name,int stride,List<T> list,Func<T> read) where T:SmdRecord
        {
            int start=Offset(),n=Count(SourceLimits.MaximumJsonRows); int take=(int)Math.Min(n,(stream.Length-stream.Position)/stride);
            for(int i=0;i<take;i++){var value=read();value.SourceOrdinal=i;list.Add(value);}
            bool complete=take==n; doc.Sections[name]=new(start,Offset()-start,complete,complete||take>0,n);
            if(!complete) doc.Diagnostics.Add(name+" is truncated: complete records can be edited; row count/order cannot change."); return complete;
        }
        SmdDocument Finish()
        {
            doc.InitialObjects=doc.Objects.ToList(); doc.InitialRegene=doc.Regene.ToList(); doc.InitialWarps=doc.Warps.ToList();
            if(doc.Warps.GroupBy(x=>x.Id).Any(g=>g.Key>0 && g.Count()>1)) doc.Diagnostics.Add("Duplicate warp IDs: the stock reader uses the first positive ID. Rows retain source order.");
            if(doc.Objects.Count>0) doc.Diagnostics.Add("SMD object events take precedence over matching database map objects. Duplicate positive IDs use the first source record.");
            return doc;
        }
    }
    static MapPoint Point(BinaryReader r)=>new(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());
    static string Fixed(BinaryReader r,int length)=>Encoding.ASCII.GetString(r.ReadBytes(length)).TrimEnd('\0');
    public static byte[] Write(SmdDocument doc)
    {
        var issues=SmdValidation.Validate(doc); if(issues.Count>0) throw new InvalidDataException(string.Join("; ",issues.Select(x=>x.Field+": "+x.Message)));
        var patches=new List<(int Offset,int Length,byte[] Data)>();
        void Patch(string name,byte[] data)
        { var section=doc.Sections[name]; if(!data.AsSpan().SequenceEqual(doc.Original.AsSpan(section.Offset,section.Length))) patches.Add((section.Offset,section.Length,data)); }
        if(!doc.Heights.Heights.SequenceEqual(doc.OriginalHeights.Heights))
        { var raw=doc.Original.AsSpan(0,doc.Sections["Terrain"].Length).ToArray(); for(int i=0;i<doc.Heights.Heights.Length;i++) Put(raw,8+i*4,doc.Heights.Heights[i]); Patch("Terrain",raw); }
        if(doc.CollisionCandidate.Length>0) Patch("Collision",doc.CollisionCandidate);
        if(!doc.EventTiles.SequenceEqual(doc.InitialTiles))
        { var raw=new byte[doc.EventTiles.Length*2]; for(int i=0;i<doc.EventTiles.Length;i++) BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(i*2),doc.EventTiles[i]); Patch("Tiles",raw); }
        Rows("Objects",24,doc.Objects,doc.InitialObjects,(raw,v,b)=>
        { Put(raw,0,v.Belong); Put(raw,4,v.Index); Put(raw,6,v.Type); Put(raw,8,v.ControlTarget); Put(raw,10,v.Status); Point(raw,12,v.Position); });
        Rows("Regene",20,doc.Regene,doc.InitialRegene,(raw,v,b)=>{Point(raw,0,v.Position);Put(raw,12,v.AreaZ);Put(raw,16,v.AreaX);});
        Rows("Warps",320,doc.Warps,doc.InitialWarps,(raw,v,b)=>
        {
            Put(raw,0,v.Id); if(b==null||v.Name!=b.Name) Fixed(raw,2,32,v.Name); if(b==null||v.Announcement!=b.Announcement) Fixed(raw,34,256,v.Announcement);
            BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(292),v.Fee); Put(raw,296,v.Zone); Point(raw,300,v.Position); Put(raw,312,v.Radius); Put(raw,316,v.Nation);
        });
        using var output=new MemoryStream(); int offset=0;
        foreach(var p in patches.OrderBy(x=>x.Offset)) { output.Write(doc.Original,offset,p.Offset-offset); output.Write(p.Data); offset=p.Offset+p.Length; }
        output.Write(doc.Original,offset,doc.Original.Length-offset); SourceLimits.CheckedLength(output.Length); return output.ToArray();
        void Rows<T>(string name,int stride,List<T> values,List<T> initial,Action<byte[],T,T?> write) where T:SmdRecord
        {
            if(values.SequenceEqual(initial)) return;
            var s=doc.Sections[name]; using var bytes=new MemoryStream(); using var w=new BinaryWriter(bytes); w.Write(s.Complete?values.Count:s.DeclaredCount);
            for(int i=0;i<values.Count;i++)
            {
                int origin=values[i].SourceOrdinal;if(origin>=initial.Count)throw new InvalidDataException("SMD row origin is outside its captured section.");
                var raw=origin>=0?doc.Original.AsSpan(s.Offset+4+origin*stride,stride).ToArray():new byte[stride];
                if(origin<0 || !Equals(initial[origin],values[i])) write(raw,values[i],origin>=0?initial[origin]:null);
                w.Write(raw);
            }
            Patch(name,bytes.ToArray());
        }
    }
    internal static void Put(byte[] raw,int offset,float value)=>BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(offset),BitConverter.SingleToInt32Bits(value));
    static void Put(byte[] raw,int offset,int value)=>BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(offset),value);
    static void Put(byte[] raw,int offset,short value)=>BinaryPrimitives.WriteInt16LittleEndian(raw.AsSpan(offset),value);
    static void Point(byte[] raw,int offset,MapPoint p) { Put(raw,offset,p.X);Put(raw,offset+4,p.Y);Put(raw,offset+8,p.Z); }
    static void Fixed(byte[] raw,int offset,int length,string value) { raw.AsSpan(offset,length).Clear(); Encoding.ASCII.GetBytes(value).CopyTo(raw,offset); }
}
