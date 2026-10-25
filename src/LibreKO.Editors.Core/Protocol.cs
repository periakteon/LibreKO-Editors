using System.Text;
using System.Text.Json;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core;
public static class Protocol {
 public const int Limit=4*1024*1024;
 public static void ValidateOutbound(string frame) { if(Encoding.UTF8.GetByteCount(frame)>Limit)throw new InvalidDataException("Message exceeds 4 MiB. File bytes must use scoped blob tickets."); }
 public static async Task<string?> ReadFrameAsync(Stream stream,CancellationToken ct=default) { using var buffer=new MemoryStream(); var b=new byte[1]; while(await stream.ReadAsync(b,ct)>0) { if(b[0]==10)return Encoding.UTF8.GetString(buffer.ToArray()).TrimEnd('\r'); if(buffer.Length>=Limit)throw new InvalidDataException("Message exceeds 4 MiB.");buffer.WriteByte(b[0]); }return buffer.Length==0?null:Encoding.UTF8.GetString(buffer.ToArray()); }
 public static string HandlePing(string frame) {var r=JsonSerializer.Deserialize<RpcRequest>(frame,JsonConfig.Options)!;return JsonSerializer.Serialize(new RpcResponse(1,r.Id,r.Version==1?JsonSerializer.SerializeToElement(new{ready=true}):null,r.Version==1?null:new("protocol","Unsupported protocol version")),JsonConfig.Options);}
}

