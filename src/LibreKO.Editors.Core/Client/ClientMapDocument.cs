using System.Numerics;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Client;
public sealed class ClientMapDocument
{
    public JsonSourceDocument Json{get;}
    ClientMapDocument(JsonSourceDocument json){Json=json;}
    public static ClientMapDocument Open(SourceFileSnapshot snapshot,byte[] bytes)=>new(JsonSourceDocument.Open(snapshot,bytes));
    public static ClientMapDocument OpenDraft(SourceFileSnapshot snapshot,byte[] bytes)=>new(JsonSourceDocument.OpenDraft(snapshot,bytes));
    public void Transform(SourceRowToken token,MapPoint position,float[] quaternion,MapPoint scale)
    {
        var fx=Json.Snapshot.Key.RelativePath.EndsWith("fx_placements.json",StringComparison.Ordinal);Json.Set(token,"p",Array(position));Json.Set(token,fx?"q":"r",new JsonArray(quaternion.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()));
        Json.Set(token,"s",fx?JsonValue.Create(scale.X):Array(scale));
    }
    public byte[] Render()=>Json.Render();
    public FileMutation Build(SourceStore store)=>new(Json.Snapshot,store.Blobs.Put(Render()));
    public static JsonArray Array(MapPoint p)=>new(p.X,p.Y,p.Z);
    public static Quaternion Quaternion(float[] values)
    {
        if(values.Length!=4||values.Any(x=>!float.IsFinite(x)))throw new InvalidDataException("Rotation needs four finite quaternion components.");
        var q=new Quaternion(values[0],values[1],values[2],values[3]);var length=q.LengthSquared();if(!float.IsFinite(length)||length<1e-12f)throw new InvalidDataException("A zero/overflowed quaternion cannot define rotation.");return System.Numerics.Quaternion.Normalize(q);
    }
}
