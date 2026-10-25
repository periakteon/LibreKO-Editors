using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;

public partial class SceneryTools:VBoxContainer
{
    readonly EditorSession session;readonly SourceDocument document;readonly int generation;readonly CancellationTokenSource lifetime;
    readonly TaskCompletionSource ready=new();readonly OptionButton rows;readonly VBoxContainer fields;readonly CodeEdit raw;readonly Label status;
    readonly Dictionary<string,(LineEdit Input,string Initial)> inputs=[];readonly Dictionary<int,Node3D> nodes=[];
    JsonSourceDocument json=null!;bool rawDirty,active=true,rendered;int ordinal,page;MapPoint? dragStart,origin;JsonSourceDocument? gesture;
    public Task ReadyTask=>ready.Task;public WorldViewport Viewport{get;}public int SelectedOrdinal=>ordinal;public bool HandlesEnabled{get;set;}
    public SceneryTools(EditorSession session,SourceDocument document)
    {
        this.session=session;this.document=document;generation=session.Sources.Generation;lifetime=CancellationTokenSource.CreateLinkedTokenSource(session.Sources.Cancellation);SizeFlagsVertical=SizeFlags.ExpandFill;SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var bar=Ui.Add(this,new HFlowContainer());rows=Ui.Add(bar,new OptionButton{FitToLongestItem=false,CustomMinimumSize=new(270,28)});rows.ItemSelected+=i=>{Flush();ordinal=page*100+(int)i;ShowFields();};
        Ui.Button(bar,"‹",()=>Run(()=>{page=Math.Max(0,page-1);Choices();}));Ui.Button(bar,"›",()=>Run(()=>{if((page+1)*100<json.RowCount){page++;Choices();}}));
        Ui.Button(bar,"Add",()=>Run(()=>{Flush();Read();json.Insert(ClientMapCatalog.Default(Path.GetFileName(document.Snapshot.Key.RelativePath)),json.RowCount);ordinal=json.RowCount-1;Commit();}));Ui.Button(bar,"Duplicate",()=>Run(Duplicate));Ui.Button(bar,"Remove",()=>Run(Remove));
        Ui.Button(bar,"↑",()=>Run(()=>Move(-1)));Ui.Button(bar,"↓",()=>Run(()=>Move(1)));var handles=Ui.Add(bar,new CheckButton{Text="Move handles"});handles.Toggled+=v=>HandlesEnabled=v;
        var tabs=Ui.Add(this,new TabContainer{SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill});var map=Ui.Column(tabs);map.Name="Map";map.SizeFlagsVertical=SizeFlags.ExpandFill;
        Viewport=Ui.Add(map,new WorldViewport(session,_=>{},true));Viewport.AuthoringInput=Input;
        var scroll=Ui.Add(tabs,new ScrollContainer{Name="Fields",HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,SizeFlagsHorizontal=SizeFlags.ExpandFill});fields=Ui.Column(scroll);fields.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        raw=Ui.Add(tabs,new CodeEdit{Name="Raw source",GuttersDrawLineNumbers=true,SizeFlagsVertical=SizeFlags.ExpandFill,SizeFlagsHorizontal=SizeFlags.ExpandFill});raw.Text=document.Text;raw.TextChanged+=()=>rawDirty=true;
        tabs.TabChanged+=i=>Run(()=>{if(i==2){FlushFields();if(!rawDirty)raw.Text=document.Text;}else if(rawDirty){Flush();Read();Choices();_=Render();}});
        status=Ui.Label(this,"Scenery changes do not change collision. Collision is an explicit source layer.",11,Ui.Accent);status.AutowrapMode=TextServer.AutowrapMode.WordSmart;status.MaxLinesVisible=3;
        try{Read();Choices();}catch(Exception e){status.Text=e.Message;tabs.CurrentTab=2;}
    }
    public override async void _Ready(){try{await Render();}finally{ready.TrySetResult();}}
    void Ensure(){if(generation!=session.Sources.Generation||lifetime.IsCancellationRequested)throw new InvalidOperationException("Source context changed.");}
    void Run(Action action){try{Ensure();action();}catch(Exception e){status.Text=e.Message;}}
    void Read(){json=null!;var parsed=document.Json();if(parsed.Root is not JsonArray)throw new InvalidDataException("Expected an ordered map array.");json=parsed;}
    void Choices(){rows.Clear();ordinal=Math.Clamp(ordinal,0,Math.Max(0,json.RowCount-1));page=Math.Clamp(page,0,Math.Max(0,(json.RowCount-1)/100));if(ordinal/100!=page)ordinal=page*100;for(int i=page*100;i<Math.Min(json.RowCount,(page+1)*100);i++){var row=json.RowAt(i).Values;rows.AddItem($"Row {i} · "+(row["name"]??row["fx"]??row["id"]??row["tex"]));}if(json.RowCount>0)rows.Select(ordinal-page*100);ShowFields();}
    void ShowFields()
    {
        inputs.Clear();Ui.Clear(fields);if(json.RowCount==0){Ui.Label(fields,"Add a map record to begin.",12,Ui.Muted);return;}var row=json.RowAt(ordinal).Values;
        Ui.Heading(fields,"SOURCE ROW "+ordinal,14);Ui.Label(fields,"p = map XYZ · r/q = map quaternion XYZW · s = scale. Unknown fields remain intact.",11,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        foreach(var pair in row){var text=pair.Value?.ToJsonString()??"null";var input=Ui.Field(fields,Label(pair.Key),pair.Key,text);input.TooltipText=pair.Key+" · JSON value; arrays retain component order";inputs[pair.Key]=(input,text);input.FocusExited+=()=>Run(()=>{FlushFields();_=Render();});}
        if(row.ContainsKey("name")||row.ContainsKey("fx")){Ui.Button(fields,"Choose model / effect…",PickResource);Ui.Button(fields,"Review shared source users",()=>{Flush();var value=json.RowAt(ordinal).Values[row.ContainsKey("name")?"name":"fx"]?.ToString();status.Text="This file uses ‘"+value+"’ in rows "+string.Join(", ",json.Rows.Select((r,i)=>(r,i)).Where(x=>x.r.Values[row.ContainsKey("name")?"name":"fx"]?.ToString()==value).Select(x=>x.i))+". Mapping edits affect selected rows; shared resource edits affect every reference.";});}
        if(row.ContainsKey("verts"))WaterTools.Add(fields,()=>json.RowAt(ordinal).Values,SetProperty,status);
    }
    static string Label(string key)=>key switch{"p"=>"Position · map X, Y, Z","r" or "q"=>"Rotation · quaternion X, Y, Z, W","s"=>"Scale","e"=>"Event ID","et"=>"Event type","b"=>"Nation / ownership","n"=>"NPC / control ID","verts"=>"Grid vertices · ordered XYZ","uv"=>"UV coordinates · ordered XY",_=>key};
    void PickResource()
    {
        var fx=json.RowAt(ordinal).Values.ContainsKey("fx");var field=fx?"fx":"name";var dir=fx?"assets/fx":"assets/objects";
        var d=Ui.Add(session.Shell,new AcceptDialog{Title="Selected client resources · "+dir,MinSize=new(640,550)});var body=Ui.Column(d);body.Position=new(18,42);body.CustomMinimumSize=new(600,440);var filter=Ui.Input(body,"Filter model / effect stem");var list=Ui.Add(body,new ItemList{CustomMinimumSize=new(580,340),SizeFlagsVertical=SizeFlags.ExpandFill});var stems=new List<string>();
        void Update(){list.Clear();stems.Clear();try{var root=Path.Combine(session.Sources.Profile!.Roots[SourceRoot.ClientProject],dir);if(!Directory.Exists(root))return;SourcePaths.AssertNoLinks(root);foreach(var file in Directory.EnumerateFiles(root,fx?"*.json":"*.glb",new EnumerationOptions{RecurseSubdirectories=false,AttributesToSkip=FileAttributes.ReparsePoint}).Order()){var stem=Path.GetFileNameWithoutExtension(file);if(!stem.Contains(filter.Text,StringComparison.OrdinalIgnoreCase))continue;stems.Add(stem);list.AddItem(stem);if(stems.Count>=2000)break;}}catch(Exception e){status.Text=e.Message;}}
        filter.TextChanged+=_=>Update();list.ItemActivated+=i=>{Run(()=>SetProperty(field,JsonValue.Create(stems[(int)i])));d.QueueFree();};d.Confirmed+=()=>d.QueueFree();d.Canceled+=()=>d.QueueFree();Update();d.PopupCentered();
    }
    void FlushFields()
    {
        if(json==null||json.RowCount==0)return;bool changed=false;foreach(var (field,pair) in inputs.ToArray())if(pair.Input.Text!=pair.Initial){JsonNode? value;try{value=JsonNode.Parse(pair.Input.Text);}catch{value=JsonValue.Create(pair.Input.Text);}json.Set(json.RowAt(ordinal).Token,field,value);inputs[field]=(pair.Input,pair.Input.Text);changed=true;}if(changed)document.EditBytes(json.Render());
    }
    public void Flush(){Ensure();if(gesture!=null)EndGesture();if(rawDirty){document.SetText(raw.Text);rawDirty=false;inputs.Clear();try{Read();}catch(Exception e){status.Text=e.Message;Viewport.Visible=false;}}else FlushFields();}
    void Commit(){document.EditBytes(json.Render());raw.Text=document.Text;rawDirty=false;page=ordinal/100;Choices();_=Render();}
    public void SetProperty(string field,JsonNode? value){Ensure();Flush();Read();json.Set(json.RowAt(ordinal).Token,field,value);Commit();}
    public void Transform(MapPoint p,float[] q,MapPoint s){Ensure();Flush();var typed=ClientMapDocument.OpenDraft(document.Snapshot,document.Render());typed.Transform(typed.Json.RowAt(ordinal).Token,p,q,s);document.EditBytes(typed.Render());Read();Choices();_=Render();}
    public void Duplicate(){Flush();Read();if(json.RowCount==0)return;json.Insert(json.RowAt(ordinal).Values,ordinal+1);ordinal++;Commit();}
    public void Remove(){Flush();Read();if(json.RowCount==0)return;json.Remove(json.RowAt(ordinal).Token);Commit();}
    public void Move(int direction){Flush();Read();if(json.RowCount==0)return;int target=ordinal+direction;if(target<0||target>=json.RowCount)return;json.Move(json.RowAt(ordinal).Token,target);ordinal=target;Commit();}
    async Task Render()
    {
        if(!IsInsideTree()||lifetime.IsCancellationRequested)return;try
        {
            if(json==null){Viewport.Visible=false;return;}Viewport.Visible=true;var pose=rendered?Viewport.CaptureCamera():null;var basePath=Path.GetDirectoryName(document.Snapshot.Key.RelativePath)!.Replace('\\','/');HeightField height=new(3,4,new float[9]);
            try{var h=session.Sources.Drafts!.Open(session.Sources.Store!.Key(SourceRoot.ClientProject,basePath+"/heightmap.png"));height=ClientHeightmapCodec.Decode(h.Render());}catch{}
            await Viewport.LoadMapAsync(height,new(WorldTable.NpcPositions,Zone:32767),null,lifetime.Token,includeDatabase:false);if(lifetime.IsCancellationRequested||!IsInsideTree())return;DrawSource();if(pose!=null)Viewport.RestoreCamera(pose);else Viewport.FrameTerrain();rendered=true;Viewport.SetActive(active);Viewport.DescribeSource("Authored source · "+document.Snapshot.Key.RelativePath+" · collision changes require separate selection");
        }catch(OperationCanceledException){}catch(Exception e){if(IsInsideTree())status.Text="Preview: "+e.Message;}
    }
    void DrawSource()
    {
        nodes.Clear();var source=gesture??json;if(source==null)return;var layer=Viewport.SourceRoot("Scenery");Ui.Clear(layer);int count=Math.Min(2000,source.RowCount);
        for(int i=0;i<count;i++)try
        {
            var row=source.RowAt(i).Values;var node=WorldLayers.Marker(i==ordinal?new("e1bc62"):new("6aa6ad"),.35f);
            if(row["p"] is JsonArray){var p=ClientMapCatalog.Vector(row["p"],3);node.Position=new(-p[0],p[1],p[2]);var q=ClientMapCatalog.Vector(row[row.ContainsKey("q")?"q":"r"],4);var rotation=new Quaternion(q[0],-q[1],-q[2],q[3]);if(rotation.LengthSquared()>1e-12)node.Quaternion=rotation.Normalized();if(row["s"] is JsonArray){var s=ClientMapCatalog.Vector(row["s"],3);node.Scale=new(s[0],s[1],s[2]);}else if(row["s"]!=null)node.Scale=Vector3.One*ClientMapCatalog.Number(row["s"]);}
            else if(row["verts"] is JsonArray verts){var points=verts.Select(v=>ClientMapCatalog.Vector(v,3)).Select(v=>new Vector3(-v[0],v[1],v[2])).ToArray();node.AddChild(WorldLayers.Lines(points,new("609cab")));}
            else{float minX=ClientMapCatalog.Number(row["minX"]),maxX=ClientMapCatalog.Number(row["maxX"]),minZ=ClientMapCatalog.Number(row["minZ"]),maxZ=ClientMapCatalog.Number(row["maxZ"]);node.AddChild(WorldLayers.Lines([new(-minX,.5f,minZ),new(-maxX,.5f,minZ),new(-maxX,.5f,maxZ),new(-minX,.5f,maxZ)],new("c9ac65"),true));}
            layer.AddChild(node);nodes[i]=node;node.AddChild(new Label3D{Text=i+" · "+(row["name"]??row["fx"]??row["id"]??row["tex"]),Position=new(0,2,0),FontSize=22,PixelSize=.014f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled});
        }catch{/* Invalid rows remain editable in fields/raw and are refused on Apply. */}
        status.Text=$"{source.RowCount} source rows · showing up to 2,000 handles · source geometry markers · exact model preview uses a fresh source build · collision is separate";
    }
    bool Input(InputEvent input)
    {
        if(!HandlesEnabled||!active)return false;
        try
        {
            if(input is InputEventKey{Pressed:true,Keycode:Key.Escape}){gesture=null;dragStart=null;DrawSource();return true;}
            if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left} b)
            {
                if(!b.Pressed){EndGesture();return true;}Flush();var hit=nodes.Select(x=>(x.Key,Distance:Viewport.SourceScreen(x.Value.Position).DistanceTo(b.Position))).OrderBy(x=>x.Distance).FirstOrDefault();if(nodes.Count==0||hit.Distance>24)return true;ordinal=hit.Key;page=ordinal/100;Choices();
                var row=json.RowAt(ordinal).Values;if(row["p"]==null){status.Text="Edit water grid vertices / area bounds in Fields.";return true;}if(Viewport.SourcePointer(b.Position) is {} p){dragStart=p;var xyz=ClientMapCatalog.Vector(row["p"],3);origin=new(xyz[0],xyz[1],xyz[2]);gesture=document.Json();}return true;
            }
            if(input is InputEventMouseMotion motion&&gesture!=null&&dragStart is {} start&&origin is {} o&&Viewport.SourcePointer(motion.Position) is {} to)
            {gesture.Set(gesture.RowAt(ordinal).Token,"p",ClientMapDocument.Array(new(o.X+to.X-start.X,o.Y,o.Z+to.Z-start.Z)));DrawSource();return true;}
        }catch(Exception e){status.Text=e.Message;gesture=null;dragStart=null;}return false;
    }
    void EndGesture(){if(gesture==null)return;json=gesture;gesture=null;dragStart=null;Commit();}
    public void SetActive(bool value){active=value;if(!value){gesture=null;dragStart=null;}Viewport.SetActive(value);}
    public override void _ExitTree(){lifetime.Cancel();lifetime.Dispose();Viewport.AuthoringInput=null;}
}
