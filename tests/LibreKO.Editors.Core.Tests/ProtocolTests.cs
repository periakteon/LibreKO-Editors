using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
public class ProtocolTests
{
 [Fact] public void RoundTripKeepsInt64AndUnicode() { var row = new RecordSnapshot(new(RecordKind.Character,1),new() { ["Experience"]=9007199254740993L, ["Name"]="Şövalye" },"v"); var copy=JsonSerializer.Deserialize<RecordSnapshot>(JsonSerializer.Serialize(row,JsonConfig.Options),JsonConfig.Options)!; Assert.Equal(9007199254740993L,copy.Values["Experience"]!.GetValue<long>()); Assert.Equal("Şövalye",copy.Values["Name"]!.GetValue<string>()); }
 [Fact] public async Task RejectsOversizedFrames() { using var s=new MemoryStream(Encoding.UTF8.GetBytes(new string('a',4*1024*1024+1)+"\n")); await Assert.ThrowsAsync<InvalidDataException>(()=>Protocol.ReadFrameAsync(s)); }
 [Fact] public void RejectsUnsupportedVersion() { var r=JsonSerializer.Deserialize<RpcResponse>(Protocol.HandlePing(JsonSerializer.Serialize(new RpcRequest(2,Guid.NewGuid(),"ping",JsonSerializer.SerializeToElement(new{})))),JsonConfig.Options)!; Assert.Equal("protocol",r.Error!.Code); }
 [Fact] public async Task ReadsTwoFramesSeparately() { using var s=new MemoryStream(Encoding.UTF8.GetBytes("first\nsecond\n")); Assert.Equal("first",await Protocol.ReadFrameAsync(s)); Assert.Equal("second",await Protocol.ReadFrameAsync(s)); Assert.Null(await Protocol.ReadFrameAsync(s)); }
}

