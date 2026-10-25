using System.Text.Json.Nodes;
using System.Buffers.Binary;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Client;
public static class AssetDependencyAudit
{
    public static bool IsModel(SourceFileKey key)=>key.Root==SourceRoot.ClientProject&&Path.GetExtension(key.RelativePath) is ".glb" or ".gltf";
    public static bool Accepts(SourceFileKey key)=>key.Root==SourceRoot.ClientProject&&key.RelativePath.StartsWith("assets/",StringComparison.Ordinal)&&!ClientTerrainCatalog.Accepts(key)&&!ClientMapCatalog.Accepts(key)&&Path.GetExtension(key.RelativePath) is ".glb" or ".gltf" or ".png" or ".bin";
    public static IReadOnlyList<ValidationIssue> ValidateStandalone(SourceFileKey key,byte[] bytes)
    {try{if(!Accepts(key))throw new InvalidDataException("Unsupported compatible asset path.");SourceLimits.CheckedLength(bytes.Length);switch(Path.GetExtension(key.RelativePath)){case ".glb":GltfAssetDocument.Read(bytes,true);break;case ".gltf":GltfAssetDocument.Read(bytes,false);break;case ".png":RgbaPngCodec.Decode(bytes);break;case ".bin":if(bytes.Length==0)throw new InvalidDataException("An imported buffer cannot be empty.");break;}return [];}catch(Exception e)when(Expected(e)){return [new(key.RelativePath,e.Message)];}}
    public static IReadOnlyList<ValidationIssue> Validate(SourceProfile profile,SourceFileKey key,BlobRef candidate,SourceStore store,IReadOnlyDictionary<SourceFileKey,BlobRef?>? overlay=null)
    {
        try
        {
            if(profile.Id!=store.Profile.Id)throw new InvalidDataException("Asset profile does not match the blob store.");store.GuardKey(key);var bytes=store.Blobs.Read(candidate);var syntax=NpcVisualCatalog.Accepts(key)?NpcVisualCatalog.Validate(key,bytes):ValidateStandalone(key,bytes);if(syntax.Count>0)return syntax;
            if(IsModel(key)){Model(key,bytes);return [];}
            if(!NpcVisualCatalog.Accepts(key))return [];
            var root=JsonNode.Parse(TextDocument.Decode(bytes).Text)!.AsObject();string name=Path.GetFileName(key.RelativePath);
            if(name=="index.json")
            {
                JsonObject? original=null;var before=store.Read(key);if(before.Content!=null)try{original=JsonNode.Parse(TextDocument.Decode(store.Blobs.Read(before.Content)).Text)!.AsObject();}catch{}
                foreach(var pair in root.Where(p=>int.TryParse(p.Key,out _))){var stem=NpcVisualCatalog.Stem(pair.Value,true);if(stem.Length==0)continue;var modelKey=store.Key(key.Root,"assets/npcs/"+stem+".glb");if(original==null||!JsonNode.DeepEquals(original[pair.Key],pair.Value)||overlay?.ContainsKey(modelKey)==true)Model(modelKey,Get(modelKey));}
            }
            else if(name=="role_fx.json"){foreach(var group in new[]{"npcs","questMarkers"})foreach(var pair in (root[group] as JsonObject??[]).Where(p=>group!="npcs"||int.TryParse(p.Key,out _))){var stem=NpcVisualCatalog.Stem(pair.Value,true);if(stem.Length>0)Effect(stem);}}
            else if(name!="plug_slots.json")
            {
                var suffix=name.EndsWith(".anim.json",StringComparison.Ordinal)?".anim.json":name.EndsWith(".plugs.json",StringComparison.Ordinal)?".plugs.json":".fxplug.json";var stem=key.RelativePath[..^suffix.Length];var modelKey=store.Key(key.Root,stem+".glb");var model=Model(modelKey,Get(modelKey));
                if(suffix==".anim.json")foreach(var value in root["animations"]!.AsArray()){string clip=value!["exported"]!.GetValue<string>();if(clip.Length>0&&!model.Clips.Contains(clip))throw new InvalidDataException("Animation clip is missing in the selected loose model: "+clip);}
                else if(suffix==".plugs.json")foreach(var value in root["plugs"]!.AsArray())
                {var row=value!.AsObject();Bone(row["joint"]!.GetValue<int>(),model);var plug=store.Key(key.Root,"assets/npcs/plugs/"+NpcVisualCatalog.Stem(row["model"])+".glb");var plugModel=Model(plug,Get(plug));if(row["anim"]?.GetValue<string>() is string clip&&clip.Length>0&&!plugModel.Clips.Contains(clip))throw new InvalidDataException("Plug animation is missing: "+clip);}
                else foreach(var value in root["parts"]!.AsArray()){var row=value!.AsObject();if(row["bone"] is JsonNode bone&&bone.GetValue<int>()>=0)Bone(bone.GetValue<int>(),model);Effect(NpcVisualCatalog.Stem(row["fx"]));}
            }return [];
            byte[] Get(SourceFileKey target){if(overlay!=null&&overlay.TryGetValue(target,out var blob)){if(blob==null)throw new InvalidDataException("Dependency is deleted in this review: "+target.RelativePath);return store.Blobs.Read(blob);}var s=store.Read(target);if(s.Content==null)throw new InvalidDataException("Missing loose dependency: "+target.RelativePath);return store.Blobs.Read(s.Content);}
            void Effect(string stem){var target=store.Key(key.Root,"assets/fx/"+stem+".json");var data=Get(target);if(JsonNode.Parse(TextDocument.Decode(data).Text) is not JsonObject)throw new InvalidDataException("Effect source is not a supported object: "+stem);var graph=ClientResourceGraph.Validate(target.RelativePath,data,p=>Get(store.Key(target.Root,p)));if(graph.Count>0)throw new InvalidDataException(string.Join("; ",graph.Select(i=>i.Message)));}
            GltfAssetDocument Model(SourceFileKey target,byte[] data)
            {
                var model=GltfAssetDocument.Read(data,target.RelativePath.EndsWith(".glb",StringComparison.Ordinal));string dir=Path.GetDirectoryName(target.RelativePath)!.Replace('\\','/');
                byte[] Uri(string uri){if(uri.StartsWith("data:",StringComparison.Ordinal))return GltfAssetDocument.DataUri(uri);GltfAssetDocument.ValidateUri(uri);return Get(store.Key(target.Root,dir+"/"+uri));}
                var buffers=(model.Root["buffers"] as JsonArray??[]).Select((v,i)=>{var row=v!.AsObject();var buffer=row["uri"] is JsonNode u?Uri(u.GetValue<string>()):model.Binary!;int required=row["byteLength"]!.GetValue<int>();if(buffer.Length<required||buffer.Length-required>3)throw new InvalidDataException("Buffer length does not match its declaration.");return buffer;}).ToArray();
                foreach(var accessor in model.Root["accessors"] as JsonArray??[])
                {
                    if(accessor!["componentType"]!.GetValue<int>()!=5126)continue;var view=model.Root["bufferViews"]![accessor["bufferView"]!.GetValue<int>()]!;var buffer=buffers[view["buffer"]!.GetValue<int>()];int parts=accessor["type"]!.ToString() switch{"SCALAR"=>1,"VEC2"=>2,"VEC3"=>3,"VEC4" or "MAT2"=>4,"MAT3"=>9,"MAT4"=>16,_=>0};int start=(view["byteOffset"]?.GetValue<int>()??0)+(accessor["byteOffset"]?.GetValue<int>()??0),stride=view["byteStride"]?.GetValue<int>()??parts*4,count=accessor["count"]!.GetValue<int>();for(int i=0;i<count;i++)for(int component=0;component<parts;component++)if(!float.IsFinite(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(checked(start+i*stride+component*4))))))throw new InvalidDataException("Nonfinite float data in model accessor.");
                }
                foreach(var image in model.Root["images"] as JsonArray??[])
                {
                    byte[] pixels;if(image!["uri"] is JsonNode u)pixels=Uri(u.GetValue<string>());else{var v=model.Root["bufferViews"]![image["bufferView"]!.GetValue<int>()]!;pixels=buffers[v["buffer"]!.GetValue<int>()].AsSpan(v["byteOffset"]?.GetValue<int>()??0,v["byteLength"]!.GetValue<int>()).ToArray();}RgbaPngCodec.Decode(pixels);
                }return model;
            }
        }catch(Exception e)when(Expected(e)){return [new(key.RelativePath,e.Message)];}
    }
    static void Bone(int index,GltfAssetDocument model){if(index<0||index>=model.BoneCount)throw new InvalidDataException("Attachment bone "+index+" is absent from the selected skeleton ("+model.BoneCount+" bones).");}
    public static IReadOnlyList<SourceFileKey> Dependents(SourceStore store,SourceFileKey target)
    {
        store.GuardKey(target);var result=new HashSet<SourceFileKey>();var profile=store.Profile;var root=profile.Roots[SourceRoot.ClientProject];
        var dir=Path.GetDirectoryName(target.RelativePath)!.Replace('\\','/');
        for(var relative=dir;relative.StartsWith("assets",StringComparison.Ordinal);relative=Path.GetDirectoryName(relative)?.Replace('\\','/')??"")
        {
            string path=SourcePaths.Resolve(profile,store.Key(SourceRoot.ClientProject,relative),false);if(Directory.Exists(path))foreach(var file in Directory.EnumerateFiles(path,"*",new EnumerationOptions{AttributesToSkip=FileAttributes.ReparsePoint}))
            {
                var key=store.Key(SourceRoot.ClientProject,relative+"/"+Path.GetFileName(file));if(!IsModel(key)||key==target)continue;
                try{var s=store.Read(key);var model=GltfAssetDocument.Read(store.Blobs.Read(s.Content!),key.RelativePath.EndsWith(".glb",StringComparison.Ordinal));if(model.ExternalUris.Any(uri=>(relative+"/"+uri).Equals(target.RelativePath,StringComparison.OrdinalIgnoreCase)))result.Add(key);}catch(Exception e)when(Expected(e)){/* Unknown custom formats are not certified by this adapter. */}
            }
            if(relative=="assets")break;
        }
        if(IsModel(target)&&target.RelativePath.StartsWith("assets/npcs/",StringComparison.Ordinal))
        {
            var index=store.Key(target.Root,"assets/npcs/index.json");var s=store.Read(index);if(s.Content!=null)try{var obj=JsonNode.Parse(TextDocument.Decode(store.Blobs.Read(s.Content)).Text)!.AsObject();if(obj.Where(p=>int.TryParse(p.Key,out _)).Any(p=>("assets/npcs/"+p.Value+".glb").Equals(target.RelativePath,StringComparison.OrdinalIgnoreCase)))result.Add(index);}catch(Exception e)when(Expected(e)){}
            foreach(var suffix in new[]{".anim.json",".plugs.json",".fxplug.json"}){var key=store.Key(target.Root,Path.ChangeExtension(target.RelativePath,null)+suffix);if(store.Read(key).Exists)result.Add(key);}
            var npcs=Path.Combine(root,"assets","npcs");if(Directory.Exists(npcs))foreach(var file in Directory.EnumerateFiles(npcs,"*.plugs.json",new EnumerationOptions{AttributesToSkip=FileAttributes.ReparsePoint}))try{var key=store.Key(target.Root,"assets/npcs/"+Path.GetFileName(file));var source=store.Read(key);var row=JsonNode.Parse(TextDocument.Decode(store.Blobs.Read(source.Content!)).Text);if(row?["plugs"] is JsonArray plugs&&plugs.Any(p=>("assets/npcs/plugs/"+p?["model"]+".glb").Equals(target.RelativePath,StringComparison.OrdinalIgnoreCase)))result.Add(key);}catch(Exception e)when(Expected(e)){}
        }return result.ToArray();
    }
    internal static bool Expected(Exception e)=>e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException or FormatException or OverflowException;
}
