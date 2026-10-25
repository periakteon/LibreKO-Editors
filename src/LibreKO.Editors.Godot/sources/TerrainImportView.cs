using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public static class TerrainImportView
{
    public static void Show(EditorSession session,ServerSourceView view)
    {
        int generation=session.Sources.Generation;var dialog=Ui.Add(session.Shell,new ConfirmationDialog{Title="Import complete terrain sources",DialogHideOnOk=false,OkButtonText="Prepare source drafts",MinSize=new(780,490)});
        var body=Ui.Column(dialog);body.Position=new(18,42);body.CustomMinimumSize=new(740,370);
        Ui.Label(body,"Select pipeline-compatible height, splat and tile-atlas PNGs. These become reviewed source drafts under the selected terrain stem. Existing import sidecars remain intact; new raster keep-import sidecars are generated in isolated builds.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var stem=Ui.Field(body,"Destination terrain stem","e.g. moradon");var paths=new List<LineEdit>();
        foreach(var name in new[]{"heightmap.png","splat.png","tiles.png"})
        {
            var row=Ui.Row(body);var input=Ui.Field(row,name,"Absolute source PNG");paths.Add(input);Ui.Button(row,"Browse…",()=>
            {var picker=Ui.Add(session.Shell,new FileDialog{Access=FileDialog.AccessEnum.Filesystem,FileMode=FileDialog.FileModeEnum.OpenFile,Filters=["*.png ; Pipeline RGBA8 source"]});picker.FileSelected+=p=>{input.Text=p;picker.QueueFree();};picker.Canceled+=()=>picker.QueueFree();picker.PopupCenteredRatio(.7f);});
        }
        var status=Ui.Label(body,"",11,Ui.Accent);status.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        dialog.Confirmed+=()=>{try
        {
            if(generation!=session.Sources.Generation)throw new InvalidOperationException("Source profile changed; reopen import.");
            SourcePaths.ValidateRelative(stem.Text);if(stem.Text.Contains('/'))throw new InvalidDataException("Use one terrain directory name.");
            var bytes=paths.Select(p=>{SourcePaths.AssertNoLinks(p.Text);using var f=File.OpenRead(p.Text);var b=new byte[SourceLimits.CheckedLength(f.Length)];f.ReadExactly(b);return b;}).ToArray();TerrainImport.Validate(bytes[0],bytes[1],bytes[2]);
            var store=session.Sources.Store!;var keys=new[]{"heightmap.png","splat.png","tiles.png"}.Select(n=>store.Key(SourceRoot.ClientProject,"assets/terrain/"+stem.Text+"/"+n)).ToArray();
            // Validate the complete set before changing any draft.
            foreach(var key in keys)SourcePaths.Resolve(session.Sources.Profile!,key,true);
            for(int i=0;i<keys.Length;i++){var current=session.Sources.Drafts!.Documents.SingleOrDefault(d=>d.Snapshot.Key==keys[i])?.Snapshot??store.Read(keys[i]);view.OpenCandidate(new(current,store.Blobs.Put(bytes[i])));}
            dialog.Hide();dialog.QueueFree();
        }catch(Exception e){status.Text=e.Message;}};dialog.Canceled+=()=>dialog.QueueFree();dialog.PopupCentered();
    }
}
