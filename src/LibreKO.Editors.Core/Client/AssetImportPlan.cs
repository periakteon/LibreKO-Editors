using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Client;
public static class AssetImportPlan
{
    public static FileChange Prepare(SourceProfile profile,string inputPath,SourceFileKey destination,SourceStore store)
    {
        try
        {
            if(!AssetDependencyAudit.Accepts(destination)||Path.GetExtension(destination.RelativePath)==".bin")throw new InvalidDataException("Import a compatible model or RGBA8 PNG asset; buffers enter only as model dependencies.");
            store.GuardKey(destination);SourcePaths.Resolve(profile,destination,true);inputPath=Path.GetFullPath(inputPath);SourcePaths.AssertNoLinks(inputPath);string inputRoot=Path.GetDirectoryName(inputPath)!;var files=new Dictionary<SourceFileKey,FileMutation>();var origins=new Dictionary<SourceFileKey,string>();long total=0;
            Read(inputPath,destination,true);var overlay=files.ToDictionary(p=>p.Key,p=>p.Value.After);
            foreach(var file in files.Values){var issues=AssetDependencyAudit.Validate(profile,file.Before.Key,file.After!,store,overlay);if(issues.Count>0)throw new InvalidDataException(string.Join("; ",issues.Select(x=>x.Field+": "+x.Message)));}return new(Guid.NewGuid(),files.Values.ToArray());
            void Read(string path,SourceFileKey key,bool sidecars=false)
            {
                path=Path.GetFullPath(path);SourcePaths.AssertNoLinks(path);if(!path.StartsWith(inputRoot+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Import dependency escapes its selected directory.");
                if(origins.TryGetValue(key,out var origin)){if(!string.Equals(origin,path,StringComparison.Ordinal))throw new InvalidDataException("Two import sources map to the same resource identity.");return;}
                if(files.Count>=256)throw new InvalidDataException("An import plan is limited to256 files.");using var stream=File.OpenRead(path);var data=new byte[SourceLimits.CheckedLength(stream.Length)];stream.ReadExactly(data);total+=data.Length;if(total>SourceLimits.MaximumFileBytes)throw new InvalidDataException("An import plan is limited to512MiB of source bytes.");
                bool convert=Path.GetExtension(path).Equals(".gltf",StringComparison.OrdinalIgnoreCase)&&key.RelativePath.EndsWith(".glb",StringComparison.Ordinal);if(convert)data=GltfAssetDocument.WrapGlb(data);
                var syntax=NpcVisualCatalog.Accepts(key)?NpcVisualCatalog.Validate(key,data):AssetDependencyAudit.ValidateStandalone(key,data);if(syntax.Count>0)throw new InvalidDataException(string.Join("; ",syntax.Select(x=>x.Message)));
                var before=store.Read(key);files.Add(key,new(before,store.Blobs.Put(data)));origins.Add(key,path);
                if(AssetDependencyAudit.IsModel(key))
                {
                    var model=GltfAssetDocument.Read(data,key.RelativePath.EndsWith(".glb",StringComparison.Ordinal));string dir=Path.GetDirectoryName(key.RelativePath)!.Replace('\\','/');foreach(var uri in model.ExternalUris){GltfAssetDocument.ValidateUri(uri);Read(Path.Combine(Path.GetDirectoryName(path)!,uri),store.Key(key.Root,dir+"/"+uri));}
                    if(sidecars)foreach(var suffix in new[]{".anim.json",".plugs.json",".fxplug.json"}){var file=Path.ChangeExtension(path,null)+suffix;if(File.Exists(file))Read(file,store.Key(key.Root,Path.ChangeExtension(key.RelativePath,null)+suffix));}
                }
                else if(key.RelativePath.EndsWith(".plugs.json",StringComparison.Ordinal))
                {
                    var root=System.Text.Json.Nodes.JsonNode.Parse(TextDocument.Decode(data).Text)!;foreach(var plug in root["plugs"]!.AsArray())
                    {string stem=NpcVisualCatalog.Stem(plug!["model"]);var file=Path.Combine(inputRoot,"plugs",stem+".glb");if(File.Exists(file))Read(file,store.Key(key.Root,"assets/npcs/plugs/"+stem+".glb"),true);}
                }
            }
        }catch(Exception e)when(AssetDependencyAudit.Expected(e)){throw new InvalidDataException("Import preparation refused: "+e.Message,e);}
    }
}
