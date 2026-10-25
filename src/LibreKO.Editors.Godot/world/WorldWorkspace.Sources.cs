using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public partial class WorldWorkspace
{
    Control databaseBody=null!;VBoxContainer workspaceColumn=null!;
    internal ServerSourceView? SourceView;
    bool sourceMode;int sourceViewGeneration=-1;
    internal void ShowDatabase(){Session.Shell.RememberContext();sourceMode=false;if(SourceView!=null)SourceView.SetActive(false);databaseBody.Visible=true;MapView.SetActive(Active&&MapView.Visible);Session.Shell.RecordLocation();}
    internal void ShowSources()
    {
        if(Session.Sources.Profile==null){SourceSettingsView.Show(Session);return;}ExitMapExpansion();
        if(SourceView==null||sourceViewGeneration!=Session.Sources.Generation)
        {if(SourceView!=null){workspaceColumn.RemoveChild(SourceView);SourceView.QueueFree();}SourceView=Ui.Add(workspaceColumn,new ClientSourceView(Session,()=>Selected?.Table==WorldTable.Npcs?Draft?.Rows.SingleOrDefault(r=>r.Key==Selected)?.Values.DeepClone().AsObject():null));SourceView.ReturnRequested+=ShowDatabase;sourceViewGeneration=Session.Sources.Generation;}
        Session.Shell.RememberContext();sourceMode=true;databaseBody.Visible=false;MapView.SetActive(false);SourceView.SetActive(Active);Session.Shell.RecordLocation();
    }
    async Task CompareSeed()
    {
        if(Selected==null||Draft==null)return;
        try
        {
            var row=Draft.Rows.Single(x=>x.Key==Selected);var store=Session.Sources.Store??throw new InvalidOperationException("Select server source roots first.");var root=store.Profile.Roots[SourceRoot.ServerData];
            var names=SeedShardResolver.Active(row.Key.Table,Directory.GetFiles(root,"*.json").Select(Path.GetFileName).OfType<string>());var docs=new List<JsonSourceDocument>();
            foreach(var name in names)
            {
                var key=new SourceFileKey(store.Profile.Id,SourceRoot.ServerData,SourceIdentity.Root(root),name);
                if(Session.Sources.Drafts?.Documents.SingleOrDefault(x=>x.Snapshot.Key==key) is {} opened)docs.Add(opened.Json());
                else{var snapshot=store.Read(key);docs.Add(JsonSourceDocument.Open(snapshot,store.Blobs.Read(snapshot.Content!)));}
            }
            var choice=await SeedComparisonView.Show(Session,row,docs);if(choice==null)return;
            if(choice.Database!=null){ApplyBatch(choice.Database.Rows);ShowDatabase();}
            if(choice.Files!=null){ShowSources();foreach(var file in choice.Files.Files)SourceView!.OpenCandidate(file);}
        }
        catch(Exception e){Session.Shell.SetStatus(e.Message);}
    }
}
