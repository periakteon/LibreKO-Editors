using Godot;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public static class SourceChangeReview
{
    public static Task<bool> Show(EditorSession session,WorldChange? database,FileChange? files)
    {
        var completion=new TaskCompletionSource<bool>();bool dbDone=database==null||database.Rows.Count==0,fileDone=files==null||files.Files.Count==0;
        int source=session.Sources.Generation,scope=session.Generation;bool Current()=>!session.IsClosing&&source==session.Sources.Generation&&scope==session.Generation;
        var d=Ui.Add(session.Shell,new AcceptDialog{Name="SourceChangeReview",Title="Review pending destinations",MinSize=new(940,670)});
        var body=Ui.Column(d);body.Position=new(18,42);body.CustomMinimumSize=new(900,550);Ui.Heading(body,"DATABASE & SOURCE REVIEW",15);
        Ui.Label(body,"Each destination has its own validation, save result and history.",12,Ui.Muted);
        var view=Ui.Add(body,new TextEdit{Editable=false,CustomMinimumSize=new(880,390),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,WrapMode=TextEdit.LineWrappingMode.Boundary});
        var text=new System.Text.StringBuilder();
        if(database!=null)foreach(var row in database.Rows)
        {
            text.AppendLine("DATABASE · "+WorldWorkspace.TableLabel(row.Key.Table)+" · "+row.Key.Id+(row.Key.IsMonster is bool monster?monster?" · Monster":" · NPC":"")+(row.Key.Zone is int zone?" · Zone "+zone:""));
            if(row.Before!=null&&row.After!=null)
                foreach(var field in row.After.Where(p=>!System.Text.Json.Nodes.JsonNode.DeepEquals(row.Before.Values[p.Key],p.Value)))text.AppendLine(Ui.FieldName(field.Key)+": "+(row.Before.Values[field.Key]?.ToString()??"null")+" → "+(field.Value?.ToString()??"null"));
            else text.AppendLine(row.After==null?"Delete record":"Create record").AppendLine((row.After??row.Before?.Values)?.ToJsonString(new(){WriteIndented=true}));
            text.AppendLine();
        }
        if(files!=null)foreach(var file in files.Files)
        {
            text.AppendLine("SOURCE · "+file.Before.Key.Root+" / "+file.Before.Key.RelativePath).AppendLine("Before: "+(file.Before.Exists?file.Before.Sha256:"absent")).AppendLine("After: "+(file.After?.Sha256??"deleted"));
            foreach(var (label,blob) in new[]{("BEFORE",file.Before.Content),("AFTER",file.After)})if(blob!=null)
            {try{var raw=LibreKO.Editors.Core.Sources.TextDocument.Decode(session.Sources.Blobs!.Read(blob)).Text;text.AppendLine(label+"\n"+(raw.Length>16000?raw[..16000]+"\n… full source retained":raw));}catch(System.IO.InvalidDataException){text.AppendLine(label+" · binary source · "+blob.Length+" bytes");}}
        }
        view.Text=text.ToString();var actions=Ui.Add(body,new HFlowContainer());var status=Ui.Label(body,"Nothing has been applied from this review.",12,Ui.Accent);status.AutowrapMode=TextServer.AutowrapMode.WordSmart;status.MaxLinesVisible=4;
        if(database!=null)_=Names();
        async Task Names(){try{var names=await WorldReferenceNames.DescribeAsync(session,database!.Rows);if(Current()&&GodotObject.IsInstanceValid(view))view.Text=text+"\nNAMED REFERENCES\n"+names;}catch{/* Raw reviewed values remain available when reference names are unavailable. */}}
        Button dbButton=null!,fileButton=null!;
        void Buttons(){dbButton.Disabled=dbDone||!session.Connected||!Current();fileButton.Disabled=fileDone||!Current();d.GetOkButton().Text=dbDone&&fileDone?"Done":"Close review";}
        void Finish(){if(dbDone&&fileDone){completion.TrySetResult(true);d.Hide();d.QueueFree();}}
        dbButton=Ui.Button(actions,"Save database changes",()=>_ = SaveDatabase());fileButton=Ui.Button(actions,"Apply file changes",()=>_ = ApplyFiles());
        Ui.Button(actions,"Production steps…",()=>HelpView.Show(session,LibreKO.Editors.Core.Help.HelpCatalog.ForChanges(files?.Files.Select(f=>f.Before.Key)??[],database?.Rows.Count>0),
            (database?.Rows.Select(r=>$"Database · {r.Key.Table} · {r.Key.Id}")??[]).Concat(files?.Files.Select(f=>$"Source · {f.Before.Key.Root}/{f.Before.Key.RelativePath}")??[]).ToArray()));
        Ui.Button(actions,"File history / recovery…",()=>{completion.TrySetResult(false);d.Hide();d.QueueFree();SourceHistoryView.Show(session);});
        async Task SaveDatabase()
        {
            if(dbDone||database==null||!Current())return;dbButton.Disabled=true;
            await session.RunAsync(async()=>
            {
                var saved=await session.Client.SendAsync<WorldSaveResult>("world.save",database);if(!Current())return;
                status.Text="Database: "+saved.Status+" · "+(saved.Message??string.Join("; ",saved.Issues?.Select(x=>x.Field+": "+x.Message)??[]));
                if(saved.Status==SaveStatus.Saved){dbDone=true;session.NotifyDatabaseChange();if(session.Shell.Workspaces.GetValueOrDefault("world") is WorldWorkspace world)await world.AcceptSavedAsync();}
                else if(saved.Status==SaveStatus.UnknownCommit){session.Connected=false;status.Text+=" · reconcile World history before retrying";}
            });if(GodotObject.IsInstanceValid(d)){Buttons();Finish();}
        }
        async Task ApplyFiles()
        {
            if(fileDone||files==null||!Current())return;fileButton.Disabled=true;
            await session.RunAsync(OperationKind.Source,async ct=>
            {
                var outcome=await session.Sources.ApplyAsync(files,ct);if(!Current())return;
                fileDone=outcome.State==FileApplyState.Applied;
                status.Text="Files: "+outcome.State+" · "+string.Join("; ",outcome.Targets.Select(x=>x.Key.RelativePath+": "+x.State))+"\n"+string.Join("; ",outcome.Issues.Select(x=>x.Field+": "+x.Message));
                if(!fileDone)status.Text+="\nSaved subsets remain saved. Review file history before retrying. Operation "+outcome.OperationId;
            });if(GodotObject.IsInstanceValid(d)){Buttons();Finish();}
        }
        void Close(){completion.TrySetResult(dbDone&&fileDone);d.Hide();d.QueueFree();}
        d.Confirmed+=Close;d.Canceled+=Close;d.CloseRequested+=Close;d.TreeExiting+=()=>completion.TrySetResult(dbDone&&fileDone);Buttons();d.PopupCentered();return completion.Task;
    }
}
