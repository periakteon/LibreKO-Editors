using System.Globalization;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Maps;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;

public partial class TerrainAuthoringView:VBoxContainer
{
    readonly EditorSession session;readonly SourceDocument primary;readonly int generation;
    readonly CancellationTokenSource lifetime;readonly TaskCompletionSource ready=new();readonly Label status;
    readonly Dictionary<(int X,int Z),TerrainStroke> samples=[];HeightField? strokeOriginal;GroundSurface? groundOriginal;
    readonly Stack<SourceDocument> undo=[],redo=[];
    HeightField? heights;GroundSurface? ground;SourceDocument? heightDocument,splatDocument,atlasDocument;
    int serial,paintTile;byte paintDirection;string mode="raise";bool active=true,rendered;
    public WorldViewport Viewport{get;}
    public TerrainTools Tools{get;}
    public Task ReadyTask=>ready.Task;
    public bool BrushEnabled{get;set;}
    public HeightField? Heights=>heights;
    public SourceDocument HeightDocument=>heightDocument??throw new InvalidOperationException("Client height source is unavailable.");
    public string DisplayedSourceHash{get;private set;}="";
    public TerrainAuthoringView(EditorSession session,SourceDocument document)
    {
        this.session=session;primary=document;generation=session.Sources.Generation;lifetime=CancellationTokenSource.CreateLinkedTokenSource(session.Sources.Cancellation);SizeFlagsVertical=SizeFlags.ExpandFill;SizeFlagsHorizontal=SizeFlags.ExpandFill;
        Tools=Ui.Add(this,new TerrainTools(session,this));Viewport=Ui.Add(this,new WorldViewport(session,_=>{},true));Viewport.AuthoringInput=Input;
        status=Ui.Label(this,"Loading authored source bytes…",11,Ui.Accent);status.AutowrapMode=TextServer.AutowrapMode.WordSmart;status.MaxLinesVisible=3;
    }
    public override async void _Ready()
    {
        try
        {
            var store=session.Sources.Store!;var directory=Path.GetDirectoryName(primary.Snapshot.Key.RelativePath)!.Replace('\\','/');
            heightDocument=session.Sources.Drafts!.Open(store.Key(SourceRoot.ClientProject,directory+"/heightmap.png"));
            try{splatDocument=session.Sources.Drafts.Open(store.Key(SourceRoot.ClientProject,directory+"/splat.png"));atlasDocument=session.Sources.Drafts.Open(store.Key(SourceRoot.ClientProject,directory+"/tiles.png"));}catch(Exception e){Report("Ground sources unavailable · "+e.Message);}
            await Reload();
        }
        catch(Exception e){Report("Source terrain unavailable · "+e.Message);}
        finally{ready.TrySetResult();}
    }
    void Ensure(){if(generation!=session.Sources.Generation||lifetime.IsCancellationRequested||heightDocument==null)throw new InvalidOperationException("Terrain source context changed or is unavailable.");}
    public async Task Reload()
    {
        Ensure();heights=ClientHeightmapCodec.Decode(heightDocument!.Render());ground=null;
        if(splatDocument!=null&&atlasDocument!=null)try{ground=GroundSurfaceCodec.Decode(splatDocument.Render(),atlasDocument.Render());if(ground.Size!=heights.Size){ground=null;Report("Splat/height dimensions differ; height editing remains separate.");}}catch(Exception e){Report("Ground preview unavailable · "+e.Message);}
        Tools.SetAtlas(ground);await Render();
    }
    async Task Render()
    {
        int current=++serial;var pose=rendered?Viewport.CaptureCamera():null;try
        {
            if(heights==null||!IsInsideTree())return;
            await Viewport.LoadMapAsync(heights,new(WorldTable.NpcPositions,Zone:32767),null,lifetime.Token,includeDatabase:false,authoredSurface:ground);
            if(current!=serial||!IsInstanceValid(this)||lifetime.IsCancellationRequested)return;
            if(pose==null)Viewport.FrameTerrain();else Viewport.RestoreCamera(pose);rendered=true;
            DisplayedSourceHash=heightDocument!.Capture(session.Sources.Store!).After!.Sha256;
            Viewport.DescribeSource("Authored loose source · "+heightDocument.Snapshot.Key.RelativePath+" · "+DisplayedSourceHash[..16]);Viewport.SetActive(active);
            Report("Visual terrain draft · server SMD changes require a separate linked review"+(ground?.TileCount>512?" · atlas preview exceeds 512-layer budget; tile selection remains available":""));
        }
        catch(OperationCanceledException){}catch(Exception e){Report(e.Message);}
    }
    public void BeginStroke(TerrainStroke stroke,int tile=0,byte direction=0)
    {
        Ensure();samples.Clear();strokeOriginal=ClientHeightmapCodec.Decode(heightDocument!.Render());groundOriginal=ground==null?null:ground with{Cells=ground.Cells.ToArray()};mode=stroke.Tool;paintTile=tile;paintDirection=direction;Sample(stroke);
    }
    public void Sample(TerrainStroke stroke)
    {
        Ensure();if(strokeOriginal==null)return;if(stroke.X<0||stroke.Z<0||stroke.X>=strokeOriginal.Size||stroke.Z>=strokeOriginal.Size)return;
        samples[(stroke.X,stroke.Z)]=stroke;
        if(mode.StartsWith("paint-",StringComparison.Ordinal))
        {
            if(groundOriginal==null)throw new InvalidOperationException("A matching splat and tile atlas are required for surface painting.");
            if(paintDirection>7||paintTile!=65535&&(paintTile<0||paintTile>=groundOriginal.TileCount||paintTile==255))throw new InvalidDataException("Select an available tile (255 is reserved) or NoTile65535, with direction0–7.");
            var cells=groundOriginal.Cells.ToArray();foreach(var s in samples.Values)for(int z=Math.Max(0,s.Z-s.Radius);z<=Math.Min(groundOriginal.Size-1,s.Z+s.Radius);z++)for(int x=Math.Max(0,s.X-s.Radius);x<=Math.Min(groundOriginal.Size-1,s.X+s.Radius);x++)if((x-s.X)*(x-s.X)+(z-s.Z)*(z-s.Z)<=s.Radius*s.Radius)
            {int at=z*groundOriginal.Size+x;cells[at]=mode=="paint-primary"?cells[at] with{Primary=paintTile,Direction1=paintDirection}:cells[at] with{Secondary=paintTile,Direction2=paintDirection};}
            ground=groundOriginal with{Cells=cells};
        }
        else heights=TerrainBrush.ApplyStroke(strokeOriginal,samples.Values.ToArray());
        _=Render();
    }
    public void CommitStroke()
    {
        Ensure();if(strokeOriginal==null)return;
        if(mode.StartsWith("paint-",StringComparison.Ordinal)){if(ground!=null&&groundOriginal!=null&&!ground.Cells.SequenceEqual(groundOriginal.Cells)){splatDocument!.EditBytes(GroundSurfaceCodec.EncodeSplat(ground));Record(splatDocument);}}
        else if(heights!=null&&!SameBits(heights.Heights,strokeOriginal.Heights)){heightDocument!.EditBytes(ClientHeightmapCodec.Encode(heights));Record(heightDocument);}
        strokeOriginal=null;groundOriginal=null;samples.Clear();Report("Stroke recorded as one source undo step · unapplied source bytes");
        DisplayedSourceHash=heightDocument!.Capture(session.Sources.Store!).After!.Sha256;
    }
    public void CancelStroke(){strokeOriginal=null;groundOriginal=null;samples.Clear();if(heightDocument!=null)_=Reload();}
    public void Flush(){if(strokeOriginal!=null)CommitStroke();}
    public void SetNumeric(int x,int z,float value)
    {
        Ensure();if(!float.IsFinite(value))throw new InvalidDataException("Height must be finite.");var field=ClientHeightmapCodec.Decode(heightDocument!.Render());var changed=TerrainBrush.ImportRegion(field,x,z,1,1,[value]);if(!SameBits(field.Heights,changed.Heights)){heightDocument.EditBytes(ClientHeightmapCodec.Encode(changed));Record(heightDocument);}heights=changed;_ = Render();
    }
    void Record(SourceDocument document){undo.Push(document);redo.Clear();}
    public void Undo(bool forward=false)
    {
        Ensure();var from=forward?redo:undo;var to=forward?undo:redo;bool done=false;
        while(from.TryPop(out var document))if(document.Undo(forward)){to.Push(document);done=true;break;}
        if(!done)(primary.Snapshot.Key.RelativePath.EndsWith("splat.png",StringComparison.Ordinal)?splatDocument:heightDocument)?.Undo(forward);_=Reload();
    }
    public void ImportRegion(int x,int z,int width,int height,float[] values)
    {Ensure();var current=ClientHeightmapCodec.Decode(heightDocument!.Render());var changed=TerrainBrush.ImportRegion(current,x,z,width,height,values);if(!SameBits(current.Heights,changed.Heights)){heightDocument.EditBytes(ClientHeightmapCodec.Encode(changed));Record(heightDocument);}_=Reload();}
    public float[] ExportRegion(int x,int z,int width,int height){Ensure();return TerrainBrush.ExportRegion(ClientHeightmapCodec.Decode(heightDocument!.Render()),x,z,width,height);}
    public FileChange PrepareLink(SourceFileKey serverKey,bool mappingReviewed=false)
    {
        Ensure();Flush();var doc=session.Sources.Drafts!.Open(serverKey);var server=SmdCodec.Read(doc.Render());var result=TerrainAlignment.Prepare(server,doc.Snapshot,heightDocument!.Snapshot,ClientHeightmapCodec.Decode(heightDocument.Render()),session.Sources.Store!,mappingReviewed);
        if(result.Issues.Count>0)throw new InvalidDataException(string.Join("; ",result.Issues.Select(i=>i.Message)));
        foreach(var file in result.Change.Files){var target=file.Before.Key==serverKey?doc:heightDocument;target.EditBytes(session.Sources.Blobs!.Read(file.After!));}
        Report("Linked server/client grid draft prepared · reviewed independently on Apply · server triangles and client bilinear interpolation still differ between vertices");return result.Change;
    }
    bool Input(InputEvent input)
    {
        if(!BrushEnabled||!active)return false;
        try
        {
            if(input is InputEventKey{Pressed:true,Keycode:Key.Escape}){CancelStroke();return true;}
            if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left} button)
            {
                if(!button.Pressed){if(strokeOriginal!=null)CommitStroke();return true;}
                if(Viewport.SourcePointer(button.Position) is {} p&&heights!=null){var s=Tools.Stroke((int)Math.Round(p.X/heights.CellSize),(int)Math.Round(p.Z/heights.CellSize));BeginStroke(s,Tools.Tile,Tools.Direction);}return true;
            }
            if(input is InputEventMouseMotion motion&&strokeOriginal!=null&&(motion.ButtonMask&MouseButtonMask.Left)!=0&&Viewport.SourcePointer(motion.Position) is {} at)
            {var first=samples.Values.First();Sample(first with{X=(int)Math.Round(at.X/strokeOriginal.CellSize),Z=(int)Math.Round(at.Z/strokeOriginal.CellSize)});return true;}
        }
        catch(Exception e){Report(e.Message);CancelStroke();return true;}
        return false;
    }
    public void Report(string text){if(IsInstanceValid(this)&&IsInsideTree())status.Text=text;}
    public void SetActive(bool value){active=value;if(!value&&strokeOriginal!=null)CancelStroke();Viewport.SetActive(value);}
    static bool SameBits(float[] a,float[] b){if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(BitConverter.SingleToInt32Bits(a[i])!=BitConverter.SingleToInt32Bits(b[i]))return false;return true;}
    public override void _ExitTree(){lifetime.Cancel();lifetime.Dispose();Viewport.AuthoringInput=null;}
}
