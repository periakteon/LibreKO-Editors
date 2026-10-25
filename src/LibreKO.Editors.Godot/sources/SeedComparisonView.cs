using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;

namespace LibreKO.Editors.Desktop;

public record SeedComparisonChoice(WorldChange? Database,FileChange? Files);
public static class SeedComparisonView
{
    public static Task<SeedComparisonChoice?> Show(EditorSession session,WorldRowSnapshot record,IReadOnlyList<JsonSourceDocument> documents)
    {
        var result=new TaskCompletionSource<SeedComparisonChoice?>();
        var match=SeedRowMatcher.Match(record,documents);
        var candidates=documents.Where(x=>ServerJsonCatalog.Format(x.Snapshot.Key.RelativePath)?.Table==record.Key.Table)
            .SelectMany(d=>d.Rows.Select(r=>(Doc:d,Row:r))).ToArray();
        var dialog=Ui.Add(session.Shell,new AcceptDialog{Title="Database / server source comparison",MinSize=new(780,600)});
        var body=Ui.Column(dialog); body.Position=new(18,42); body.CustomMinimumSize=new(740,0);
        Ui.Heading(body,"EXPLICIT DESTINATIONS",14);
        Ui.Label(body,$"{record.Key.Table} · {record.Key.Id} · {match.State} semantic match ({match.Candidates.Count})",13,Ui.Accent);
        Ui.Label(body,SeedShardResolver.StructureWarning(record.Key.Table),12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var picker=Ui.Add(body,new OptionButton{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill});
        foreach(var item in candidates) picker.AddItem(item.Doc.Snapshot.Key.RelativePath+" · "+item.Row.Token.JsonPointer+(match.Candidates.Contains(item.Row.Token)?" · semantic match":" · different values"));
        var diff=Ui.Add(body,new TextEdit{Editable=false,CustomMinimumSize=new(700,330),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill});
        void Refresh()
        {
            if(picker.Selected<0 || picker.Selected>=candidates.Length) { diff.Text="No supported seed rows. Create/open the source explicitly."; return; }
            var item=candidates[picker.Selected];
            diff.Text="DATABASE ("+record.Version+")\n"+record.Values.ToJsonString(new(){WriteIndented=true})+"\n\nSOURCE ("+item.Row.Token.FileHash+")\n"+item.Row.Values.ToJsonString(new(){WriteIndented=true});
        }
        picker.ItemSelected+=_=>Refresh(); Refresh();
        var actions=Ui.Row(body); var error=Ui.Label(body,"",12,Ui.Accent);
        void Choose(bool database)
        {
            try
            {
                if(picker.Selected<0 || picker.Selected>=candidates.Length) return;
                var item=candidates[picker.Selected];
                var choice=database?new SeedComparisonChoice(SeedComparison.PrepareDatabaseUpdate(item.Doc,item.Row.Token,record,record.Key,session.Sources.WorldCatalog),null)
                    :new SeedComparisonChoice(null,SeedComparison.PrepareSeedUpdate(record,item.Doc,item.Row.Token,session.Sources.Store!));
                result.TrySetResult(choice); dialog.Hide(); dialog.QueueFree();
            }
            catch(Exception e) { error.Text=e.Message; }
        }
        Ui.Button(actions,"Prepare database draft",()=>Choose(true)); Ui.Button(actions,"Prepare file draft",()=>Choose(false));
        void Close() { result.TrySetResult(null); dialog.QueueFree(); }
        dialog.Confirmed+=Close; dialog.Canceled+=Close; dialog.CloseRequested+=Close; dialog.TreeExiting+=()=>result.TrySetResult(null);
        dialog.PopupCentered(); return result.Task;
    }
}
