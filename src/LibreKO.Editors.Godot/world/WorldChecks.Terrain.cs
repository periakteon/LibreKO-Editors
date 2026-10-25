using System.Text;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Maps;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public static partial class WorldChecks
{
    static async Task Terrain(EditorShell shell)
    {
        _=SourceChecks.Marker(shell);var session=shell.Session;var store=session.Sources.Store!;var profile=store.Profile;var stem="native-terrain";
        SourceFileKey Key(SourceRoot root,string relative)=>store.Key(root,relative);
        void Write(SourceFileKey key,byte[] bytes){var path=SourcePaths.Resolve(profile,key,true);Directory.CreateDirectory(Path.GetDirectoryName(path)!);SourcePaths.Resolve(profile,key,true);File.WriteAllBytes(path,bytes);}
        var heightKey=Key(SourceRoot.ClientProject,"assets/terrain/"+stem+"/heightmap.png");var splatKey=Key(SourceRoot.ClientProject,"assets/terrain/"+stem+"/splat.png");var atlasKey=Key(SourceRoot.ClientProject,"assets/terrain/"+stem+"/tiles.png");var smdKey=Key(SourceRoot.ServerMaps,stem+".smd");
        var field=new HeightField(3,4,[0,1,2,3,4,5,6,7,8]);var atlas=new byte[2*2*2*4];for(int i=0;i<8;i++){atlas[i*4]=(byte)(i<4?70:180);atlas[i*4+1]=(byte)(i<4?130:90);atlas[i*4+2]=55;atlas[i*4+3]=255;}
        Write(heightKey,ClientHeightmapCodec.Encode(field));Write(splatKey,GroundSurfaceCodec.EncodeSplat(new(3,2,2,Enumerable.Range(0,9).Select(_=>new GroundCell(0,65535,0,0)).ToArray(),atlas)));Write(atlasKey,RgbaPngCodec.Encode(new(2,4,atlas)));
        using(var bytes=new MemoryStream()){using(var w=new BinaryWriter(bytes,Encoding.UTF8,true)){w.Write(3);w.Write(4f);for(int x=0;x<3;x++)for(int z=0;z<3;z++)w.Write(field.Heights[z*3+x]);w.Write(8f);w.Write(8f);w.Write(0);w.Write(0u);w.Write(0);for(int i=0;i<9;i++)w.Write((short)1);w.Write(0);w.Write(0);}Write(smdKey,bytes.ToArray());}
        var beforeHeight=store.Read(heightKey);var beforeSmd=store.Read(smdKey);var beforeSplat=store.Read(splatKey);
        await shell.RequestWorkspaceAsync("world");var app=(WorldWorkspace)shell.Active!;app.ShowSources();await app.SourceView!.OpenAsync(heightKey);await app.SourceView.PreviewReady;
        TerrainAuthoringView View()=>app.SourceView.FindChildren("*","",true,false).OfType<TerrainAuthoringView>().Single(v=>v.IsVisibleInTree());
        var view=View();if(view.Heights==null)throw new Exception("Authored terrain did not load.");
        view.Viewport.TopView();view.Viewport.ProcessInput(new InputEventMouseButton{ButtonIndex=MouseButton.WheelUp,Pressed=true});var camera=view.Viewport.FindChildren("*","Camera3D",true,false).OfType<Camera3D>().Single();var pose=camera.Transform;var cameraSize=camera.Size;
        view.BeginStroke(new("raise",1,1,0,5,0));view.Sample(new("raise",1,1,0,5,0));if(view.HeightDocument.IsDirty)throw new Exception("Brush preview changed source before release.");view.CommitStroke();
        if(camera.Transform!=pose||camera.Size!=cameraSize)throw new Exception("A terrain brush reset the operator camera.");
        if(ClientHeightmapCodec.Decode(view.HeightDocument.Render()).Heights[4]!=9||session.Sources.CaptureDrafts()!.Files.Count!=1)throw new Exception("Brush repeated per frame or visual-only source count differs.");
        view.HeightDocument.Undo();await view.Reload();if(view.HeightDocument.IsDirty)throw new Exception("Terrain stroke was not one source undo.");view.HeightDocument.Redo();await view.Reload();
        view.BeginStroke(new("paint-primary",1,1,0,1,0),1,7);view.CommitStroke();var splat=session.Sources.Drafts!.Documents.Single(d=>d.Snapshot.Key==splatKey);var painted=GroundSurfaceCodec.Decode(splat.Render(),store.Blobs.Read(store.Read(atlasKey).Content!));if(painted.Cells[4]!=new GroundCell(1,65535,7,0))throw new Exception("Ground tile/direction paint was lost.");
        view.Undo();await view.Reload();if(GroundSurfaceCodec.Decode(splat.Render(),store.Blobs.Read(store.Read(atlasKey).Content!)).Cells[4].Primary!=0||ClientHeightmapCodec.Decode(view.HeightDocument.Render()).Heights[4]!=9)throw new Exception("Terrain context undo targeted the wrong source file.");
        view.Undo(true);await view.Reload();view.Viewport.ProcessInput(new InputEventMouseMotion{Relative=new(25,100),ButtonMask=MouseButtonMask.Right});
        var linked=view.PrepareLink(smdKey);if(linked.Files.Count!=2||session.Sources.CaptureDrafts()!.Files.Count!=3)throw new Exception("Linked server/client draft did not retain independent ground edits.");
        foreach(var before in new[]{beforeHeight,beforeSmd,beforeSplat})if(store.Read(before.Key).Sha256!=before.Sha256)throw new Exception("Terrain authoring wrote a target before Apply.");
        // Unique virtual paths let unchanged stock readers independently decode the staged bytes, without shadowing any installed resource.
        var unique="editor_probe_"+Guid.NewGuid().ToString("N");var packPath=SourcePaths.Resolve(profile,Key(SourceRoot.BuildOutput,unique+".pck"),false);using(var packer=new PckPacker())
        {
            if(packer.PckStart(packPath)!=Error.Ok)throw new Exception("Cannot prepare independent terrain reader pack.");
            void Add(string name,byte[] bytes){if(packer.AddFileFromBuffer("res://assets/terrain/"+unique+"/"+name,bytes)!=Error.Ok)throw new Exception("Cannot add probe resource.");}
            Add("heightmap.png",view.HeightDocument.Render());Add("splat.png",splat.Render());Add("tiles.png",store.Blobs.Read(store.Read(atlasKey).Content!));if(packer.Flush()!=Error.Ok)throw new Exception("Cannot finalize independent terrain pack.");
        }
        if(!ProjectSettings.LoadResourcePack(packPath,false))throw new Exception("Cannot mount independent fixture paths.");var stock=LibreKO.Heightmap.LoadPng(unique)!;var nativeGround=LibreKO.GroundTextures.LoadPng(unique)!;
        if(stock.Heights[4]!=9||nativeGround.CellLayer[4]!=1||nativeGround.Dir1[4]!=7)throw new Exception("Stock client readers disagree with authored bytes.");
        using(var image=new Image()){if(image.LoadPngFromBuffer(view.HeightDocument.Render())!=Error.Ok||!image.GetData().AsSpan().SequenceEqual(RgbaPngCodec.Decode(view.HeightDocument.Render()).Pixels))throw new Exception("Godot PNG reader altered float bytes.");}
        foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,800)}){shell.GetWindow().Size=size;shell.GetViewport().WarpMouse(new(4,4));await EventGalleries.Capture(shell,"world-terrain",size);}
        var review=SourceChangeReview.Show(session,null,session.Sources.CaptureDrafts());await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame);var dialog=shell.GetChildren().OfType<AcceptDialog>().Single(d=>d.Name=="SourceChangeReview");dialog.EmitSignal(AcceptDialog.SignalName.Canceled);if(await review||!app.HasUnsavedChanges)throw new Exception("Terrain review cancellation lost source drafts.");
        app.Discard();await app.SourceView.PreviewReady;view=View();if(view.HeightDocument.IsDirty||view.Heights?.Heights[4]!=4)throw new Exception("Discard did not refresh all authored terrain dependencies.");
    }
}
