using System.Globalization;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public partial class CollisionTools:VBoxContainer
{
    readonly EditorSession session;readonly SourceDocument document;readonly int generation;readonly CancellationTokenSource lifetime;readonly Label status;readonly LineEdit selection,translation,rotation,scale,pivot;readonly CodeEdit verticesText;readonly TaskCompletionSource ready=new();IReadOnlyList<MapPoint> vertices=[];bool textDirty;
    public WorldViewport Viewport{get;}public Task ReadyTask=>ready.Task;
    public CollisionTools(EditorSession session,SourceDocument document)
    {
        this.session=session;this.document=document;generation=session.Sources.Generation;lifetime=CancellationTokenSource.CreateLinkedTokenSource(session.Sources.Cancellation);SizeFlagsVertical=SizeFlags.ExpandFill;SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var bar=Ui.Add(this,new HFlowContainer());Ui.Button(bar,"Import / replace KOCL…",()=>Pick(false));Ui.Button(bar,"Append verified KOCL…",()=>Pick(true));Ui.Button(bar,"Remove selected triangles",()=>Run(Remove));Ui.Button(bar,"Prepare empty collision",()=>Run(()=>Replace([])));
        Ui.Button(bar,"Link server collision…",()=>ReportLink());
        Ui.Button(bar,"Affected bounds & shared zones…",()=>_ = ReportUsers());
        var tabs=Ui.Add(this,new TabContainer{SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill});var map=Ui.Column(tabs);map.Name="Collision map";map.SizeFlagsVertical=SizeFlags.ExpandFill;Viewport=Ui.Add(map,new WorldViewport(session,_=>{},true));
        var panel=Ui.Column(tabs);panel.Name="Triangle tools";Ui.Label(panel,"Explicit zero-based triangle indices, comma-separated (or inclusive ranges such as 0-12). All transforms use map coordinates. Pivot defaults to the origin.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        selection=Ui.Field(panel,"Triangle selection","0,2,10-20");translation=Ui.Field(panel,"Translate XYZ","0 0 0","0 0 0");rotation=Ui.Field(panel,"Rotate quaternion XYZW","0 0 0 1","0 0 0 1");scale=Ui.Field(panel,"Scale XYZ","1 1 1","1 1 1");pivot=Ui.Field(panel,"Pivot XYZ","0 0 0","0 0 0");
        Ui.Button(panel,"Transform selected triangles",()=>Run(()=>Transform(Indices(),Point(translation.Text),Numbers(rotation.Text,4),Point(scale.Text),Point(pivot.Text))));
        verticesText=Ui.Add(tabs,new CodeEdit{Name="Vertex coordinates",GuttersDrawLineNumbers=true,SizeFlagsHorizontal=SizeFlags.ExpandFill,SizeFlagsVertical=SizeFlags.ExpandFill});verticesText.TextChanged+=()=>textDirty=true;
        status=Ui.Label(this,"",11,Ui.Accent);status.AutowrapMode=TextServer.AutowrapMode.WordSmart;status.MaxLinesVisible=3;Read();
    }
    public override async void _Ready(){try{await Render();}finally{ready.TrySetResult();}}
    void Ensure(){if(generation!=session.Sources.Generation||lifetime.IsCancellationRequested)throw new InvalidOperationException("Collision source context changed.");}
    void Run(Action action){try{Ensure();action();}catch(Exception e){status.Text=e.Message;}}
    void ReportLink()=>status.Text="Server collision requires original shape→face provenance and a verified SMD index rebuild. A client KOCL triangle soup does not contain that provenance. Import/edit the server SMD independently; no inferred link is applied.";
    async Task ReportUsers()
    {
        try
        {
            Ensure();Flush();var chosen=string.IsNullOrWhiteSpace(selection.Text)?vertices:Indices().SelectMany(i=>vertices.Skip(i*3).Take(3)).ToArray();
            string bounds=chosen.Count==0?"Empty collision":FormattableString.Invariant($"XYZ bounds: ({chosen.Min(v=>v.X):R}, {chosen.Min(v=>v.Y):R}, {chosen.Min(v=>v.Z):R}) to ({chosen.Max(v=>v.X):R}, {chosen.Max(v=>v.Y):R}, {chosen.Max(v=>v.Z):R}) · {chosen.Count/3:N0} selected triangles");
            string stem=document.Snapshot.Key.RelativePath.Split('/')[2];var messages=new List<string>{bounds,"Shared terrain stem: "+stem};
            var known=LibreKO.ZoneCatalog.All.Where(z=>z.Stem==stem).Select(z=>z.Id).Concat(session.Sources.Profile!.TerrainAliases?.Where(x=>x.Value==stem).Select(x=>x.Key)??[]).Distinct();messages.Add("Client/profile zone IDs: "+string.Join(", ",known));
            if(session.Connected)
            {
                var found=new List<string>();for(int p=0;p<200;p++){var rows=await session.Client.SendAsync<Page<WorldRowSnapshot>>("world.list",new WorldQuery(WorldTable.ZoneInfos,Page:p,Size:100),lifetime.Token);Ensure();foreach(var row in rows.Rows){string smd=row.Values["SmdName"]?.ToString()??"";var alias=session.Sources.Profile.TerrainAliases?.GetValueOrDefault(row.Key.Id);if(alias==stem||Path.GetFileNameWithoutExtension(smd).Equals(stem,StringComparison.OrdinalIgnoreCase))found.Add(row.Key.Id+" · "+row.Values["Name"]+" · "+smd);}if((p+1)*100>=rows.Total)break;}
                messages.Add("Connected database zones (scan bound20,000):\n"+string.Join("\n",found));
            }
            else messages.Add("Database disconnected; connect to inspect database zone associations.");
            Ensure();var d=Ui.Add(session.Shell,new AcceptDialog{Title="Collision impact review",MinSize=new(760,450)});var text=Ui.Add(d,new TextEdit{Editable=false,Text=string.Join("\n\n",messages)+"\n\nThis source is shared by these mappings. Client KOCL changes leave server SMD collision unchanged; deploy and reload explicitly.",CustomMinimumSize=new(720,350)});d.Confirmed+=()=>d.QueueFree();d.Canceled+=()=>d.QueueFree();d.PopupCentered();
        }catch(Exception e){if(IsInsideTree())status.Text=e.Message;}
    }
    void Read(){vertices=CollisionCodec.Read(document.Render());verticesText.Editable=vertices.Count<=30000;verticesText.Text=verticesText.Editable?string.Join('\n',vertices.Select(v=>FormattableString.Invariant($"{v.X:R} {v.Y:R} {v.Z:R}"))):"Large collision: use bounded triangle transforms or import a verified KOCL (coordinate text limit 30,000 vertices).";textDirty=false;status.Text=$"{vertices.Count/3:N0} collision triangles · original map-space orientation · no automatic scenery collision";}
    public void Flush(){Ensure();if(!textDirty)return;try{var values=verticesText.Text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(Point).ToArray();var bytes=CollisionCodec.Write(values);document.EditBytes(bytes);vertices=values;textDirty=false;}catch(Exception e){throw new InvalidDataException("Collision coordinate edits are invalid: "+e.Message,e);}}
    public void Replace(IReadOnlyList<MapPoint> next){Ensure();document.EditBytes(CollisionCodec.Write(next));Read();_=Render();}
    public void Transform(IReadOnlyList<int> indices,MapPoint delta,float[]? q=null,MapPoint? s=null,MapPoint? pivotPoint=null){Flush();Replace(CollisionEdits.Transform(vertices,indices,delta,q,s,pivotPoint));}
    void Remove(){Flush();var remove=Indices().ToHashSet();Replace(vertices.Where((_,i)=>!remove.Contains(i/3)).ToArray());}
    int[] Indices(){var result=new HashSet<int>();foreach(var token in selection.Text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)){var parts=token.Split('-');int first=int.Parse(parts[0],CultureInfo.InvariantCulture),last=parts.Length==1?first:int.Parse(parts[1],CultureInfo.InvariantCulture);if(parts.Length>2||first<0||last<first||last>=vertices.Count/3)throw new InvalidDataException("Triangle selection is outside this source.");for(int i=first;i<=last;i++)result.Add(i);}if(result.Count==0)throw new InvalidDataException("Select at least one triangle.");return result.Order().ToArray();}
    static float[] Numbers(string text,int count){var values=text.Split([' ',',','\t'],StringSplitOptions.RemoveEmptyEntries).Select(v=>float.Parse(v,CultureInfo.InvariantCulture)).ToArray();if(values.Length!=count||values.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Enter "+count+" finite components.");return values;}
    static MapPoint Point(string text){var v=Numbers(text,3);return new(v[0],v[1],v[2]);}
    void Pick(bool append)
    {
        var picker=Ui.Add(session.Shell,new FileDialog{Access=FileDialog.AccessEnum.Filesystem,FileMode=FileDialog.FileModeEnum.OpenFile,Title="Import explicit collision triangle soup",Filters=["*.bin ; KOCL triangle soup"]});picker.FileSelected+=path=>{Run(()=>{Flush();SourcePaths.AssertNoLinks(path);using var input=File.OpenRead(path);var bytes=new byte[SourceLimits.CheckedLength(input.Length)];input.ReadExactly(bytes);var next=CollisionCodec.Read(bytes);Replace(append?vertices.Concat(next).ToArray():next);});picker.QueueFree();};picker.Canceled+=()=>picker.QueueFree();picker.PopupCenteredRatio(.7f);
    }
    async Task Render()
    {
        if(!IsInsideTree()||lifetime.IsCancellationRequested)return;try
        {
            var pose=Viewport.Heights==null?null:Viewport.CaptureCamera();HeightField height=new(3,4,new float[9]);try{var path=Path.GetDirectoryName(document.Snapshot.Key.RelativePath)!.Replace('\\','/')+"/heightmap.png";var h=session.Sources.Drafts!.Open(session.Sources.Store!.Key(SourceRoot.ClientProject,path));height=ClientHeightmapCodec.Decode(h.Render());}catch{}
            await Viewport.LoadMapAsync(height,new(WorldTable.NpcPositions,Zone:32767),null,lifetime.Token,includeDatabase:false);if(lifetime.IsCancellationRequested||!IsInsideTree())return;var root=Viewport.SourceRoot("Collision");root.Visible=true;var points=new List<Vector3>();foreach(var tri in vertices.Chunk(3).Take(16000)){for(int i=0;i<3;i++){var a=tri[i];var b=tri[(i+1)%3];points.Add(new(-a.X,a.Y,a.Z));points.Add(new(-b.X,b.Y,b.Z));}}if(points.Count>0)root.AddChild(WorldLayers.Segments(points,new("ce8d66")));if(pose==null)Viewport.FrameTerrain();else Viewport.RestoreCamera(pose);Viewport.DescribeSource("Explicit authored KOCL · "+vertices.Count/3+" triangles · wireframe budget16,000 triangles");
        }catch(OperationCanceledException){}catch(Exception e){if(IsInsideTree())status.Text=e.Message;}
    }
    public void SetActive(bool value)=>Viewport.SetActive(value);
    public override void _ExitTree(){lifetime.Cancel();lifetime.Dispose();}
}
