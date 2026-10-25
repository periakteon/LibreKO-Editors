using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;

public partial class ClientSourceView:ServerSourceView
{
    readonly EditorSession session;readonly Label previewStatus;readonly Func<System.Text.Json.Nodes.JsonObject?>? prototype;
    public ClientSourceView(EditorSession session,Func<System.Text.Json.Nodes.JsonObject?>? prototype=null):base(session)
    {
        this.session=session;this.prototype=prototype;
        var bar=Ui.Add(this,new HFlowContainer());MoveChild(bar,0);
        Ui.Button(bar,"Preview captured source…",()=>_ = PreviewAsync());
        Ui.Button(bar,"Stop source preview",()=>{session.Sources.Previews.Invalidate();Refresh();});
        Ui.Button(bar,"Build client…",()=>ClientBuildView.Show(session));
        Ui.Button(bar,"Import terrain…",()=>TerrainImportView.Show(session,this));
        Ui.Button(bar,"Source roots & tools…",()=>SourceSettingsView.Show(session));
        previewStatus=Ui.Label(this,"Source preview runs in a fresh window · installed previews remain reference views",11,Ui.Muted);MoveChild(previewStatus,1);
        previewStatus.AutowrapMode=TextServer.AutowrapMode.WordSmart;previewStatus.MaxLinesVisible=2;
        session.Sources.Previews.Changed+=Refresh;SelectionChanged+=Refresh;
    }
    void Refresh(){if(IsInsideTree())previewStatus.Text=session.Sources.Previews.Status;}
    public async Task PreviewAsync()
    {
        var document=SelectedDocument;if(document==null||document.Snapshot.Key.Root!=SourceRoot.ClientProject){session.Shell.SetStatus("Select a client model, texture or terrain source first.");return;}
        var key=document.Snapshot.Key;var stem=SelectedModelStem;
        await session.RunAsync(OperationKind.Build,async ct=>
        {
            var plan=await BuildProjectFactory.PrepareAsync(session.Sources,true,ct);
            var manifest=CreateManifest(session.Sources,plan,key,stem) with{Prototype=prototype?.Invoke()};
            await session.Sources.Previews.OpenAsync(manifest,ct);Refresh();
        });
    }
    internal static SourcePreviewManifest CreateManifest(SourceSession sources,ClientBuildPlan plan,SourceFileKey key,string? stem=null)
    {
        string kind="model";if(key.RelativePath.StartsWith("assets/terrain/",StringComparison.Ordinal)){kind="map";stem=key.RelativePath.Split('/')[2];}
        else if(key.RelativePath.EndsWith(".png",StringComparison.Ordinal))kind="texture";
        else if(string.IsNullOrWhiteSpace(stem)&&key.RelativePath.EndsWith(".glb",StringComparison.Ordinal))stem=Path.GetFileNameWithoutExtension(key.RelativePath);
        if(kind=="model"&&string.IsNullOrWhiteSpace(stem))throw new InvalidOperationException("Choose a model stem in the NPC source visual view first.");
        var file=plan.SnapshotFiles.SingleOrDefault(f=>f.RelativePath==key.RelativePath)??throw new InvalidDataException("Selected source is absent from the captured snapshot.");
        return new(plan.Id,plan.SourceProfileId,sources.Generation,plan.PreviewDraft?.Files.Select(f=>f.Before).ToArray()??[],plan.SnapshotRoot)
        {Build=plan,Target=key,SourceRevision=sources.Revision,DraftVersion=sources.Drafts!.Version,Kind=kind,Stem=stem,ExpectedSourceHash=file.Sha256};
    }
    public override void _ExitTree(){session.Sources.Previews.Changed-=Refresh;base._ExitTree();}
}
