using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Sources;
public record BuildCommand(string Stage,string FileName,IReadOnlyList<string> Arguments,string WorkingDirectory,IReadOnlyDictionary<string,string> Environment);
public record BuildProcessResult(int ExitCode,string Output,bool HadErrors,bool IdleExportClosed=false);
public sealed class BuildRunner
{
    readonly Func<BuildCommand,CancellationToken,Task<BuildProcessResult>>? process;
    readonly Func<ClientBuildPlan,AuthoringTools,CancellationToken,Task<IReadOnlyList<ValidationIssue>>>? verify;
    readonly ConcurrentDictionary<int,Process> children=new();
    public IReadOnlyList<int> OwnedProcessIds=>children.Keys.ToArray();
    public BuildRunner(Func<BuildCommand,CancellationToken,Task<BuildProcessResult>>? process=null,Func<ClientBuildPlan,AuthoringTools,CancellationToken,Task<IReadOnlyList<ValidationIssue>>>? verify=null){this.process=process;this.verify=verify;}
    internal Task<BuildProcessResult> RunOwnedAsync(BuildCommand command,IProgress<string>? progress,CancellationToken ct)=>Execute(command,progress,ct);
    public void StopOwned(){foreach(var child in children.Values)try{if(!child.HasExited){child.Kill(true);child.WaitForExit(2000);}}catch(InvalidOperationException){} }
    public async Task<ClientBuildResult> RunAsync(ClientBuildPlan plan,AuthoringTools tools,IProgress<string>? progress,CancellationToken ct)
    {
        var issues=new List<ValidationIssue>();var versions=new Dictionary<string,string>();var outputs=new List<BuildSnapshotFile>();string manifest="";bool outputOwned=false;string status="failed";
        try
        {
            BuildPaths.Guard(plan);issues.AddRange(BuildPlan.VerifySnapshot(plan));if(issues.Count>0)return new(plan.Id,false,"",issues);
            if(plan.Profile?.Tools!=null&&plan.Profile.Tools!=tools)throw new InvalidDataException("Build tool settings changed after snapshot capture; prepare a new build.");
            if(Directory.Exists(plan.OutputRoot)||File.Exists(plan.OutputRoot))throw new InvalidDataException("Build output already exists; use a new build ID.");
            if(!Directory.Exists(plan.SnapshotRoot))throw new InvalidDataException("Captured snapshot directory is unavailable.");
            Directory.CreateDirectory(plan.OutputRoot);plan=plan with{OutputIdentity=SourceIdentity.Root(plan.OutputRoot)};outputOwned=true;manifest=Path.Combine(plan.OutputRoot,"build-manifest.json");Save("preparing",false);
            string engine=Tool(tools.GodotEditor,"Godot Mono editor"),sdk=Tool(tools.DotnetSdk,".NET SDK"),template=Tool(tools.ExportTemplate,"Windows Mono export template");
            versions["Godot"]=await Run("godot-version",engine,["--version"]);if(!versions["Godot"].Trim().StartsWith("4.7.2",StringComparison.Ordinal)||!versions["Godot"].Contains("mono",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Configure the verified Godot4.7.2 Mono editor.");
            versions["Dotnet"]=await Run("sdk-version",sdk,["--version"]);if(!Version.TryParse(versions["Dotnet"].Trim().Split('-')[0],out var sdkVersion)||sdkVersion.Major<9)throw new InvalidDataException("Configure a .NET SDK9 or newer, matching the client target framework.");
            versions["TemplateSHA256"]=Hash(template);
            var projectFiles=Directory.GetFiles(plan.SnapshotRoot,"*.csproj",SearchOption.TopDirectoryOnly);if(projectFiles.Length>1)throw new InvalidDataException("Select a client with one root C# project.");
            if(projectFiles.Length==1)await Run("compile",sdk,["build",projectFiles[0],"--configuration","ExportRelease","-m:1","-nodeReuse:false","-p:UseSharedCompilation=false","-p:NuGetAudit=false"]);
            await Run("import",engine,["--headless","--editor","--path",plan.SnapshotRoot,"--import"]);
            issues.AddRange(BuildPlan.VerifySnapshot(plan,derivedImports:true));if(issues.Count>0)throw new InvalidDataException("Client import changed captured raw source bytes.");
            await Run("export",engine,["--headless","--editor","--path",plan.SnapshotRoot,"--export-release","Editor Client",Path.Combine(plan.OutputRoot,"knightonline.exe"),"--quit"]);
            CheckHeader(Path.Combine(plan.OutputRoot,"knightonline.pck"),"GDPC"u8,16);CheckHeader(Path.Combine(plan.OutputRoot,"knightonline.exe"),"MZ"u8,2);
            if(projectFiles.Length==1)
            {
                string assembly=Path.GetFileNameWithoutExtension(projectFiles[0]);var match=Regex.Match(File.ReadAllText(Path.Combine(plan.SnapshotRoot,"project.godot")),"project/assembly_name\\s*=\\s*\"([^\"]+)\"");if(match.Success)assembly=match.Groups[1].Value;SourcePaths.ValidateRelative(assembly);
                foreach(var name in new[]{assembly+".dll",assembly+".deps.json",assembly+".runtimeconfig.json","GodotSharp.dll","coreclr.dll","hostfxr.dll","hostpolicy.dll"})
                {var candidates=Directory.EnumerateFiles(plan.OutputRoot,name,SearchOption.AllDirectories).ToArray();if(candidates.Length!=1)throw new InvalidDataException("Export omitted or duplicated managed client/runtime file: "+name);SourcePaths.AssertNoLinks(candidates[0]);if(name.EndsWith(".json",StringComparison.Ordinal)){using var json=System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(candidates[0]));}else CheckHeader(candidates[0],"MZ"u8,64);}
                var dll=Directory.EnumerateFiles(plan.OutputRoot,assembly+".dll",SearchOption.AllDirectories).Single();using var stream=File.OpenRead(dll);using var pe=new PEReader(stream);if(!pe.HasMetadata)throw new InvalidDataException("Exported managed client assembly has no metadata.");var metadata=pe.GetMetadataReader();if(metadata.GetString(metadata.GetAssemblyDefinition().Name)!=assembly)throw new InvalidDataException("Exported managed assembly identity differs from the client project.");
            }
            foreach(var file in Directory.EnumerateFiles(plan.OutputRoot,"*",SearchOption.AllDirectories).Where(p=>p!=manifest&&!p.EndsWith(".log",StringComparison.OrdinalIgnoreCase)))
            {SourcePaths.AssertNoLinks(file);outputs.Add(new(Path.GetRelativePath(plan.OutputRoot,file).Replace('\\','/'),Hash(file),new FileInfo(file).Length));}
            Save("exported",false);progress?.Report("Export completed; verifying in an independent content probe…");
            if(verify==null)throw new InvalidDataException("An independent content probe is required before declaring a client build successful.");
            issues.AddRange(await verify(plan,tools,ct));if(issues.Count>0)throw new InvalidDataException("Independent output verification failed.");
            foreach(var file in outputs){ct.ThrowIfCancellationRequested();var path=WorkspacePaths.ResolveOwnedPath(plan.OutputRoot,file.RelativePath);SourcePaths.AssertNoLinks(path);if(new FileInfo(path).Length!=file.Length||Hash(path)!=file.Sha256)throw new InvalidDataException("Output changed after probe: "+file.RelativePath);}
            issues.AddRange(BuildPlan.VerifySnapshot(plan,derivedImports:true));if(issues.Count>0)throw new InvalidDataException("Raw snapshot bytes changed during export/probe.");BuildPaths.Guard(plan);status="verified";Save(status,true);return new(plan.Id,true,manifest,[]);
            async Task<string> Run(string stage,string exe,IReadOnlyList<string> args)
            {
                ct.ThrowIfCancellationRequested();BuildPaths.Guard(plan);progress?.Report(stage+"…");var environment=new Dictionary<string,string>{["DOTNET_ROOT"]=Path.GetDirectoryName(sdk)!,["PATH"]=Path.GetDirectoryName(sdk)+Path.PathSeparator+System.Environment.GetEnvironmentVariable("PATH"),["DOTNET_NOLOGO"]="1",["DOTNET_CLI_TELEMETRY_OPTOUT"]="1",["UseSharedCompilation"]="false",["MSBUILDDISABLENODEREUSE"]="1"};
                var command=new BuildCommand(stage,exe,args,plan.SnapshotRoot,environment);var result=process==null?await Execute(command,progress,ct):await process(command,ct);BuildPaths.Guard(plan);File.WriteAllText(Path.Combine(plan.OutputRoot,stage+".log"),result.Output,new UTF8Encoding(false));
                if(result.IdleExportClosed)versions["ExportShutdown"]="Closed owned exporter after savepack and stable output; independent probe still required.";
                if(result.ExitCode!=0&&!result.IdleExportClosed||result.HadErrors)throw new InvalidDataException(stage+" failed (exit "+result.ExitCode+"). See "+stage+".log.");return result.Output.Trim();
            }
        }
        catch(OperationCanceledException){status="cancelled";issues.Add(new("Build","Build cancelled; only its owned child processes were stopped."));}
        catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or System.ComponentModel.Win32Exception or BadImageFormatException or System.Text.Json.JsonException){issues.Add(new("Build",e.Message));}
        if(outputOwned)try{Save(status,false);}catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException){issues.Add(new("Manifest",e.Message));manifest="";}
        return new(plan.Id,false,manifest,issues);
        void Save(string state,bool verified)=>new BuildManifest(plan,state,verified,versions,outputs,issues.ToArray(),DateTimeOffset.UtcNow).Save(manifest);
    }
    static string Tool(string? path,string label){if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)||!File.Exists(path))throw new InvalidDataException("Configure an existing absolute "+label+" path in the source profile. Ordinary source/database editing remains available.");SourcePaths.AssertNoLinks(path);return Path.GetFullPath(path);}
    static void CheckHeader(string path,ReadOnlySpan<byte> magic,int minimum){SourcePaths.AssertNoLinks(path);using var input=File.OpenRead(path);if(input.Length<minimum)throw new InvalidDataException("Export output is missing/partial: "+Path.GetFileName(path));Span<byte> header=stackalloc byte[magic.Length];input.ReadExactly(header);if(!header.SequenceEqual(magic))throw new InvalidDataException("Export output has an invalid header: "+Path.GetFileName(path));}
    public static string Hash(string path){SourcePaths.AssertNoLinks(path);using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file));}
    async Task<BuildProcessResult> Execute(BuildCommand command,IProgress<string>? progress,CancellationToken ct)
    {
        var start=new ProcessStartInfo(command.FileName){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=command.WorkingDirectory,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in command.Arguments)start.ArgumentList.Add(arg);foreach(var pair in command.Environment)start.Environment[pair.Key]=pair.Value;
        using var child=new Process{StartInfo=start};if(!child.Start())throw new IOException("Could not start configured build tool.");children[child.Id]=child;var output=new StringBuilder();object gate=new();bool error=false,complete=false,truncated=false,idleClosed=false;DateTime? completedAt=null;
        async Task Pump(StreamReader reader){while(await reader.ReadLineAsync() is string line){var plain=Regex.Replace(line,"\u001b\\[[0-9;]*m","");lock(gate){if(plain.StartsWith("ERROR:",StringComparison.Ordinal)||plain.Contains("SHADER ERROR",StringComparison.Ordinal))error=true;if(plain.Contains("[ DONE ] savepack",StringComparison.Ordinal)||plain.Contains("[DONE] savepack",StringComparison.Ordinal)){complete=true;completedAt??=DateTime.UtcNow;}if(output.Length<4*1024*1024)output.AppendLine(plain);else truncated=true;}progress?.Report(plain);}}
        var read=Task.WhenAll(Pump(child.StandardOutput),Pump(child.StandardError));using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(15));using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,timeout.Token);
        try
        {
            while(!child.HasExited)
            {
                linked.Token.ThrowIfCancellationRequested();if(command.Stage=="export"&&complete&&completedAt is {} at&&DateTime.UtcNow-at>TimeSpan.FromSeconds(10))
                {
                    string? outputFile=command.Arguments.FirstOrDefault(a=>a.EndsWith("knightonline.exe",StringComparison.Ordinal));var pack=outputFile==null?null:Path.ChangeExtension(outputFile,".pck");if(pack!=null&&File.Exists(pack)&&new FileInfo(pack).Length>16&&DateTime.UtcNow-File.GetLastWriteTimeUtc(pack)>TimeSpan.FromSeconds(10)){child.Kill(true);idleClosed=true;break;}
                }
                await Task.Delay(200,linked.Token);
            }
            await child.WaitForExitAsync();await read;if(truncated)output.AppendLine("Build log exceeded4MiB and was truncated; all lines were checked for engine/shader errors.");return new(child.ExitCode,output.ToString(),error,idleClosed);
        }
        catch{if(!child.HasExited)child.Kill(true);await child.WaitForExitAsync();await read;throw;}
        finally{children.TryRemove(child.Id,out _);}
    }
}
