using System.Text;
using System.Text.Json;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public static class BuildProjectFactory
{
    public static async Task<ClientBuildPlan> PrepareAsync(SourceSession sources,bool preview,CancellationToken ct)
    {
        var profile=sources.Profile??throw new InvalidOperationException("Select authoring sources.");var store=sources.Store!;int generation=sources.Generation,revision=sources.Revision;var tools=profile.Tools??throw new InvalidOperationException("Configure Godot Mono editor, .NET SDK and Windows export template in the source profile.");
        if(string.IsNullOrWhiteSpace(tools.ExportTemplate)||!File.Exists(tools.ExportTemplate))throw new InvalidOperationException("Select an existing Windows Mono export template in source tools.");
        if(!preview&&sources.HasUnsavedChanges)throw new InvalidOperationException("Save or discard all source drafts before building a normal client. Draft previews use a separate captured snapshot.");
        var drafts=preview?sources.CaptureDrafts():null;var root=profile.Roots.GetValueOrDefault(SourceRoot.BuildOutput)??throw new InvalidOperationException("Select a separate Build output root.");var staging=Path.Combine(root,"client-"+Guid.NewGuid().ToString("N"));
        if(OperatingSystem.IsWindows()&&staging.Length>145)throw new InvalidOperationException("Select a shorter Build output root. Godot Mono's Windows build tools reserve path space for generated runtime/intermediate files.");
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,sources.Cancellation);var plan=await BuildPlan.CaptureAsync(store,sources.Owner.Profile.WorkspaceRoot,Path.Combine(staging,"snapshot"),Path.Combine(staging,"output"),drafts,linked.Token);
        if(generation!=sources.Generation||revision!=sources.Revision)throw new OperationCanceledException("Authoring sources changed during snapshot capture.");
        var issues=await Task.Run(()=>Preflight(plan),linked.Token);if(issues.Count>0)throw new InvalidDataException("Incomplete/unsupported client sources:\n"+string.Join("\n",issues.Take(40).Select(i=>i.Field+": "+i.Message)));
        foreach(var input in plan.SnapshotFiles.Where(f=>f.RelativePath.EndsWith(".png",StringComparison.Ordinal)).ToArray())
        {
            var key=new SourceFileKey(plan.SourceProfileId,SourceRoot.ClientProject,"snapshot",input.RelativePath);if(!ClientTerrainCatalog.Accepts(key))continue;string sidecar=input.RelativePath+".import";
            if(!plan.SnapshotFiles.Any(f=>f.RelativePath==sidecar))plan=BuildPlan.Generate(plan,sidecar,Encoding.UTF8.GetBytes("[remap]\n\nimporter=\"keep\"\n"));
            else if(!System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(Path.Combine(plan.SnapshotRoot,sidecar)),"importer\\s*=\\s*\"keep\""))throw new InvalidDataException("Terrain raster requires a keep importer to retain exact bytes: "+sidecar);
        }
        var rootProjects=plan.SnapshotFiles.Where(f=>!f.RelativePath.Contains('/')&&f.RelativePath.EndsWith(".csproj",StringComparison.Ordinal)).ToArray();
        if(rootProjects.Length==1)
        {
            var projectName=Path.GetFileNameWithoutExtension(rootProjects[0].RelativePath);var assembly=System.Text.RegularExpressions.Regex.Match(File.ReadAllText(Path.Combine(plan.SnapshotRoot,"project.godot")),"project/assembly_name\\s*=\\s*\"([^\"]+)\"");var solutionName=(assembly.Success?assembly.Groups[1].Value:projectName)+".sln";SourcePaths.ValidateRelative(solutionName);
            if(!plan.SnapshotFiles.Any(f=>f.RelativePath==solutionName))
            {
                const string projectId="{E8411666-FE13-47AB-9E00-9F4EB014C321}";var solution=new StringBuilder("Microsoft Visual Studio Solution File, Format Version 12.00\n# Visual Studio Version17\n");solution.AppendLine($"Project(\"{{9A19103F-16F7-4668-BE54-9A1E7A4F7556}}\") = \"{projectName}\", \"{rootProjects[0].RelativePath}\", \"{projectId}\"\nEndProject\nGlobal\n\tGlobalSection(SolutionConfigurationPlatforms) = preSolution");foreach(var config in new[]{"Debug","ExportDebug","ExportRelease"})solution.AppendLine($"\t\t{config}|Any CPU = {config}|Any CPU");solution.AppendLine("\tEndGlobalSection\n\tGlobalSection(ProjectConfigurationPlatforms) = postSolution");foreach(var config in new[]{"Debug","ExportDebug","ExportRelease"})solution.AppendLine($"\t\t{projectId}.{config}|Any CPU.ActiveCfg = {config}|Any CPU\n\t\t{projectId}.{config}|Any CPU.Build.0 = {config}|Any CPU");solution.AppendLine("\tEndGlobalSection\nEndGlobal");plan=BuildPlan.Generate(plan,solutionName,Encoding.UTF8.GetBytes(solution.ToString()));
            }
        }
        string Text(string value)=>JsonSerializer.Serialize(value,JsonConfig.Options);
        var preset=$"""
        [preset.0]
        name="Editor Client"
        platform="Windows Desktop"
        runnable=true
        export_filter="all_resources"
        include_filter="*.json,*.bin,*.comp,*.png,*.gdshaderinc"
        exclude_filter="editor-build-plan.json"
        export_path=""
        script_export_mode=2
        [preset.0.options]
        custom_template/debug={Text(tools.ExportTemplate)}
        custom_template/release={Text(tools.ExportTemplate)}
        debug/export_console_wrapper=0
        binary_format/embed_pck=false
        texture_format/s3tc_bptc=true
        texture_format/etc2_astc=false
        codesign/enable=false
        application/modify_resources=false
        dotnet/include_scripts_content=false
        dotnet/include_debug_symbols=false
        """;
        plan=BuildPlan.Generate(plan,"export_presets.cfg",Encoding.UTF8.GetBytes(preset));
        // Explicit raw provenance in the exported pack links imported model resources to captured sources.
        plan=BuildPlan.Generate(plan,"editor-build-inputs.json",JsonSerializer.SerializeToUtf8Bytes(new{plan.Id,plan.SourceProfileId,Preview=preview,Files=plan.SnapshotFiles.Where(f=>!f.Generated).ToArray()},JsonConfig.Options));return plan;
    }
    public static IReadOnlyList<ValidationIssue> Preflight(ClientBuildPlan plan)
    {
        var issues=new List<ValidationIssue>();var files=plan.SnapshotFiles.ToDictionary(f=>f.RelativePath,StringComparer.OrdinalIgnoreCase);
        foreach(var file in plan.SnapshotFiles)
        {
            string path=Path.Combine(plan.SnapshotRoot,file.RelativePath);try
            {
                if(file.RelativePath.EndsWith(".import",StringComparison.Ordinal))
                {
                    foreach(var line in File.ReadLines(path))if(line.TrimStart().StartsWith("source_file=",StringComparison.Ordinal)){var value=line[(line.IndexOf('=')+1)..].Trim().Trim('"');if(value.StartsWith("res://",StringComparison.Ordinal)&&!files.ContainsKey(value[6..]))issues.Add(new(file.RelativePath,"Raw import source is missing: "+value));}
                }
                if(file.RelativePath.EndsWith(".csproj",StringComparison.Ordinal))
                {
                    var project=System.Xml.Linq.XDocument.Load(path);foreach(var reference in project.Descendants().Where(e=>e.Name.LocalName is "ProjectReference" or "Compile" or "Reference"))
                    {var value=(string?)reference.Attribute("Include");if(string.IsNullOrEmpty(value)||value.Contains('*')||!value.Contains('/')&&!value.Contains('\\'))continue;var absolute=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!,value));if(!absolute.StartsWith(Path.GetFullPath(plan.SnapshotRoot)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))issues.Add(new(file.RelativePath,"External build reference needs a self-contained source project: "+value));}
                }
                var key=new SourceFileKey(plan.SourceProfileId,SourceRoot.ClientProject,"snapshot",file.RelativePath);
                if(ClientTerrainCatalog.Accepts(key)&&file.RelativePath.EndsWith("heightmap.png",StringComparison.Ordinal))ClientHeightmapCodec.Decode(File.ReadAllBytes(path));
                else if(ClientMapCatalog.Accepts(key)){issues.AddRange(ClientMapCatalog.Validate(key,File.ReadAllBytes(path)));if(file.RelativePath.EndsWith(".json",StringComparison.Ordinal))issues.AddRange(ClientResourceGraph.Validate(file.RelativePath,File.ReadAllBytes(path),dependency=>files.ContainsKey(dependency)?File.ReadAllBytes(Path.Combine(plan.SnapshotRoot,dependency)):throw new InvalidDataException("Missing captured resource: "+dependency)));}
                else if(NpcVisualCatalog.Accepts(key))issues.AddRange(NpcVisualCatalog.Validate(key,File.ReadAllBytes(path)));
                else if(AssetDependencyAudit.IsModel(key))
                {
                    var model=GltfAssetDocument.Read(File.ReadAllBytes(path),file.RelativePath.EndsWith(".glb",StringComparison.Ordinal));foreach(var uri in model.ExternalUris){var relative=(Path.GetDirectoryName(file.RelativePath)!.Replace('\\','/')+"/"+uri);if(!files.ContainsKey(relative))issues.Add(new(file.RelativePath,"Missing model dependency: "+relative));}
                }
                if(file.RelativePath=="assets/npcs/index.json")
                {var index=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();foreach(var pair in index.Where(p=>int.TryParse(p.Key,out _)&&!string.IsNullOrEmpty(p.Value?.ToString()))){string relative="assets/npcs/"+pair.Value+".glb";if(!files.ContainsKey(relative))issues.Add(new(file.RelativePath,"Missing mapped model: "+relative));}}
            }catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException or System.Text.Json.JsonException){issues.Add(new(file.RelativePath,e.Message));}
            if(issues.Count>=100)break;
        }return issues;
    }
}
