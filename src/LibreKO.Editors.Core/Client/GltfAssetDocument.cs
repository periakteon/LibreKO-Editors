using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Client;
public sealed class GltfAssetDocument
{
    public JsonObject Root{get;}public byte[]? Binary{get;}public IReadOnlyList<string> ExternalUris{get;}
    public IReadOnlySet<string> Clips{get;}public int BoneCount{get;}
    GltfAssetDocument(JsonObject root,byte[]? binary)
    {
        Root=root;Binary=binary;if(root["asset"]?["version"]?.GetValue<string>()!="2.0")throw new InvalidDataException("Compatible assets require glTF2.0.");
        var uris=new HashSet<string>(StringComparer.Ordinal);var folded=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var group in new[]{"buffers","images"})foreach(var entry in List(group))if(entry?["uri"] is JsonNode uriNode)
        {var uri=uriNode.GetValue<string>();if(uri.StartsWith("data:",StringComparison.Ordinal)){DataUri(uri);continue;}ValidateUri(uri);if(folded.TryGetValue(uri,out var other)&&other!=uri)throw new InvalidDataException("Dependency URI identities differ only by case.");folded[uri]=uri;uris.Add(uri);}
        if(uris.Count>256)throw new InvalidDataException("Compatible imports are limited to256 external dependencies.");ExternalUris=uris.ToArray();
        var clips=new HashSet<string>(StringComparer.Ordinal);foreach(var animation in List("animations")){var name=animation?["name"]?.GetValue<string>()??"";if(name.Length>0&&!clips.Add(name))throw new InvalidDataException("Duplicate animation resource identity: "+name);}Clips=clips;
        var nodes=List("nodes");foreach(var node in nodes)
        {
            if(node is not JsonObject obj)throw new InvalidDataException("Invalid glTF node.");foreach(var field in new[]{"translation","scale"})if(obj[field]!=null)ClientMapCatalog.Vector(obj[field],3);if(obj["rotation"]!=null)ClientMapDocument.Quaternion(ClientMapCatalog.Vector(obj["rotation"],4));if(obj["matrix"]!=null)ClientMapCatalog.Vector(obj["matrix"],16);
            if(obj["children"] is JsonArray children)foreach(var child in children)Index(child,nodes.Count,"child node");if(obj["mesh"]!=null)Index(obj["mesh"],List("meshes").Count,"mesh");if(obj["skin"]!=null)Index(obj["skin"],List("skins").Count,"skin");
        }
        var visiting=new HashSet<int>();var visited=new HashSet<int>();for(int i=0;i<nodes.Count;i++)Visit(i,0);
        void Visit(int i,int depth){if(depth>256||!visiting.Add(i))throw new InvalidDataException("Node hierarchy is cyclic or too deep.");if(!visited.Contains(i)&&nodes[i]?["children"] is JsonArray children)foreach(var child in children)Visit(child!.GetValue<int>(),depth+1);visiting.Remove(i);visited.Add(i);}
        foreach(var skin in List("skins"))
        {
            if(skin?["joints"] is not JsonArray joints||joints.Count==0)throw new InvalidDataException("A skin needs a nonempty joint list.");var names=new HashSet<string>(StringComparer.Ordinal);var indices=new HashSet<int>();foreach(var joint in joints){int i=Index(joint,nodes.Count,"joint");if(!indices.Add(i))throw new InvalidDataException("Duplicate skeleton joint index.");var name=nodes[i]?["name"]?.GetValue<string>();if(!string.IsNullOrEmpty(name)&&!names.Add(name))throw new InvalidDataException("Duplicate skeleton bone identity: "+name);}BoneCount=Math.Max(BoneCount,joints.Count);if(skin["skeleton"]!=null)Index(skin["skeleton"],nodes.Count,"skeleton root");if(skin["inverseBindMatrices"]!=null)Index(skin["inverseBindMatrices"],List("accessors").Count,"bind accessor");
        }
        var buffers=List("buffers");for(int i=0;i<buffers.Count;i++){int length=Nonnegative(buffers[i]?["byteLength"],"buffer byteLength");if(buffers[i]?["uri"]==null&&(i!=0||binary==null||binary.Length<length||binary.Length-length>3))throw new InvalidDataException("Embedded buffer does not match the GLB BIN chunk.");}
        var views=List("bufferViews");foreach(var view in views){int buffer=Index(view?["buffer"],buffers.Count,"buffer");long offset=Nonnegative(view?["byteOffset"]??JsonValue.Create(0),"view offset"),length=Nonnegative(view?["byteLength"],"view length");if(offset+length>Nonnegative(buffers[buffer]?["byteLength"],"buffer length"))throw new InvalidDataException("Buffer view exceeds its buffer.");}
        var accessors=List("accessors");foreach(var accessor in accessors)
        {
            if(accessor is not JsonObject a||a["sparse"]!=null)throw new InvalidDataException("Sparse/custom accessors need conversion in an external model tool.");int count=Nonnegative(a["count"],"accessor count"),component=a["componentType"]!.GetValue<int>();int bytes=component switch{5120 or 5121=>1,5122 or 5123=>2,5125 or 5126=>4,_=>throw new InvalidDataException("Unsupported accessor component type.")};int parts=a["type"]?.GetValue<string>() switch{"SCALAR"=>1,"VEC2"=>2,"VEC3"=>3,"VEC4"=>4,"MAT2"=>4,"MAT3"=>9,"MAT4"=>16,_=>throw new InvalidDataException("Unsupported accessor type.")};
            int at=Index(a["bufferView"],views.Count,"accessor buffer view");var view=views[at]!;int stride=view["byteStride"]?.GetValue<int>()??bytes*parts;if(stride<bytes*parts||stride>252)throw new InvalidDataException("Invalid accessor stride.");long need=Nonnegative(a["byteOffset"]??JsonValue.Create(0),"accessor offset")+(count==0?0:(long)(count-1)*stride+bytes*parts);if(need>Nonnegative(view["byteLength"],"view length"))throw new InvalidDataException("Accessor exceeds its view.");
        }
        foreach(var mesh in List("meshes"))foreach(var primitive in mesh?["primitives"]?.AsArray()??throw new InvalidDataException("Mesh primitives are missing."))
        {if(primitive?["attributes"] is not JsonObject attributes||attributes["POSITION"]==null)throw new InvalidDataException("Mesh requires POSITION attributes.");foreach(var pair in attributes)Index(pair.Value,accessors.Count,"mesh attribute");if(primitive["indices"]!=null)Index(primitive["indices"],accessors.Count,"triangle indices");if(primitive["material"]!=null)Index(primitive["material"],List("materials").Count,"material");}
        foreach(var image in List("images")){if(image?["uri"]==null){Index(image?["bufferView"],views.Count,"image buffer view");if(image?["mimeType"]?.ToString()!="image/png")throw new InvalidDataException("Embedded textures require PNG conversion.");}}
        foreach(var animation in List("animations"))
        {var samplers=animation?["samplers"]?.AsArray()??throw new InvalidDataException("Animation samplers are missing.");foreach(var sampler in samplers){Index(sampler?["input"],accessors.Count,"animation input");Index(sampler?["output"],accessors.Count,"animation output");}foreach(var channel in animation?["channels"]?.AsArray()??throw new InvalidDataException("Animation channels are missing.")){Index(channel?["sampler"],samplers.Count,"animation sampler");Index(channel?["target"]?["node"],nodes.Count,"animation target");if(channel?["target"]?["path"]?.ToString() is not("translation" or "rotation" or "scale" or "weights"))throw new InvalidDataException("Unsupported animation target path.");}}
        if(root["extensionsRequired"] is JsonArray required&&required.Count>0)throw new InvalidDataException("Assets requiring glTF extensions need external conversion to the verified core glTF subset.");
    }
    JsonArray List(string key){if(Root[key]==null)return new();var list=Root[key]!.AsArray();if(list.Count>SourceLimits.MaximumJsonRows)throw new InvalidDataException("glTF array exceeds supported bound.");return list;}
    static int Index(JsonNode? value,int count,string label){int i=value?.GetValue<int>()??-1;if(i<0||i>=count)throw new InvalidDataException("Missing/out-of-range "+label+".");return i;}
    static int Nonnegative(JsonNode? value,string label){int i=value?.GetValue<int>()??-1;if(i<0||i>SourceLimits.MaximumFileBytes)throw new InvalidDataException("Invalid "+label+".");return i;}
    public static void ValidateUri(string uri){if(uri.Contains('%')||uri.Contains('#')||uri.Contains('?')||uri.Contains('\\'))throw new InvalidDataException("Use literal relative dependency URIs without escaping/query/fragment.");SourcePaths.ValidateRelative(uri);}
    public static byte[] DataUri(string uri){int comma=uri.IndexOf(',');if(comma<0||uri[..comma] is not("data:application/octet-stream;base64" or "data:application/gltf-buffer;base64" or "data:image/png;base64"))throw new InvalidDataException("Only bounded base64 buffers and PNG data URIs are supported.");SourceLimits.CheckedLength((uri.Length-comma)*3L/4);try{return Convert.FromBase64String(uri[(comma+1)..]);}catch(FormatException e){throw new InvalidDataException("Malformed data URI.",e);}}
    public static GltfAssetDocument Read(byte[] bytes,bool binary)
    {
        SourceLimits.CheckedLength(bytes.LongLength);byte[]? bin=null;byte[] json=bytes;
        if(binary)
        {
            if(bytes.Length<20||!bytes.AsSpan(0,4).SequenceEqual("glTF"u8)||BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4))!=2||BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8))!=bytes.Length)throw new InvalidDataException("Malformed GLB2 header/length.");
            int offset=12;bool first=true;json=[];while(offset<bytes.Length){if(bytes.Length-offset<8)throw new InvalidDataException("Truncated GLB chunk.");uint n=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset));uint type=BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset+4));offset+=8;if(n%4!=0||n>bytes.Length-offset)throw new InvalidDataException("Invalid GLB chunk length.");if(type==0x4e4f534a&&first)json=bytes.AsSpan(offset,(int)n).ToArray();else if(type==0x004e4942&&!first&&bin==null)bin=bytes.AsSpan(offset,(int)n).ToArray();else throw new InvalidDataException("Unsupported or duplicate GLB chunk.");offset+=(int)n;first=false;}if(json.Length==0)throw new InvalidDataException("GLB JSON must be the first chunk.");
        }
        var root=JsonNode.Parse(new UTF8Encoding(false,true).GetString(json),documentOptions:new(){MaxDepth=SourceLimits.MaximumJsonDepth}) as JsonObject??throw new InvalidDataException("A glTF object is required.");return new(root,bin);
    }
    public static byte[] WrapGlb(byte[] json){Read(json,false);int length=(json.Length+3)/4*4;var bytes=new byte[20+length];"glTF"u8.CopyTo(bytes);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),2);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8),bytes.Length);BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12),length);"JSON"u8.CopyTo(bytes.AsSpan(16));bytes.AsSpan(20).Fill((byte)' ');json.CopyTo(bytes,20);return bytes;}
}
