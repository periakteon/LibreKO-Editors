using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public sealed class SourcePreviewHost(SourceSession sources):IDisposable
{
    readonly ConcurrentDictionary<int,Process> children=new();CancellationTokenSource request=new();BuildRunner? runner;int serial;bool disposed;
    public string Status{get;private set;}="No captured source preview";public string? DisplayedSourceHash{get;private set;}public string? LastResultPath{get;private set;}
    public IReadOnlyList<int> OwnedProcessIds=>children.Keys.Concat(runner?.OwnedProcessIds??[]).Concat(ContentProbeProject.OwnedProcessIds).Distinct().ToArray();
    public event Action? Changed;
    public Task Completion{get;private set;}=Task.CompletedTask;
    public void MarkChanged(){Invalidate();Status="Source changed · captured preview closed; refresh to inspect the new bytes";Changed?.Invoke();}
    public void Invalidate(){serial++;request.Cancel();foreach(var child in children.Values)try{if(!child.HasExited){child.Kill(true);child.WaitForExit(2000);}}catch(InvalidOperationException){}runner?.StopOwned();ContentProbeProject.StopOwned();DisplayedSourceHash=null;Status="Captured source preview closed";Changed?.Invoke();}
    public async Task OpenAsync(SourcePreviewManifest manifest,CancellationToken ct)
    {
        if(disposed)throw new ObjectDisposedException(nameof(SourcePreviewHost));Invalidate();request.Dispose();request=CancellationTokenSource.CreateLinkedTokenSource(ct,sources.Cancellation);int current=serial;var token=request.Token;
        void Guard(){token.ThrowIfCancellationRequested();if(current!=serial)throw new OperationCanceledException("A newer source preview replaced this request.");manifest.Guard(sources.Profile?.Id??"",sources.Generation,sources.Revision,sources.Drafts?.Version??-1);}
        Guard();Status="Building captured source preview…";Changed?.Invoke();var tools=sources.Profile!.Tools??throw new InvalidOperationException("Configure source build tools.");runner=new(verify:ContentProbeProject.VerifyPlanAsync);var result=await runner.RunAsync(manifest.Build!,tools,null,token);Guard();if(!result.Succeeded)throw new InvalidDataException(string.Join("; ",result.Issues.Select(i=>i.Message)));
        var saved=BuildManifest.Read(result.ManifestPath);manifest=manifest with{Build=saved.Plan};var path=Path.Combine(Path.GetDirectoryName(manifest.ProjectRoot)!,"source-preview.json");File.WriteAllBytes(path,JsonSerializer.SerializeToUtf8Bytes(manifest,JsonConfig.Options));LastResultPath=path+".result.json";
        var start=new ProcessStartInfo(OS.GetExecutablePath()){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(path)!};if(OS.HasFeature("editor")){start.ArgumentList.Add("--path");start.ArgumentList.Add(ProjectSettings.GlobalizePath("res://"));}foreach(var arg in new[]{"--","--source-preview",path})start.ArgumentList.Add(arg);
        var child=new Process{StartInfo=start};child.Start();children[child.Id]=child;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();try
        {
            var deadline=DateTime.UtcNow.AddSeconds(40);while(!File.Exists(LastResultPath)&&!child.HasExited){Guard();if(DateTime.UtcNow>deadline)throw new TimeoutException("Fresh source preview did not report a rendered result.");await Task.Delay(50,token);}Guard();
            if(!File.Exists(LastResultPath))throw new InvalidDataException("Fresh source preview exited before reporting resources.");var proof=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(LastResultPath))!;if(proof["displayedSourceHash"]?.GetValue<string>()!=manifest.ExpectedSourceHash)throw new InvalidDataException("Packed cache shown as authored preview.");if(manifest.Kind=="model"&&proof["modelResourcePath"]?.GetValue<string>()!=manifest.ModelResourcePath)throw new InvalidDataException("Preview loaded a different source model path.");DisplayedSourceHash=manifest.ExpectedSourceHash;Status="Fresh captured source · "+manifest.Target!.RelativePath+" · "+DisplayedSourceHash[..16];Changed?.Invoke();
            Completion=Monitor();return;
        }
        catch{if(!child.HasExited)child.Kill(true);await child.WaitForExitAsync();children.TryRemove(child.Id,out _);await Log();child.Dispose();throw;}
        async Task Log(){try{BuildPaths.Guard(manifest.Build!);File.WriteAllText(path+".log",await stdout+await stderr);}catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException){} }
        async Task Monitor()
        {
            try{await child.WaitForExitAsync(token);if(child.ExitCode!=0&&current==serial)Status="Source preview failed; inspect "+path+".log";}
            catch(OperationCanceledException){}finally{if(!child.HasExited)child.Kill(true);await child.WaitForExitAsync();children.TryRemove(child.Id,out _);await Log();child.Dispose();if(current==serial){Status="Captured source preview closed";Changed?.Invoke();}}
        }
    }
    public void Dispose(){if(disposed)return;disposed=true;Invalidate();request.Dispose();}
}
