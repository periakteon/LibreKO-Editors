using System.Diagnostics;
using System.Text.Json;
using Godot;
using LibreKO.Editors.Core.Sources;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Desktop;
public static class SourcePreviewCloseChecks
{
    // Native check driver, separate from the preview's close/lifetime implementation.
    public static async void Observe(Node owner,string path)
    {
        var action=System.Environment.GetEnvironmentVariable("LIBREKO_PREVIEW_CLOSE_CHECK");
        if(action is not("escape" or "window" or "button"))return;
        while(!File.Exists(path+".result.json"))await owner.ToSignal(owner.GetTree(),SceneTree.SignalName.ProcessFrame);
        await owner.ToSignal(owner.GetTree(),SceneTree.SignalName.ProcessFrame);
        GD.Print("SOURCE CLOSE CHECK "+action);
        if(action=="escape")Input.ParseInputEvent(new InputEventKey{Keycode=Key.Escape,Pressed=true});
        else if(action=="window")owner.GetWindow().EmitSignal(Window.SignalName.CloseRequested);
        else owner.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text.StartsWith("Close captured preview")).EmitSignal(BaseButton.SignalName.Pressed);
    }
    public static async Task Run(EditorShell shell)
    {
        var marker=SourceChecks.Marker(shell);var fixture=marker["fixtureRoot"]!.GetValue<string>();
        var saved=Path.Combine(fixture,"last-client-build-manifest.txt");
        if(!File.Exists(saved))await BuildChecks.Run(shell);
        var built=BuildManifest.Read(File.ReadAllText(saved).Trim());shell.Session.Sources.Select(built.Plan.Profile!);
        var sources=shell.Session.Sources;var key=sources.Store!.Key(SourceRoot.ClientProject,"assets/npcs/build_model.glb");
        var manifest=ClientSourceView.CreateManifest(sources,built.Plan,key,"build_model");
        foreach(var action in new[]{"escape","window","button"})
        {
            var path=Path.Combine(fixture,"preview-close-"+action+".json");File.WriteAllBytes(path,JsonSerializer.SerializeToUtf8Bytes(manifest,JsonConfig.Options));
            var start=new ProcessStartInfo(OS.GetExecutablePath()){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
            if(OS.HasFeature("editor")){start.ArgumentList.Add("--path");start.ArgumentList.Add(ProjectSettings.GlobalizePath("res://"));}
            foreach(var arg in new[]{"--","--source-preview",path})start.ArgumentList.Add(arg);start.Environment["LIBREKO_PREVIEW_CLOSE_CHECK"]=action;
            using var child=Process.Start(start)??throw new Exception("Could not launch source close fixture.");var output=child.StandardOutput.ReadToEndAsync();var errors=child.StandardError.ReadToEndAsync();
            try
            {
                var deadline=DateTime.UtcNow.AddSeconds(30);
                while(!child.HasExited&&DateTime.UtcNow<deadline)await shell.ToSignal(shell.GetTree().CreateTimer(.1),SceneTreeTimer.SignalName.Timeout);
                if(!child.HasExited){child.Kill(true);throw new Exception("Source preview did not close via "+action);}
                var text=await output+await errors;File.WriteAllText(path+".log",text);
                if(child.ExitCode!=0||!text.Contains("SOURCE PREVIEW CLOSING")||!text.Contains("SOURCE CLOSE CHECK "+action))throw new Exception("Source close path failed: "+action+" · "+text);
            }
            finally{if(!child.HasExited)child.Kill(true);}
        }
        if(shell.Session.Client.IsClosed||shell.Session.IsClosing)throw new Exception("Closing a captured preview closed its parent editor.");
        GD.Print("Captured source preview Escape, window close and Close button verified in three independent rendered processes");
    }
}
