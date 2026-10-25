using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public static class ClientBuildView
{
    public static void Show(EditorSession session)
    {
        var dialog=Ui.Add(session.Shell,new AcceptDialog{Title="Build authored client",MinSize=new(850,570)});
        var body=Ui.Column(dialog);body.Position=new(18,42);body.CustomMinimumSize=new(810,470);
        Ui.Heading(body,"CLIENT BUILD",18);
        Ui.Label(body,"Save source drafts before a normal build. The build captures source files, imports resources and exports into a new isolated output. Install or deploy the verified output separately.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var tools=session.Sources.Profile?.Tools;var status=Ui.Label(body,"Godot Mono: "+(tools?.GodotEditor??"not configured")+"\n.NET SDK: "+(tools?.DotnetSdk??"not configured")+"\nTemplate: "+(tools?.ExportTemplate??"not configured"),11,Ui.Muted);status.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var log=Ui.Add(body,new TextEdit{Editable=false,CustomMinimumSize=new(0,280),SizeFlagsVertical=Control.SizeFlags.ExpandFill});
        var runner=new BuildRunner(verify:ContentProbeProject.VerifyPlanAsync);
        string? output=null;var bar=Ui.Row(body);var build=Ui.Button(bar,"Save drafts & build",()=>{});build.Pressed+=()=>_ = Build();Ui.Button(bar,"Cancel build",session.Cancel);Ui.Button(bar,"Open output",()=>{if(output!=null)OS.ShellOpen(output);});
        Ui.Button(bar,"Production steps…",()=>HelpView.Show(session,"client"));
        async Task Build()
        {
            if(!await session.Shell.CanLeaveAsync())return;build.Disabled=true;
            await session.RunAsync(OperationKind.Build,async ct=>
            {
                try{var plan=await BuildProjectFactory.PrepareAsync(session.Sources,false,ct);var result=await runner.RunAsync(plan,session.Sources.Profile!.Tools!,new LogProgress(log),ct);output=plan.OutputRoot;if(GodotObject.IsInstanceValid(dialog)){status.Text=(result.Succeeded?"Verified client output: ":"Build failed: ")+result.ManifestPath;log.Text+=string.Join("\n",result.Issues.Select(i=>i.Field+": "+i.Message));}}
                finally{if(GodotObject.IsInstanceValid(build))build.Disabled=false;}
            });
            if(GodotObject.IsInstanceValid(build))build.Disabled=false;
        }
        dialog.Canceled+=Close;dialog.Confirmed+=Close;dialog.PopupCentered();
        void Close(){runner.StopOwned();ContentProbeProject.StopOwned();dialog.QueueFree();}
    }
    sealed class LogProgress(TextEdit log):IProgress<string>{public void Report(string message){Callable.From(()=>{if(GodotObject.IsInstanceValid(log)){log.Text=(log.Text+message+"\n");if(log.Text.Length>100000)log.Text=log.Text[^80000..];log.ScrollVertical=log.GetLineCount();}}).CallDeferred();}}
}
