using System.Text;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public static class BuildChecks
{
    public static async Task VerifyOutput()
    {
        var path=System.Environment.GetEnvironmentVariable("LIBREKO_CLIENT_BUILD_MANIFEST")??throw new InvalidOperationException("Set LIBREKO_CLIENT_BUILD_MANIFEST to the exported build manifest.");var manifest=BuildManifest.Read(path);var editor=System.Environment.GetEnvironmentVariable("LIBREKO_AUTHORING_GODOT")??throw new InvalidOperationException("Configure the trusted Mono editor for verification.");var tools=(manifest.Plan.Profile?.Tools??new AuthoringTools(null,null,null)) with{GodotEditor=editor};var issues=await ContentProbeProject.VerifyAsync(new(manifest.Plan.Id,false,path,[]),tools,default);if(issues.Count>0)throw new Exception(string.Join("; ",issues.Select(i=>i.Message)));GD.Print("CLIENT OUTPUT VERIFIED: "+path);
    }
    public static async Task Run(EditorShell shell)
    {
        var marker=SourceChecks.Marker(shell);var session=shell.Session;var sources=session.Sources;var original=sources.Profile!;string fixture=marker["fixtureRoot"]!.GetValue<string>();string id=Guid.NewGuid().ToString("N");
        var shortFixture=LibreKO.Editors.Core.WorkspacePaths.ResolveOwnedPath(session.Root,".superpowers/client-fixtures/"+id[..8]);if(Directory.Exists(shortFixture))throw new InvalidOperationException("Build fixture identity already exists.");SourcePaths.AssertNoLinks(shortFixture);Directory.CreateDirectory(shortFixture);File.WriteAllText(Path.Combine(shortFixture,"fixture-marker.json"),System.Text.Json.JsonSerializer.Serialize(new{fixtureId=id,sourceMarker=fixture},JsonConfig.Options));
        var client=Path.Combine(shortFixture,"c");var output=Path.Combine(shortFixture,"o");Directory.CreateDirectory(client);Directory.CreateDirectory(output);
        var roots=original.Roots.ToDictionary(x=>x.Key,x=>x.Value);roots[SourceRoot.ClientProject]=client;roots[SourceRoot.BuildOutput]=output;
        string engine=System.Environment.GetEnvironmentVariable("LIBREKO_AUTHORING_GODOT")??(OS.HasFeature("editor")?OS.GetExecutablePath():throw new InvalidOperationException("Set LIBREKO_AUTHORING_GODOT to the Mono editor for packaged build checks."));
        var tools=new AuthoringTools(engine,Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),"dotnet","dotnet.exe"),null,Path.Combine(session.Profile.AssetRoot,"knightonline.exe"));sources.Select(original with{Id=id,Roots=roots,Tools=tools});var store=sources.Store!;
        void Write(string relative,byte[] bytes){var path=SourcePaths.Resolve(sources.Profile!,store.Key(SourceRoot.ClientProject,relative),true);Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllBytes(path,bytes);}
        Write("project.godot",Encoding.UTF8.GetBytes("config_version=5\n[application]\nconfig/name=\"Client build fixture\"\nrun/main_scene=\"res://main.tscn\"\nconfig/features=PackedStringArray(\"4.7\", \"C#\")\n[dotnet]\nproject/assembly_name=\"ClientFixture\"\n"));
        Write("ClientFixture.csproj","<Project Sdk=\"Godot.NET.Sdk/4.7.2\"><PropertyGroup><TargetFramework>net9.0</TargetFramework><EnableDynamicLoading>true</EnableDynamicLoading></PropertyGroup></Project>"u8.ToArray());Write("FixtureTag.cs","public static class FixtureTag { public const int Value=42; }"u8.ToArray());Write("main.tscn","[gd_scene format=3]\n[node name=\"ClientFixture\" type=\"Node3D\"]\n"u8.ToArray());
        var heightKey=store.Key(SourceRoot.ClientProject,"assets/terrain/build-fixture/heightmap.png");var height=new HeightField(3,4,[0,1,2,3,4,5,6,7,8]);Write(heightKey.RelativePath,ClientHeightmapCodec.Encode(height));Write("assets/npcs/index.json","{\"100\":\"build_model\"}"u8.ToArray());Write("assets/npcs/build_model.glb",WorldChecks.NpcVisualFixture());Write("assets/folder with spaces/data.json","{\"custom\":9007199254740993}"u8.ToArray());
        var missing=ClientMapCatalog.Default("placements.json");missing["name"]="missing_scenery";Write("assets/terrain/build-fixture/placements.json",Encoding.UTF8.GetBytes(new System.Text.Json.Nodes.JsonArray(missing).ToJsonString()));
        try{await BuildProjectFactory.PrepareAsync(sources,false,default);throw new Exception("Client build preflight verified a missing scenery dependency.");}catch(InvalidDataException){}Write("assets/terrain/build-fixture/placements.json","[]"u8.ToArray());
        var before=store.Read(heightKey);var normal=await BuildProjectFactory.PrepareAsync(sources,false,default);if(normal.PreviewDraft!=null)throw new Exception("Normal client snapshot adopted source drafts.");
        var draft=sources.Drafts!.Open(heightKey);draft.EditBytes(ClientHeightmapCodec.Encode(TerrainBrush.Apply(height,new("raise",1,1,0,5,0))));try{await BuildProjectFactory.PrepareAsync(sources,false,default);throw new Exception("Normal build bypassed the source save guard.");}catch(InvalidOperationException){}
        try
        {
            var preview=await BuildProjectFactory.PrepareAsync(sources,true,default);if(preview.PreviewDraft==null||ClientHeightmapCodec.Decode(File.ReadAllBytes(Path.Combine(preview.SnapshotRoot,heightKey.RelativePath))).Heights[4]!=9)throw new Exception("Preview did not freeze the explicit terrain candidate.");
            var runner=new BuildRunner(verify:ContentProbeProject.VerifyPlanAsync);var result=await runner.RunAsync(preview,tools,new BuildProgress(),default);if(!result.Succeeded)throw new Exception("Client build failed: "+string.Join("; ",result.Issues.Select(x=>x.Message)));
            if(store.Read(heightKey).Sha256!=before.Sha256||!draft.IsDirty||runner.OwnedProcessIds.Count!=0||ContentProbeProject.OwnedProcessIds.Count!=0)throw new Exception("Client build changed sources/drafts or leaked a child.");
            var proof=await ContentProbeProject.VerifyAsync(result,tools,default);if(proof.Count>0)throw new Exception(string.Join("; ",proof.Select(x=>x.Message)));GD.Print("CLIENT BUILD MANIFEST: "+result.ManifestPath);
            File.WriteAllText(Path.Combine(fixture,"last-client-build-manifest.txt"),result.ManifestPath,Encoding.UTF8);
        }
        finally{sources.Drafts!.Discard();}
    }
    sealed class BuildProgress:IProgress<string>{public void Report(string message){if(!string.IsNullOrWhiteSpace(message))GD.Print("BUILD "+message);}}
}
