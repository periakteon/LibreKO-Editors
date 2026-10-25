using System.Text;
public static class SmdFixtures
{
    public static byte[] Create()
    {
        using var stream=new MemoryStream(); using var w=new BinaryWriter(stream);
        w.Write(5); w.Write(8f); for(int x=0;x<5;x++) for(int z=0;z<5;z++) w.Write(x*100f+z*10f);
        w.Write(32f); w.Write(32f); w.Write(1);
        foreach(var v in new[]{2f,1f,2f,3f,1f,2f,2f,1f,3f}) w.Write(v);
        for(int cell=0;cell<4;cell++)
        {
            w.Write(cell==0?0x1234u:0u); if(cell!=0) continue;
            w.Write(1); w.Write((ushort)27);
            for(int sub=0;sub<16;sub++) { w.Write(sub==0?1:0); if(sub==0) { w.Write(0u); w.Write(1u); w.Write(2u); } }
        }
        w.Write(2);
        for(int i=0;i<2;i++) { w.Write(99+i); w.Write((short)11); w.Write((short)2); w.Write((short)88); w.Write((short)1); w.Write(1f+i); w.Write(2f); w.Write(3f); }
        for(int x=0;x<5;x++) for(int z=0;z<5;z++) w.Write((short)(x*10+z+1));
        w.Write(1); w.Write(4f); w.Write(5f); w.Write(6f); w.Write(7f); w.Write(8f); w.Write(2);
        for(int i=0;i<2;i++)
        {
            w.Write((short)7); Fixed(w,i==0?"Original warp":"Duplicate warp",32); Fixed(w,"Announcement",256); w.Write((ushort)0x1234);
            w.Write(uint.MaxValue); w.Write((short)21); w.Write((ushort)0x5678); w.Write(9f+i); w.Write(10f); w.Write(11f); w.Write(12f); w.Write((short)2); w.Write((ushort)0x9abc);
        }
        w.Write(new byte[]{0xfa,0xce,0x55,0x77}); return stream.ToArray();
    }
    static void Fixed(BinaryWriter writer,string text,int length) { var bytes=new byte[length]; Encoding.ASCII.GetBytes(text).CopyTo(bytes,0); writer.Write(bytes); }
}
