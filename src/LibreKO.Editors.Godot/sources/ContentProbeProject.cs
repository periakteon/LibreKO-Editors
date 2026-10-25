using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public static class ContentProbeProject
{
    static readonly ConcurrentDictionary<int,Process> children=new();public static IReadOnlyList<int> OwnedProcessIds=>children.Keys.ToArray();
    public static void StopOwned(){foreach(var child in children.Values)try{if(!child.HasExited){child.Kill(true);child.WaitForExit(2000);}}catch(InvalidOperationException){} }
    public static Task<IReadOnlyList<ValidationIssue>> VerifyAsync(ClientBuildResult result,AuthoringTools tools,CancellationToken ct)=>VerifyPlanAsync(BuildManifest.Read(result.ManifestPath).Plan,tools,ct);
    public static async Task<IReadOnlyList<ValidationIssue>> VerifyPlanAsync(ClientBuildPlan plan,AuthoringTools tools,CancellationToken ct)
    {
        var issues=new List<ValidationIssue>();try
        {
            BuildPaths.Guard(plan);var manifest=BuildManifest.Read(Path.Combine(plan.OutputRoot,"build-manifest.json"));if(manifest.Plan.Id!=plan.Id||manifest.Status is not("exported" or "verified"))throw new InvalidDataException("Export is not ready for an independent probe.");
            foreach(var output in manifest.Outputs){ct.ThrowIfCancellationRequested();var path=Path.Combine(plan.OutputRoot,output.RelativePath);SourcePaths.AssertNoLinks(path);if(new FileInfo(path).Length!=output.Length||BuildRunner.Hash(path)!=output.Sha256)throw new InvalidDataException("Export manifest output hash mismatch: "+output.RelativePath);}
            var root=Path.Combine(Path.GetDirectoryName(plan.SnapshotRoot)!,"probe-"+Guid.NewGuid().ToString("N"));SourcePaths.AssertNoLinks(root);Directory.CreateDirectory(root);var resources=new List<object>();
            foreach(var file in plan.SnapshotFiles)
            {
                ct.ThrowIfCancellationRequested();var key=new SourceFileKey(plan.SourceProfileId,SourceRoot.ClientProject,"snapshot",file.RelativePath);string path=Path.Combine(plan.SnapshotRoot,file.RelativePath);
                if(file.RelativePath.EndsWith(".json",StringComparison.Ordinal)||file.RelativePath.EndsWith(".bin",StringComparison.Ordinal)||file.RelativePath.EndsWith(".comp",StringComparison.Ordinal))resources.Add(new{path="res://"+file.RelativePath,kind="raw",hash=file.Sha256});
                if(AssetDependencyAudit.IsModel(key))
                {
                    var model=GltfAssetDocument.Read(File.ReadAllBytes(path),file.RelativePath.EndsWith(".glb",StringComparison.Ordinal));int vertices=0;foreach(var node in model.Root["nodes"] as JsonArray??[])if(node?["mesh"] is JsonNode mesh)foreach(var primitive in model.Root["meshes"]![mesh.GetValue<int>()]!["primitives"]!.AsArray())vertices+=model.Root["accessors"]![primitive!["attributes"]!["POSITION"]!.GetValue<int>()]!["count"]!.GetValue<int>();
                    resources.Add(new{path="res://"+file.RelativePath,kind="model",vertices,bones=model.BoneCount,clips=model.Clips.ToArray(),sourceHash=file.Sha256});
                }
                if(ClientTerrainCatalog.Accepts(key)&&file.RelativePath.EndsWith("heightmap.png",StringComparison.Ordinal))
                {var height=ClientHeightmapCodec.Decode(File.ReadAllBytes(path));resources.Add(new{path="res://"+file.RelativePath,kind="height",hash=file.Sha256,size=height.Size,samples=new[]{0,height.Heights.Length/2,height.Heights.Length-1}.Select(i=>new{index=i,bits=BitConverter.SingleToInt32Bits(height.Heights[i])}).ToArray()});}
                else if(!ClientTerrainCatalog.Accepts(key)&&file.RelativePath.EndsWith(".png",StringComparison.Ordinal))resources.Add(new{path="res://"+file.RelativePath,kind="texture"});
            }
            var sourceProject=File.ReadAllText(Path.Combine(plan.SnapshotRoot,"project.godot"));var globals=Regex.Match(sourceProject,@"(?ms)^\[shader_globals\].*?(?=^\[|\z)").Value;
            File.WriteAllText(Path.Combine(root,"project.godot"),"config_version=5\n[application]\nconfig/name=\"LibreKO content verification\"\nrun/main_scene=\"res://probe.tscn\"\n[display]\nwindow/size/viewport_width=480\nwindow/size/viewport_height=360\n[rendering]\nrenderer/rendering_method=\"forward_plus\"\nrendering_device/driver.windows=\"d3d12\"\n"+globals,new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(root,"probe.tscn"),"[gd_scene load_steps=2 format=3]\n[ext_resource type=\"Script\" path=\"res://probe.gd\" id=\"1\"]\n[node name=\"ContentProbe\" type=\"Node3D\"]\nscript=ExtResource(\"1\")\n",new UTF8Encoding(false));File.WriteAllText(Path.Combine(root,"probe.gd"),Script,new UTF8Encoding(false));
            var config=Path.Combine(root,"probe-config.json");var result=Path.Combine(root,"probe-result.json");File.WriteAllBytes(config,JsonSerializer.SerializeToUtf8Bytes(new{buildId=plan.Id,pack=Path.Combine(plan.OutputRoot,"knightonline.pck"),resources,result,screenshot=Path.Combine(root,"frame.png")},JsonConfig.Options));
            var start=new ProcessStartInfo(tools.GodotEditor??throw new InvalidDataException("Configure the Godot Mono editor for verification.")){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=root,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"--path",root,"--","--probe-config",config})start.ArgumentList.Add(arg);
            SourcePaths.AssertNoLinks(start.FileName);using var process=new Process{StartInfo=start};process.Start();children[process.Id]=process;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(2));using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,timeout.Token);
            try{await process.WaitForExitAsync(linked.Token);}catch{if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync();throw;}finally{children.TryRemove(process.Id,out _);}
            string log=await stdout+await stderr;File.WriteAllText(Path.Combine(root,"probe.log"),log,new UTF8Encoding(false));if(process.ExitCode!=0||Regex.IsMatch(log,@"(?m)^(ERROR:|SHADER ERROR)"))throw new InvalidDataException("Independent content process failed. See "+Path.Combine(root,"probe.log"));
            if(!File.Exists(result)||JsonNode.Parse(File.ReadAllBytes(result))?["verified"]?.GetValue<bool>()!=true||!File.Exists(Path.Combine(root,"frame.png")))throw new InvalidDataException("Independent content process did not produce verified resources and a rendered frame.");BuildPaths.Guard(plan);
        }catch(OperationCanceledException){throw;}catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.ComponentModel.Win32Exception or JsonException){issues.Add(new("Content probe",e.Message));}return issues;
    }
    const string Script="""
    extends Node3D
    var config: Dictionary
    var models := 0
    var shown := false
    func fail(message: String):
        push_error(message)
        get_tree().quit(1)
    func _ready():
        var args = OS.get_cmdline_user_args()
        if args.size() != 2 or args[0] != "--probe-config":
            fail("Missing probe configuration")
            return
        config = JSON.parse_string(FileAccess.get_file_as_string(args[1]))
        if not ProjectSettings.load_resource_pack(config.pack, true):
            fail("Cannot mount new client output")
            return
        var environment = WorldEnvironment.new()
        environment.environment = Environment.new()
        environment.environment.background_mode = Environment.BG_COLOR
        environment.environment.background_color = Color(0.06,0.07,0.09)
        environment.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
        environment.environment.ambient_light_color = Color.WHITE
        environment.environment.ambient_light_energy = 0.8
        add_child(environment)
        var light = DirectionalLight3D.new()
        light.rotation_degrees = Vector3(-35,-35,0)
        add_child(light)
        var camera = Camera3D.new()
        camera.position = Vector3(0,2.5,7)
        add_child(camera)
        camera.look_at(Vector3(0,1,0))
        for entry in config.resources:
            if entry.kind == "raw":
                if FileAccess.get_sha256(entry.path).to_upper() != entry.hash:
                    fail("Raw output hash mismatch: " + entry.path)
                    return
            elif entry.kind == "texture":
                var texture = ResourceLoader.load(entry.path, "Texture2D", ResourceLoader.CACHE_MODE_IGNORE)
                if not texture is Texture2D or texture.get_width() < 1 or texture.get_height() < 1:
                    fail("Imported texture is missing: " + entry.path)
                    return
            elif entry.kind == "height":
                if FileAccess.get_sha256(entry.path).to_upper() != entry.hash:
                    fail("Height PNG bytes differ: " + entry.path)
                    return
                var image = Image.new()
                if image.load_png_from_buffer(FileAccess.get_file_as_bytes(entry.path)) != OK or image.get_width() != entry.size or image.get_format() != Image.FORMAT_RGBA8:
                    fail("Height PNG format changed")
                    return
                var pixels = image.get_data()
                for sample in entry.samples:
                    if pixels.decode_s32(int(sample.index)*4) != int(sample.bits):
                        fail("Height float bits changed")
                        return
            elif entry.kind == "model":
                var packed = ResourceLoader.load(entry.path, "PackedScene", ResourceLoader.CACHE_MODE_IGNORE)
                if not packed is PackedScene:
                    fail("Packed model is missing: " + entry.path)
                    return
                var instance = packed.instantiate()
                add_child(instance)
                var counts = inspect(instance)
                if int(counts.vertices) != int(entry.vertices) or int(counts.bones) != int(entry.bones):
                    fail("Model geometry/skeleton differs: " + entry.path + " " + str(counts))
                    return
                for clip in entry.clips:
                    if not counts.clips.has(clip):
                        fail("Model clip missing: " + str(clip))
                        return
                models += 1
                if not shown and counts.vertices > 0:
                    shown = true
                else:
                    instance.queue_free()
        if not shown:
            var marker = MeshInstance3D.new()
            marker.mesh = BoxMesh.new()
            add_child(marker)
        for i in range(4):
            await get_tree().process_frame
        await RenderingServer.frame_post_draw
        var frame = get_viewport().get_texture().get_image()
        if frame.is_empty() or frame.save_png(config.screenshot) != OK:
            fail("No independent rendered frame")
            return
        var result = FileAccess.open(config.result, FileAccess.WRITE)
        result.store_string(JSON.stringify({"verified":true,"models":models,"buildId":config.buildId}))
        result.close()
        print("CONTENT PROBE PASS " + str(config.buildId))
        get_tree().quit()
    func inspect(node: Node) -> Dictionary:
        var counts = {"vertices":0,"bones":0,"clips":[]}
        if node is MeshInstance3D and node.mesh:
            for i in range(node.mesh.get_surface_count()):
                counts.vertices += node.mesh.surface_get_arrays(i)[Mesh.ARRAY_VERTEX].size()
        if node is Skeleton3D:
            counts.bones += node.get_bone_count()
        if node is AnimationPlayer:
            for clip in node.get_animation_list():
                counts.clips.append(str(clip))
        for child in node.get_children():
            var next = inspect(child)
            counts.vertices += next.vertices
            counts.bones += next.bones
            counts.clips.append_array(next.clips)
        return counts
    """;
}
