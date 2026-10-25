using System.Text;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
public class BuildPlanTests
{
    [Fact]
    public async Task BuildNeverOverwritesSourcesAndCannotSucceedOnPartialExport()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ClientProject,"project.godot","config_version=5\n[application]\nconfig/name=\"Fixture\""u8.ToArray());var key=f.Key(SourceRoot.ClientProject,"assets/data.json");f.Write(key.Root,key.RelativePath,"{\"value\":1}"u8.ToArray());f.Write(SourceRoot.ClientProject,".godot/ignored.bin",[1]);f.Write(SourceRoot.ClientProject,"bin/ignored.bin",[1]);
        var before=store.Read(key);Assert.NotEmpty(BuildPaths.Validate(f.Profile,f.Profile.Roots[key.Root],f.Profile.Roots[key.Root]));
        var root=Path.Combine(f.Profile.Roots[SourceRoot.BuildOutput],Guid.NewGuid().ToString("N"));var plan=await BuildPlan.CaptureAsync(store,f.WorkspaceRoot,Path.Combine(root,"snapshot"),Path.Combine(root,"output"));Assert.Null(plan.PreviewDraft);Assert.DoesNotContain(plan.Inputs,i=>i.Key.RelativePath.Contains("ignored"));Assert.Equal(before.Sha256,store.Read(key).Sha256);
        var tools=FakeTools(f);var runner=new BuildRunner(process:async(command,ct)=>{await Task.CompletedTask;if(command.Stage=="export")File.WriteAllBytes(Path.Combine(plan.OutputRoot,"knightonline.pck"),[]);return new(0,command.Stage=="sdk-version"?"10.0.103":"4.7.2.stable.mono",false);},verify:(_,_,_)=>Task.FromResult<IReadOnlyList<ValidationIssue>>([]));
        var failedResult=await runner.RunAsync(plan,tools,new Progress<string>(),default);Assert.False(failedResult.Succeeded);Assert.Equal(before.Sha256,store.Read(key).Sha256);Assert.True(File.Exists(failedResult.ManifestPath));
    }
    [Fact]
    public async Task PreviewCapturesOnlyExplicitCandidatesAndNormalBuildsCannotAdoptThem()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ClientProject,"project.godot","config_version=5"u8.ToArray());var key=f.Key(SourceRoot.ClientProject,"assets/example.json");f.Write(key.Root,key.RelativePath,"old"u8.ToArray());var change=new FileChange(Guid.NewGuid(),[store.Stage(key,"draft"u8.ToArray())]);string Root()=>Path.Combine(f.Profile.Roots[SourceRoot.BuildOutput],Guid.NewGuid().ToString("N"));var root=Root();var preview=await BuildPlan.CaptureAsync(store,f.WorkspaceRoot,Path.Combine(root,"snapshot"),Path.Combine(root,"output"),change);Assert.NotNull(preview.PreviewDraft);Assert.Equal("draft",File.ReadAllText(Path.Combine(preview.SnapshotRoot,key.RelativePath)));Assert.Equal("old",Encoding.UTF8.GetString(store.Blobs.Read(store.Read(key).Content!)));
        root=Root();var normal=await BuildPlan.CaptureAsync(store,f.WorkspaceRoot,Path.Combine(root,"snapshot"),Path.Combine(root,"output"));Assert.Equal("old",File.ReadAllText(Path.Combine(normal.SnapshotRoot,key.RelativePath)));Assert.Null(normal.PreviewDraft);
        f.Write(key.Root,key.RelativePath,"external"u8.ToArray());root=Root();await Assert.ThrowsAsync<InvalidDataException>(()=>BuildPlan.CaptureAsync(store,f.WorkspaceRoot,Path.Combine(root,"snapshot"),Path.Combine(root,"output"),change));
    }
    [Fact]
    public async Task ExitZeroCannotBypassProbeOrAdoptOldOutputs()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ClientProject,"project.godot","config_version=5"u8.ToArray());var root=Path.Combine(f.Profile.Roots[SourceRoot.BuildOutput],Guid.NewGuid().ToString("N"));var plan=await BuildPlan.CaptureAsync(store,f.WorkspaceRoot,Path.Combine(root,"snapshot"),Path.Combine(root,"output"));
        var runner=new BuildRunner(process:(command,ct)=>{if(command.Stage=="export"){File.WriteAllBytes(Path.Combine(plan.OutputRoot,"knightonline.pck"),"GDPCstill-not-verified"u8.ToArray());File.WriteAllBytes(Path.Combine(plan.OutputRoot,"knightonline.exe"),"MZfixture"u8.ToArray());}return Task.FromResult(new BuildProcessResult(0,command.Stage=="sdk-version"?"10.0.103":"4.7.2.stable.mono",false));},verify:(_,_,_)=>Task.FromResult<IReadOnlyList<ValidationIssue>>([new("Probe","Wrong candidate model hash")]));
        var result=await runner.RunAsync(plan,FakeTools(f),null,default);Assert.False(result.Succeeded);Assert.Contains(result.Issues,i=>i.Message.Contains("Wrong candidate"));Assert.False(BuildManifest.Read(result.ManifestPath).Verified);
        var second=await runner.RunAsync(plan,FakeTools(f),null,default);Assert.False(second.Succeeded);Assert.Contains(second.Issues,i=>i.Message.Contains("already"));
    }
    [Fact]
    public async Task MissingToolsAndChangedSnapshotsLeaveSourceEditingAvailable()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ClientProject,"project.godot","config_version=5"u8.ToArray());string Root()=>Path.Combine(f.Profile.Roots[SourceRoot.BuildOutput],Guid.NewGuid().ToString("N"));var root=Root();var plan=await BuildPlan.CaptureAsync(store,f.WorkspaceRoot,Path.Combine(root,"snapshot"),Path.Combine(root,"output"));var result=await new BuildRunner().RunAsync(plan,new(null,null,null),null,default);Assert.False(result.Succeeded);
        File.WriteAllText(Path.Combine(plan.SnapshotRoot,"project.godot"),"changed");result=await new BuildRunner().RunAsync(plan,FakeTools(f),null,default);Assert.False(result.Succeeded);Assert.Contains(result.Issues,i=>i.Message.Contains("snapshot",StringComparison.OrdinalIgnoreCase));Assert.Equal("config_version=5",File.ReadAllText(Path.Combine(f.Profile.Roots[SourceRoot.ClientProject],"project.godot")));
    }
    [Fact]
    public async Task SnapshotRejectsUncapturedCodeAndDirectoryReplacement()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ClientProject,"project.godot","config_version=5"u8.ToArray());var root=Path.Combine(f.Profile.Roots[SourceRoot.BuildOutput],Guid.NewGuid().ToString("N"));var plan=await BuildPlan.CaptureAsync(store,f.WorkspaceRoot,Path.Combine(root,"snapshot"),Path.Combine(root,"output"));File.WriteAllText(Path.Combine(plan.SnapshotRoot,"injected.cs"),"uncaptured code");Assert.NotEmpty(BuildPlan.VerifySnapshot(plan));
        Directory.Move(plan.SnapshotRoot,plan.SnapshotRoot+"-original");Directory.CreateDirectory(plan.SnapshotRoot);File.WriteAllText(Path.Combine(plan.SnapshotRoot,"project.godot"),"config_version=5");Assert.Throws<InvalidDataException>(()=>BuildPaths.Guard(plan));
    }
    [Fact]
    public async Task NativeArgumentsRemainSeparateAndCancellationStopsOnlyOwnedProcess()
    {
        if(!OperatingSystem.IsWindows())return;using var f=SourceFixture.Create();var exe=Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");var script=Path.Combine(f.Root,"echo argument.ps1");File.WriteAllText(script,"param([string]$value)\n[Console]::WriteLine($value)\n");var runner=new BuildRunner();const string value="one argument with spaces and (parentheses)";var echo=await runner.RunOwnedAsync(new("echo",exe,["-NoProfile","-File",script,value],f.Root,new Dictionary<string,string>()),null,default);Assert.Equal(value,echo.Output.Trim());Assert.Equal(0,echo.ExitCode);
        var otherStart=new System.Diagnostics.ProcessStartInfo(exe){UseShellExecute=false,CreateNoWindow=true};foreach(var a in new[]{"-NoProfile","-Command","Start-Sleep -Seconds 20"})otherStart.ArgumentList.Add(a);using var other=System.Diagnostics.Process.Start(otherStart)!;
        try{using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(10));var task=runner.RunOwnedAsync(new("cancel",exe,["-NoProfile","-Command","Start-Sleep -Seconds 20"],f.Root,new Dictionary<string,string>()),null,cancel.Token);var deadline=DateTime.UtcNow.AddSeconds(3);while(runner.OwnedProcessIds.Count==0&&DateTime.UtcNow<deadline)await Task.Delay(10);Assert.Single(runner.OwnedProcessIds);cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>task);Assert.Empty(runner.OwnedProcessIds);Assert.False(other.HasExited);}finally{if(!other.HasExited)other.Kill(true);await other.WaitForExitAsync();}
    }
    [Fact]
    public async Task LateSourceRootJunctionIsRejectedByPurePathsAndCapturedGuard()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ClientProject,"project.godot","config_version=5"u8.ToArray());var root=Path.Combine(f.Profile.Roots[SourceRoot.BuildOutput],Guid.NewGuid().ToString("N"));var plan=await BuildPlan.CaptureAsync(store,f.WorkspaceRoot,Path.Combine(root,"snapshot"),Path.Combine(root,"output"));var source=f.Profile.Roots[SourceRoot.ClientProject];var backup=source+"-original";Assert.StartsWith(Path.GetFullPath(f.Root)+Path.DirectorySeparatorChar,Path.GetFullPath(source));Assert.StartsWith(Path.GetFullPath(f.Root)+Path.DirectorySeparatorChar,Path.GetFullPath(backup));Directory.Move(source,backup);
        if(OperatingSystem.IsWindows()){var start=new System.Diagnostics.ProcessStartInfo("cmd.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};foreach(var arg in new[]{"/c","mklink","/J",source,backup})start.ArgumentList.Add(arg);using var process=System.Diagnostics.Process.Start(start)!;process.WaitForExit();Assert.Equal(0,process.ExitCode);}else Directory.CreateSymbolicLink(source,backup);
        Assert.NotEmpty(BuildPaths.Validate(f.Profile,plan.SnapshotRoot,plan.OutputRoot,f.WorkspaceRoot));Assert.Throws<InvalidDataException>(()=>BuildPaths.Guard(plan));
    }
    [Fact]
    public async Task ManagedExportCannotSucceedWithAnUnrelatedDllAndNoClientRuntime()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ClientProject,"project.godot","config_version=5"u8.ToArray());f.Write(SourceRoot.ClientProject,"Client.csproj","<Project Sdk=\"Godot.NET.Sdk/4.7.2\"/>"u8.ToArray());var root=Path.Combine(f.Profile.Roots[SourceRoot.BuildOutput],Guid.NewGuid().ToString("N"));var plan=await BuildPlan.CaptureAsync(store,f.WorkspaceRoot,Path.Combine(root,"snapshot"),Path.Combine(root,"output"));
        var runner=new BuildRunner(process:(command,ct)=>{if(command.Stage=="export"){File.WriteAllBytes(Path.Combine(plan.OutputRoot,"knightonline.pck"),"GDPCfake-resource-pack"u8.ToArray());File.WriteAllBytes(Path.Combine(plan.OutputRoot,"knightonline.exe"),"MZfixture"u8.ToArray());File.WriteAllBytes(Path.Combine(plan.OutputRoot,"unrelated.dll"),[1]);}return Task.FromResult(new BuildProcessResult(0,command.Stage=="sdk-version"?"10.0.103":"4.7.2.stable.mono",false));},verify:(_,_,_)=>Task.FromResult<IReadOnlyList<ValidationIssue>>([]));
        var result=await runner.RunAsync(plan,FakeTools(f),null,default);Assert.False(result.Succeeded);Assert.Contains(result.Issues,i=>i.Message.Contains("managed",StringComparison.OrdinalIgnoreCase));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task InvalidManagedMetadataAndRuntimeJsonReturnFailedResults(bool brokenJson)
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ClientProject,"project.godot","config_version=5"u8.ToArray());f.Write(SourceRoot.ClientProject,"Client.csproj","<Project Sdk=\"Godot.NET.Sdk/4.7.2\"/>"u8.ToArray());var root=Path.Combine(f.Profile.Roots[SourceRoot.BuildOutput],Guid.NewGuid().ToString("N"));var plan=await BuildPlan.CaptureAsync(store,f.WorkspaceRoot,Path.Combine(root,"snapshot"),Path.Combine(root,"output"));
        var runner=new BuildRunner(process:(command,ct)=>{if(command.Stage=="export"){File.WriteAllBytes(Path.Combine(plan.OutputRoot,"knightonline.pck"),"GDPCfake-resource-pack"u8.ToArray());File.WriteAllBytes(Path.Combine(plan.OutputRoot,"knightonline.exe"),"MZfixture"u8.ToArray());foreach(var name in new[]{"Client.dll","GodotSharp.dll","coreclr.dll","hostfxr.dll","hostpolicy.dll"}){var bytes=new byte[64];bytes[0]=(byte)'M';bytes[1]=(byte)'Z';File.WriteAllBytes(Path.Combine(plan.OutputRoot,name),bytes);}File.WriteAllText(Path.Combine(plan.OutputRoot,"Client.deps.json"),brokenJson?"invalid":"{}");File.WriteAllText(Path.Combine(plan.OutputRoot,"Client.runtimeconfig.json"),"{}");}return Task.FromResult(new BuildProcessResult(0,command.Stage=="sdk-version"?"10.0.103":"4.7.2.stable.mono",false));},verify:(_,_,_)=>Task.FromResult<IReadOnlyList<ValidationIssue>>([]));var result=await runner.RunAsync(plan,FakeTools(f),null,default);Assert.False(result.Succeeded);Assert.False(BuildManifest.Read(result.ManifestPath).Verified);
    }
    static AuthoringTools FakeTools(SourceFixture f)
    {var engine=f.Write(SourceRoot.Pipeline,"Godot editor.exe",[1]);var sdk=f.Write(SourceRoot.Pipeline,"dotnet sdk.exe",[1]);var template=f.Write(SourceRoot.Pipeline,"template.exe",[1]);return new(engine,sdk,null,template);}
}
