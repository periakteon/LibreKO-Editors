using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public partial class SourcePreviewRuntime:Node3D
{
    SourcePreviewManifest manifest=null!;string path="";readonly Node3D world=new();readonly Camera3D camera=new(){Current=true,Far=30000};Vector3 focus=new(0,1,0);float distance=5,yaw,pitch=-.16f;
    readonly CancellationTokenSource lifetime=new();bool closing;
    public static void Start(EditorShell shell,string path){shell.Theme=Ui.Theme();shell.SetProcessUnhandledKeyInput(false);shell.AddChild(new SourcePreviewRuntime{path=path});SourcePreviewCloseChecks.Observe(shell,path);}
    public override async void _Ready()
    {
        GetTree().AutoAcceptQuit=false;GetWindow().CloseRequested+=CloseRequested;
        try
        {
            SourcePaths.AssertNoLinks(path);manifest=JsonSerializer.Deserialize<SourcePreviewManifest>(File.ReadAllBytes(path),JsonConfig.Options)??throw new InvalidDataException("Missing source preview manifest.");manifest.Guard(manifest.SourceProfileId,manifest.SourceGeneration,manifest.SourceRevision,manifest.DraftVersion);var build=manifest.Build!;var output=BuildManifest.Read(Path.Combine(build.OutputRoot,"build-manifest.json"));if(!output.Verified)throw new InvalidDataException("Source preview requires independently verified output.");foreach(var file in output.Outputs){var target=Path.Combine(build.OutputRoot,file.RelativePath);if(BuildRunner.Hash(target)!=file.Sha256)throw new InvalidDataException("Captured output changed: "+file.RelativePath);}
            if(!ProjectSettings.LoadResourcePack(Path.Combine(build.OutputRoot,"knightonline.pck"),true))throw new InvalidDataException("Cannot mount captured source output.");
            var provenance=JsonNode.Parse(Godot.FileAccess.GetFileAsString("res://editor-build-inputs.json"))!;var targetRecord=provenance["files"]!.AsArray().Single(f=>f?["relativePath"]?.GetValue<string>()==manifest.Target!.RelativePath);string displayedSourceHash=targetRecord!["sha256"]!.GetValue<string>();if(displayedSourceHash!=manifest.ExpectedSourceHash)throw new InvalidDataException("Packed cache shown as authored preview.");
            GetWindow().Title="LibreKO · Captured source · "+manifest.Target!.RelativePath;GetWindow().Size=new(1100,760);AddChild(world);AddChild(camera);AddChild(new WorldEnvironment{Environment=new Godot.Environment{BackgroundMode=Godot.Environment.BGMode.Color,BackgroundColor=new("111419"),AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=Colors.White,AmbientLightEnergy=.8f}});AddChild(new DirectionalLight3D{RotationDegrees=new(-45,-35,0),LightEnergy=1.5f});LibreKO.FxLayer.Attach(GetViewport());
            var hud=new CanvasLayer();AddChild(hud);var panel=new PanelContainer{Position=new(16,16),CustomMinimumSize=new(1000,0)};panel.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new("1b1b1ee8"),BorderColor=new("7b653d"),BorderWidthBottom=1,ContentMarginLeft=12,ContentMarginTop=8,ContentMarginBottom=8});hud.AddChild(panel);var text=new Label{Text="CAPTURED SOURCE · "+manifest.Target.RelativePath+"\nSHA256 "+displayedSourceHash+"\nDrag right to orbit · middle to pan · wheel to zoom · F to focus · source edits close this captured preview",AutowrapMode=TextServer.AutowrapMode.WordSmart};text.AddThemeColorOverride("font_color",new("d7bd80"));var hudBody=new VBoxContainer();panel.AddChild(hudBody);hudBody.AddChild(text);var closeButton=new Button{Text="Close captured preview · Esc",SizeFlagsHorizontal=Control.SizeFlags.ShrinkBegin};hudBody.AddChild(closeButton);closeButton.Pressed+=CloseRequested;
            if(manifest.Kind=="map")await Map();
            else if(manifest.Kind=="texture"){var texture=ResourceLoader.Load<Texture2D>("res://"+manifest.Target.RelativePath);if(texture==null)throw new InvalidDataException("Source texture is missing.");var rect=new TextureRect{Texture=texture,Position=new(120,150),Size=new(800,530),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered};hud.AddChild(rect);}
            else
            {
                Node3D body;if(manifest.AssembleNpc){var proto=manifest.Prototype??new JsonObject{["Id"]=0,["ModelId"]=100,["Size"]=100,["IsMonster"]=true};body=NpcPreviewBuilder.BuildFromStem(Path.GetFileNameWithoutExtension(manifest.ModelResourcePath),proto)??throw new InvalidDataException("Selected source NPC model was not exported.");}
                else{body=(ResourceLoader.Load<PackedScene>("res://"+manifest.ModelResourcePath)??throw new InvalidDataException("Selected source model was not exported: "+manifest.ModelResourcePath)).Instantiate<Node3D>();if(NpcPreviewBuilder.Find<AnimationPlayer>(body) is {} animation&&animation.GetAnimationList().FirstOrDefault() is string clip){animation.Play(clip);animation.Advance(0);}}
                world.AddChild(body);FrameModel(body);
            }
            Place();for(int i=0;i<8;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);var image=GetViewport().GetTexture().GetImage();image.SavePng(path+".png");
            var meshes=world.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>().Where(m=>m.Mesh!=null).ToArray();var vertices=meshes.Sum(m=>Enumerable.Range(0,m.Mesh.GetSurfaceCount()).Sum(i=>m.Mesh.SurfaceGetArrays(i)[(int)Mesh.ArrayType.Vertex].AsVector3Array().Length));var bones=world.FindChildren("*","Skeleton3D",true,false).OfType<Skeleton3D>().Sum(s=>s.GetBoneCount());var effects=world.FindChildren("*","",true,false).Count(n=>n is LibreKO.FxInstance);var attachments=world.FindChildren("*","BoneAttachment3D",true,false).Count;
            BuildPaths.Guard(build);File.WriteAllBytes(path+".result.json",JsonSerializer.SerializeToUtf8Bytes(new{ready=true,displayedSourceHash,modelResourcePath=manifest.Kind=="model"?manifest.ModelResourcePath:null,vertices,bones,effects,attachments,sourceProfileId=manifest.SourceProfileId,manifest.SourceGeneration},JsonConfig.Options));GD.Print("SOURCE PREVIEW READY "+displayedSourceHash);
            if(manifest.AutoCloseFrames>0){for(int i=0;i<manifest.AutoCloseFrames;i++)await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await RequestCloseAsync();}
        }catch(OperationCanceledException) when(closing){}
        catch(Exception e){if(!closing){GD.PrintErr(e);GetTree().Quit(1);}}
    }
    void CloseRequested()=>_ = RequestCloseAsync();
    public async Task RequestCloseAsync()
    {
        if(closing)return;closing=true;lifetime.Cancel();GetViewport().GuiDisableInput=true;
        GD.Print("SOURCE PREVIEW CLOSING");await LibreKO.Shutdown.Begin(this,200);
    }
    public override void _Input(InputEvent input)
    {if(input is InputEventKey{Pressed:true,Echo:false,Keycode:Key.Escape}){GetViewport().SetInputAsHandled();CloseRequested();}}
    void FrameModel(Node3D body)
    {
        Aabb? bounds=null;foreach(var mesh in body.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>())if(mesh.Mesh!=null&&!PreviewEffects.IsEffect(mesh)){var box=mesh.GlobalTransform*mesh.GetAabb();bounds=bounds==null?box:bounds.Value.Merge(box);}if(bounds is {} b){focus=b.GetCenter();distance=Math.Max(.5f,b.Size.Length()*1.7f);}
    }
    async Task Map()
    {
        string stem=manifest.Stem??throw new InvalidDataException("Select a terrain stem.");var raw=LibreKO.Heightmap.LoadPng(stem)??throw new InvalidDataException("Selected source terrain has no heightmap.");var heights=new HeightField(raw.MapSize,raw.CellSize,raw.Heights);var terrain=new WorldTerrainPreview();world.AddChild(terrain);LibreKO.GroundTextures? tiles=null;if(Godot.FileAccess.FileExists("res://assets/terrain/"+stem+"/splat.png")&&Godot.FileAccess.FileExists("res://assets/terrain/"+stem+"/tiles.png")){tiles=LibreKO.GroundTextures.LoadPng(stem);if(tiles?.TileCount>512)tiles=null;}await terrain.BuildAsync(heights,tiles,lifetime.Token);lifetime.Token.ThrowIfCancellationRequested();
        float extent=(raw.MapSize-1)*raw.CellSize;focus=new(-extent/2,raw.Heights.Average(),extent/2);distance=Math.Max(8,extent*1.5f);var scenery=new Node3D();world.AddChild(scenery);if(Godot.FileAccess.FileExists("res://assets/terrain/"+stem+"/placements.json"))await WorldSceneryPreview.BuildAsync(scenery,stem,new(extent/2,focus.Y,extent/2),default);
        if(Godot.FileAccess.FileExists("res://assets/terrain/"+stem+"/water.json")&&LibreKO.ZoneWater.LoadJson(stem) is {} water)foreach(var patch in water.Patches){using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);for(int z=0;z<patch.Rows-1;z++)for(int x=0;x<patch.Cols-1;x++)foreach(int i in new[]{z*patch.Cols+x,(z+1)*patch.Cols+x,z*patch.Cols+x+1,z*patch.Cols+x+1,(z+1)*patch.Cols+x,(z+1)*patch.Cols+x+1}){var p=patch.Verts[i];surface.SetNormal(Vector3.Up);surface.AddVertex(new(-p.X,p.Y,p.Z));}world.AddChild(new MeshInstance3D{Mesh=surface.Commit(),MaterialOverride=new StandardMaterial3D{AlbedoColor=new(.18f,.4f,.5f,.65f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha,CullMode=BaseMaterial3D.CullModeEnum.Disabled}});}
        if(Godot.FileAccess.FileExists("res://assets/terrain/"+stem+"/fx_placements.json")&&LibreKO.FxPlacements.LoadJson(stem) is {} effects)foreach(var fx in effects.Items.Take(120)){var anchor=new Node3D{Position=new(-fx.MapPos.X,fx.MapPos.Y,fx.MapPos.Z),Quaternion=new Quaternion(fx.Rot.X,-fx.Rot.Y,-fx.Rot.Z,fx.Rot.W).Normalized(),Scale=Vector3.One*fx.Scale};world.AddChild(anchor);LibreKO.Fx.Spawn(fx.Fx,anchor,Vector3.Zero);}
        if(Godot.FileAccess.FileExists("res://assets/terrain/"+stem+"/collision.bin")){var collision=new Node3D();world.AddChild(collision);await WorldSceneryPreview.CollisionAsync(collision,stem,default);}
    }
    void Place(){camera.Position=focus+new Vector3(Mathf.Sin(yaw)*Mathf.Cos(pitch),-Mathf.Sin(pitch),Mathf.Cos(yaw)*Mathf.Cos(pitch))*distance;camera.LookAt(focus);}
    public override void _UnhandledInput(InputEvent input)
    {
        if(input is InputEventMouseMotion m){if((m.ButtonMask&MouseButtonMask.Right)!=0){yaw-=m.Relative.X*.006f;pitch=Math.Clamp(pitch+m.Relative.Y*.006f,-1.45f,1.45f);Place();}if((m.ButtonMask&MouseButtonMask.Middle)!=0){focus+=(-camera.GlobalBasis.X*m.Relative.X+camera.GlobalBasis.Y*m.Relative.Y)*distance*.0018f;Place();}}
        if(input is InputEventMouseButton{Pressed:true} b){if(b.ButtonIndex==MouseButton.WheelUp)distance*=.9f;else if(b.ButtonIndex==MouseButton.WheelDown)distance*=1.1f;Place();}if(input is InputEventKey{Pressed:true,Keycode:Key.F})Place();
    }
    public override void _ExitTree(){lifetime.Cancel();lifetime.Dispose();LibreKO.Fx.BeginShutdown(this);}
}
