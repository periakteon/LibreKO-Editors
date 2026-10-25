using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Maps;
namespace LibreKO.Editors.Desktop;

public sealed class SmdMapLayers(WorldViewport viewport,EditorSession session)
{
    readonly Dictionary<(string Kind,int Ordinal),Node3D> nodes=[];
    int generation;
    SmdDocument? gesture;
    MapPoint? dragStart;
    public SmdDraft Draft{get;private set;}=null!;
    public SourceFileSnapshot Snapshot{get;private set;}=null!;
    public string SelectedKind{get;private set;}="Warps";
    public int SelectedOrdinal{get;private set;}
    public bool Enabled{get;set;}
    public short? PaintValue{get;set;}
    public SmdDocument Document=>gesture??Draft.Document;
    public event Action? Changed;
    public Task TerrainReady{get;private set;}=Task.CompletedTask;
    public void Bind(SmdDocument document,SourceFileSnapshot snapshot)
    {generation=session.Sources.Generation;Snapshot=snapshot;Draft=new(document);gesture=null;SelectedKind=document.Warps.Count>0?"Warps":document.Regene.Count>0?"Regene":"Objects";SelectedOrdinal=0;viewport.SourceLayers=this;Refresh();viewport.DescribeSource("Server SMD snapshot · "+snapshot.Key.RelativePath+" · edits remain in source draft");}
    public void Select(string kind,int ordinal){Ensure();SelectedKind=kind;SelectedOrdinal=ordinal;Changed?.Invoke();}
    void Ensure(){if(generation!=session.Sources.Generation)throw new InvalidOperationException("Source profile changed; reopen this map.");}
    public void Edit(Action<SmdDocument> edit){Ensure();Draft.Edit(edit);Refresh();TerrainReady=viewport.RefreshServerTerrainAsync(this,SmdHeightAdapter.ToCanonical(Document));Changed?.Invoke();}
    public void SetHeight(int x,int z,float value)
    {Ensure();if(!float.IsFinite(value)||x<0||z<0||x>=Document.Heights.Size||z>=Document.Heights.Size||!Document.Sections.TryGetValue("Terrain",out var section)||!section.Complete||!section.CanWrite)throw new InvalidDataException("A complete writable terrain section and finite in-range sample are required.");Edit(d=>d.Heights.Heights[x*d.Heights.Size+z]=value);}
    public void TransformCollision(IReadOnlyList<int> triangles,MapPoint delta,IReadOnlyDictionary<int,IReadOnlyList<int>> provenance)
    {
        Edit(d=>{var original=d.CollisionVertices.Chunk(3).Select(v=>new MapPoint(v[0],v[1],v[2])).ToArray();var moved=LibreKO.Editors.Core.Client.CollisionEdits.Transform(original,triangles,delta);var flat=moved.SelectMany(v=>new[]{v.X,v.Y,v.Z}).ToArray();Array.Copy(flat,d.CollisionVertices,flat.Length);var issues=SmdCollisionIndex.Rebuild(d,provenance);if(issues.Count>0)throw new InvalidDataException(string.Join("; ",issues.Select(i=>i.Message)));});
    }
    public void Structure(string kind,string action)
    {
        Ensure();if(!Document.Complete)throw new InvalidDataException("Structural source edits require a complete SMD.");var at=SelectedKind==kind?SelectedOrdinal:0;int next=at;
        Edit(d=>{if(kind=="Warps")Change(d.Warps,()=>new(0,"New warp","",0,1,new(0,0,0),2,0));else if(kind=="Regene")Change(d.Regene,()=>new(new(0,0,0),1,1));else Change(d.Objects,()=>new(0,0,0,0,0,new(0,0,0)));});Select(kind,next);
        void Change<T>(List<T> values,Func<T> create)
        {
            at=Math.Clamp(at,0,Math.Max(0,values.Count-1));if(action=="Add"){values.Add(create());next=values.Count-1;return;}if(values.Count==0)return;
            if(action=="Duplicate"){values.Insert(at+1,values[at]);next=at+1;}else if(action=="Remove"){values.RemoveAt(at);next=Math.Max(0,Math.Min(at,values.Count-1));}else{int target=at+(action=="Up"?-1:1);if(target>=0&&target<values.Count){var value=values[at];values.RemoveAt(at);values.Insert(target,value);next=target;}}
        }
    }
    public void Undo(){Ensure();CancelGesture();Draft.Undo();Refresh();TerrainReady=viewport.RefreshServerTerrainAsync(this,SmdHeightAdapter.ToCanonical(Document));Changed?.Invoke();}
    public void Redo(){Ensure();CancelGesture();Draft.Redo();Refresh();TerrainReady=viewport.RefreshServerTerrainAsync(this,SmdHeightAdapter.ToCanonical(Document));Changed?.Invoke();}
    public void BeginGesture(){Ensure();gesture=SmdCodec.Read(SmdCodec.Write(Draft.Document));}
    public void PreviewMove(MapPoint destination)
    {
        Ensure();if(gesture==null)return;
        if(SelectedKind=="Warps")SmdSpatialEdits.MoveWarp(gesture,SelectedOrdinal,destination);
        else if(SelectedKind=="Regene")SmdSpatialEdits.MoveRegene(gesture,SelectedOrdinal,destination);
        else gesture.Objects[SelectedOrdinal]=gesture.Objects[SelectedOrdinal] with{Position=destination};
        Refresh();
    }
    public void Paint(int x,int z,short value){Ensure();if(gesture==null)BeginGesture();SmdSpatialEdits.SetEventTile(gesture!,x,z,value);Refresh();}
    public void CommitGesture()
    {
        if(gesture==null)return;Ensure();var preview=gesture;gesture=null;dragStart=null;
        Draft.Edit(d=>{d.Objects.Clear();d.Objects.AddRange(preview.Objects);d.Regene.Clear();d.Regene.AddRange(preview.Regene);d.Warps.Clear();d.Warps.AddRange(preview.Warps);preview.EventTiles.CopyTo(d.EventTiles,0);});Refresh();Changed?.Invoke();
    }
    public void CancelGesture(){gesture=null;dragStart=null;if(Draft!=null)Refresh();}
    public MapPoint SelectedPosition()=>SelectedKind switch{"Warps"=>Document.Warps[SelectedOrdinal].Position,"Regene"=>Document.Regene[SelectedOrdinal].Position,_=>Document.Objects[SelectedOrdinal].Position};
    public bool Input(InputEvent input)
    {
        if(!Enabled||generation!=session.Sources.Generation)return false;
        if(input is InputEventKey{Pressed:true,Keycode:Key.Escape}){CancelGesture();return true;}
        if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left} button)
        {
            if(!button.Pressed){CommitGesture();return true;}
            if(PaintValue==null)
            {
                var nearest=nodes.Where(x=>GodotObject.IsInstanceValid(x.Value)).Select(x=>(x.Key,Distance:viewport.SourceScreen(x.Value.Position).DistanceTo(button.Position))).OrderBy(x=>x.Distance).FirstOrDefault();
                if(nearest.Distance>20||nodes.Count==0)return true;Select(nearest.Key.Kind,nearest.Key.Ordinal);
            }
            if(viewport.SourcePointer(button.Position) is {} point){BeginGesture();dragStart=point;if(PaintValue is short value)PaintAt(point,value);}return true;
        }
        if(input is InputEventMouseMotion motion&&gesture!=null&&(motion.ButtonMask&MouseButtonMask.Left)!=0&&viewport.SourcePointer(motion.Position) is {} p)
        {
            if(PaintValue is short value)PaintAt(p,value);
            else if(dragStart is {} start)
            {
                var initial=SelectedKind switch{"Warps"=>Draft.Document.Warps[SelectedOrdinal].Position,"Regene"=>Draft.Document.Regene[SelectedOrdinal].Position,_=>Draft.Document.Objects[SelectedOrdinal].Position};
                var x=initial.X+p.X-start.X;var z=initial.Z+p.Z-start.Z;PreviewMove(new(x,SmdHeightAdapter.Sample(Document,x,z) is var h&&h!=float.MinValue?h:initial.Y,z));
            }
            return true;
        }
        return false;
    }
    void PaintAt(MapPoint p,short value){var x=(int)(p.X/Document.Heights.CellSize);var z=(int)(p.Z/Document.Heights.CellSize);if(x>=0&&z>=0&&x<Document.Heights.Size&&z<Document.Heights.Size)Paint(x,z,value);}
    public void Refresh()
    {
        if(Draft==null)return;nodes.Clear();foreach(var name in new[]{"Server regene","Server event tiles","Server warps","Server collision"})Ui.Clear(viewport.SourceRoot(name));
        var collision=new List<Vector3>();for(int face=0;face<Math.Min(Document.CollisionVertices.Length/9,16000);face++){int at=face*9;for(int i=0;i<3;i++){int a=at+i*3,b=at+(i+1)%3*3;collision.Add(new(-Document.CollisionVertices[a],Document.CollisionVertices[a+1],Document.CollisionVertices[a+2]));collision.Add(new(-Document.CollisionVertices[b],Document.CollisionVertices[b+1],Document.CollisionVertices[b+2]));}}if(collision.Count>0)viewport.SourceRoot("Server collision").AddChild(WorldLayers.Segments(collision,new("ce8d66")));
        void Marker(string kind,int ordinal,MapPoint p,string text,string layer,Color color)
        {
            var node=WorldLayers.Marker(color,.5f);node.Position=new(-p.X,p.Y,p.Z);node.AddChild(new Label3D{Text=text,Position=new(0,3,0),FontSize=24,PixelSize=.014f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled});viewport.SourceRoot(layer).AddChild(node);nodes[(kind,ordinal)]=node;
        }
        int shown=0;
        for(int i=0;i<Document.Warps.Count&&shown<2000;i++,shown++)Marker("Warps",i,Document.Warps[i].Position,$"SMD warp {i} · {Document.Warps[i].Id} · {Document.Warps[i].Name}","Server warps",new("cca45e"));
        for(int i=0;i<Document.Regene.Count&&shown<2000;i++,shown++)Marker("Regene",i,Document.Regene[i].Position,$"SMD regene {i}","Server regene",new("75b3a2"));
        for(int i=0;i<Document.Objects.Count&&shown<2000;i++,shown++)Marker("Objects",i,Document.Objects[i].Position,$"SMD object {i} · {Document.Objects[i].Index}","Server regene",new("b48fc9"));
        var points=new List<Vector3>();int blocked=0,n=Document.Heights.Size;
        for(int x=0;x<n&&blocked<4096;x++)for(int z=0;z<n&&blocked<4096;z++)if(Document.EventTiles.Length==n*n&&Document.EventTiles[x*n+z]<=0)
        {var px=x*Document.Heights.CellSize;var pz=z*Document.Heights.CellSize;var y=Document.Heights.Heights[x*n+z]+.2f;var s=Document.Heights.CellSize;points.AddRange([new(-px,y,pz),new(-px-s,y,pz),new(-px-s,y,pz),new(-px-s,y,pz+s),new(-px-s,y,pz+s),new(-px,y,pz+s),new(-px,y,pz+s),new(-px,y,pz)]);blocked++;}
        if(points.Count>0)viewport.SourceRoot("Server event tiles").AddChild(WorldLayers.Segments(points,new("c06759")));
        viewport.EnableSourceLayers();
    }
}
