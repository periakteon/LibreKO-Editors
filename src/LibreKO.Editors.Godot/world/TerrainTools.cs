using System.Globalization;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
using LibreKO.Editors.Core.Client;
using LibreKO.Editors.Core.Maps;
namespace LibreKO.Editors.Desktop;
public partial class TerrainTools:VBoxContainer
{
    readonly EditorSession session;readonly TerrainAuthoringView owner;readonly AcceptDialog palette;readonly OptionButton tool,direction;
    readonly LineEdit x,z,radius,strength,target,tile,width,height;readonly TextureRect tilePreview;GroundSurface? atlas;
    static readonly string[] modes=["raise","lower","flatten","smooth","paint-primary","paint-secondary"];
    public int Tile=>int.Parse(tile.Text,CultureInfo.InvariantCulture);public byte Direction=>(byte)direction.Selected;
    int I(LineEdit field)=>int.Parse(field.Text,CultureInfo.InvariantCulture);float F(LineEdit field)=>float.Parse(field.Text,CultureInfo.InvariantCulture);
    public TerrainStroke Stroke(int? px=null,int? pz=null)=>new(modes[tool.Selected],px??I(x),pz??I(z),I(radius),F(strength),F(target));
    public TerrainTools(EditorSession session,TerrainAuthoringView owner)
    {
        this.session=session;this.owner=owner;var compact=Ui.Add(this,new HFlowContainer());var enabled=Ui.Add(compact,new CheckButton{Text="Edit terrain"});enabled.Toggled+=v=>owner.BrushEnabled=v;
        palette=Ui.Add(session.Shell,new AcceptDialog{Title="Authored terrain tools",MinSize=new(820,690)});var body=Ui.Column(palette);body.Position=new(18,42);body.CustomMinimumSize=new(780,590);Ui.Heading(body,"TERRAIN & GROUND SURFACE",15);
        Ui.Label(body,"Brushes use their original stroke samples. Saving visual terrain does not change server ground; prepare a linked draft explicitly.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        tool=Ui.Add(body,new OptionButton());foreach(var name in new[]{"Raise height","Lower height","Flatten height","Smooth height","Paint primary tile","Paint secondary tile"})tool.AddItem(name);
        var row=Ui.Row(body);x=Ui.Field(row,"Grid X","X","0");z=Ui.Field(row,"Grid Z","Z","0");radius=Ui.Field(row,"Radius (samples)","Radius","2");strength=Ui.Field(row,"Strength / blend","Strength","1");target=Ui.Field(row,"Target height","Height","0");
        var actions=Ui.Add(body,new HFlowContainer());Ui.Button(actions,"Apply one stroke",()=>Run(()=>{owner.BeginStroke(Stroke(),Tile,Direction);owner.CommitStroke();}));Ui.Button(actions,"Set exact height",()=>Run(()=>owner.SetNumeric(I(x),I(z),F(target))));Ui.Button(actions,"Undo source",()=>Run(()=>owner.Undo()));Ui.Button(actions,"Redo source",()=>Run(()=>owner.Undo(true)));
        var surface=Ui.Row(body);tile=Ui.Field(surface,"Tile index / NoTile65535","Index","0");direction=Ui.Add(surface,new OptionButton());for(int i=0;i<8;i++)direction.AddItem("Direction "+i);tilePreview=Ui.Add(surface,new TextureRect{CustomMinimumSize=new(80,80),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered});tile.TextChanged+=_=>PreviewTile();
        var region=Ui.Row(body);width=Ui.Field(region,"Region width","Width","1");height=Ui.Field(region,"Region height","Height","1");var regionActions=Ui.Add(body,new HFlowContainer());Ui.Button(regionActions,"Import height region…",()=>Region(false));Ui.Button(regionActions,"Export height region…",()=>Region(true));
        Ui.Button(body,"Prepare linked server SMD heights…",Link);Ui.Label(body,"Client height spacing is fixed at4 units. Linked drafts compare dimensions, grid coordinates, source mapping and existing samples.",11,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        Ui.Button(compact,"Terrain tools…",()=>palette.PopupCentered());palette.Confirmed+=()=>palette.Hide();palette.Canceled+=()=>palette.Hide();
        VisibilityChanged+=()=>{if(!IsVisibleInTree())palette.Hide();};
    }
    void Run(Action action){try{action();}catch(Exception e){owner.Report(e.Message);}}
    public void SetAtlas(GroundSurface? surface){atlas=surface;PreviewTile();}
    void PreviewTile()
    {
        tilePreview.Texture=null;if(atlas==null||!int.TryParse(tile.Text,out var index)||index<0||index>=atlas.TileCount)return;
        int count=checked(atlas.TilePixels*atlas.TilePixels*4);using var image=Image.CreateFromData(atlas.TilePixels,atlas.TilePixels,false,Image.Format.Rgba8,atlas.Atlas.AsSpan(index*count,count).ToArray());tilePreview.Texture=ImageTexture.CreateFromImage(image);tilePreview.TooltipText="Authored atlas · tile "+index+" / "+atlas.TileCount;
    }
    void Link()
    {
        Run(()=>
        {
            var root=session.Sources.Profile!.Roots[SourceRoot.ServerMaps];var picker=Ui.Add(session.Shell,new FileDialog{Title="Select server SMD for linked grid review",Access=FileDialog.AccessEnum.Filesystem,FileMode=FileDialog.FileModeEnum.OpenFile,CurrentDir=root,Filters=["*.smd ; Server map"]});
            picker.FileSelected+=path=>
            {
                picker.Hide();picker.QueueFree();Run(()=>
                {
                    var key=session.Sources.Store!.Key(SourceRoot.ServerMaps,Path.GetRelativePath(root,path).Replace('\\','/'));
                    var serverDocument=session.Sources.Drafts!.Open(key);var server=SmdCodec.Read(serverDocument.Render());var before=ClientHeightmapCodec.Decode(session.Sources.Blobs!.Read(owner.HeightDocument.Snapshot.Content!));var canonical=SmdHeightAdapter.ToCanonical(server);
                    var alignment=canonical.Size!=before.Size?"Different grid dimensions":$"{canonical.Size} × {canonical.Size} server samples at {canonical.CellSize} units / client spacing {before.CellSize} · "+canonical.Heights.Where((v,i)=>v!=before.Heights[i]).Count()+" existing grid vertices differ";
                    var review=Ui.Add(session.Shell,new ConfirmationDialog{Title="Review server/client terrain mapping",DialogHideOnOk=false,OkButtonText="Prepare linked drafts",MinSize=new(720,360)});var body=Ui.Column(review);body.Position=new(18,42);body.CustomMinimumSize=new(680,250);
                    Ui.Label(body,"Server: "+key.RelativePath+"\nClient: "+owner.HeightDocument.Snapshot.Key.RelativePath+"\n"+alignment+"\nBoth source candidates receive the proposed client grid. Dimensions/spacing must match. Between-grid interpolation remains server triangles versus client bilinear sampling.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
                    var confirmed=Ui.Add(body,new CheckButton{Text="I have reviewed this map association and the grid replacement"});var error=Ui.Label(body,"",11,Ui.Accent);
                    review.Confirmed+=()=>{try{if(!confirmed.ButtonPressed)throw new InvalidOperationException("Review and confirm the explicit source association first.");owner.PrepareLink(key,true);review.Hide();review.QueueFree();}catch(Exception e){error.Text=e.Message;}};review.Canceled+=()=>review.QueueFree();review.PopupCentered();
                });
            };picker.Canceled+=()=>picker.QueueFree();palette.Hide();picker.PopupCenteredRatio(.7f);
        });
    }
    void Region(bool export)
    {
        Run(()=>
        {
            var picker=Ui.Add(session.Shell,new FileDialog{Title=export?"Export bounded height region":"Import bounded height region",Access=FileDialog.AccessEnum.Filesystem,FileMode=export?FileDialog.FileModeEnum.SaveFile:FileDialog.FileModeEnum.OpenFile,Filters=["*.lkhr ; LibreKO float height region"]});
            picker.FileSelected+=path=>{Run(()=>
            {
                if(!path.EndsWith(".lkhr",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Use the .lkhr region format.");
                if(export)
                {
                    var absolute=Path.GetFullPath(path);foreach(var root in session.Sources.Profile!.Roots.Where(p=>p.Key!=SourceRoot.BuildOutput))if(absolute.StartsWith(Path.GetFullPath(root.Value).TrimEnd('\\','/')+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Export regions to a separate output location.");
                    var values=owner.ExportRegion(I(x),I(z),I(width),I(height));using var stream=File.Create(absolute);using var w=new BinaryWriter(stream);w.Write("LKHR"u8);w.Write(I(width));w.Write(I(height));foreach(var value in values)w.Write(value);
                }
                else
                {
                    SourcePaths.AssertNoLinks(path);using var stream=File.OpenRead(path);SourceLimits.CheckedLength(stream.Length);using var r=new BinaryReader(stream);if(!r.ReadBytes(4).AsSpan().SequenceEqual("LKHR"u8))throw new InvalidDataException("Invalid region header.");int w=r.ReadInt32(),h=r.ReadInt32();if(w<=0||h<=0||w>4097||h>4097||stream.Length!=12+(long)w*h*4)throw new InvalidDataException("Invalid region dimensions/length.");var values=new float[w*h];for(int i=0;i<values.Length;i++)values[i]=r.ReadSingle();owner.ImportRegion(I(x),I(z),w,h,values);
                }
            });picker.Hide();picker.QueueFree();};picker.Canceled+=()=>picker.QueueFree();palette.Hide();picker.PopupCenteredRatio(.7f);
        });
    }
    public override void _ExitTree(){if(IsInstanceValid(palette))palette.QueueFree();}
}
