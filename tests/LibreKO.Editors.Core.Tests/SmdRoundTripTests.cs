using System.Buffers.Binary;
using LibreKO.Editors.Core.Maps;
public class SmdRoundTripTests
{
    [Fact]
    public void SmdNoOpRetainsPaddingDuplicatesCollisionAndTail()
    {
        var input=SmdFixtures.Create(); var doc=SmdCodec.Read(input);
        Assert.Equal(input,SmdCodec.Write(SmdCodec.Read(input))); Assert.Equal(2,doc.Warps.Count(w=>w.Id==7));
        var original=doc.Warps[0]; doc.Warps[0]=original with{Name="Edited warp"};
        var edited=SmdCodec.Write(doc); var span=doc.Sections["Warps"];
        Assert.Equal(input[..(span.Offset+6)],edited[..(span.Offset+6)]);
        Assert.Equal(input[(span.Offset+38)..],edited[(span.Offset+38)..]);
        Assert.Equal(uint.MaxValue,SmdCodec.Read(edited).Warps[0].Fee);
        doc.Warps[0]=original with{Name="Ğ"}; Assert.NotEmpty(SmdValidation.Validate(doc)); Assert.Throws<InvalidDataException>(()=>SmdCodec.Write(doc));
        doc.Warps[0]=original with{Name=new string('x',33)}; Assert.NotEmpty(SmdValidation.Validate(doc));
        doc.Warps[0]=original with{Radius=float.NaN}; Assert.NotEmpty(SmdValidation.Validate(doc));
    }
    [Fact]
    public void PartialSectionsCanOnlyChangeIndependentlyBoundedRecords()
    {
        var complete=SmdFixtures.Create(); var full=SmdCodec.Read(complete);
        var input=complete[..(full.Sections["Warps"].Offset+4+320+17)]; var doc=SmdCodec.Read(input);
        Assert.Equal(input,SmdCodec.Write(doc)); Assert.False(doc.Sections["Warps"].Complete); Assert.Single(doc.Warps);
        doc.Warps[0]=doc.Warps[0] with{Fee=123}; Assert.Equal(123u,SmdCodec.Read(SmdCodec.Write(doc)).Warps[0].Fee);
        doc.Warps.Add(doc.Warps[0]); Assert.Throws<InvalidDataException>(()=>SmdCodec.Write(doc));
        var overflow=complete.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(overflow, int.MaxValue); Assert.Throws<InvalidDataException>(()=>SmdCodec.Read(overflow));
        var count=complete.ToArray(); BinaryPrimitives.WriteInt32LittleEndian(count.AsSpan(full.Sections["Objects"].Offset),int.MaxValue); Assert.Throws<InvalidDataException>(()=>SmdCodec.Read(count));
    }
}
