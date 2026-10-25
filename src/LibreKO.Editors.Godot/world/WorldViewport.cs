using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Maps;
namespace LibreKO.Editors.Desktop;
public partial class WorldViewport : VBoxContainer
{
    readonly EditorSession session;
    readonly bool sourceOnly;
    readonly Action<IReadOnlyList<WorldRowChange>> edit;
    readonly SubViewportContainer canvas;
    readonly SubViewport viewport;
    readonly Node3D world = new();
    readonly Camera3D camera;
    readonly WorldLayers layers;
    readonly Label status, hover;
    readonly ItemList sceneList;
    readonly AcceptDialog sceneDialog;
    readonly MenuButton layerMenu;
    readonly OptionButton animationChoice;
    AnimationPlayer? prototypeAnimation;
    readonly WorldSelection selection = new();
    readonly Dictionary<WorldKey, WorldRowSnapshot> rows = [];
    readonly Dictionary<WorldKey, Node3D> visuals = [];
    readonly Dictionary<WorldKey, WorldRowSnapshot?> prototypes = [];
    readonly Dictionary<WorldKey, WorldKey> prototypeKeys = [];
    public IReadOnlyDictionary<WorldKey, WorldKey> PrototypeKeys => prototypeKeys;
    public IReadOnlyDictionary<WorldKey, WorldRowSnapshot> Rows => rows;
    public event Action<IReadOnlyList<WorldKey>>? SelectionChanged;
    public event Action? ContextRequested;
    public event Action? ExpandRequested;
    public event Action? FullscreenRequested;
    Button? expandButton;
    public void SetExpanded(bool expanded){if(expandButton!=null)expandButton.Text=expanded?"Exit expanded map":"Expand map";}
    public int? ContextZone { get; private set; }
    public int? DisplayZone { get; private set; }
    public string? Stem { get; private set; }
    public bool Loaded { get; private set; }
    public HeightField? Heights { get; private set; }
    internal bool SnapGrid = true, SnapTerrain = true, Active;
    CancellationTokenSource? loading;
    int loadSerial;
    Vector3 focus = Vector3.Zero;
    float distance = 80, yaw = .5f, pitch = -.8f;
    bool freeCamera;
    readonly Node3D handles = new();
    WorldRowSnapshot[]? moving;
    IReadOnlyList<WorldRowChange>? pendingMove;
    Vector3 dragStart;
    float verticalDrag;
    int dragAxis = -1;
    public WorldViewport(EditorSession session, Action<IReadOnlyList<WorldRowChange>> edit,bool sourceOnly=false)
    {
        this.session = session;this.sourceOnly=sourceOnly; this.edit = edit; SizeFlagsVertical = SizeFlags.ExpandFill; SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var tools = Ui.Add(this, new HFlowContainer()); tools.AddThemeConstantOverride("h_separation", 4);
        Ui.Button(tools, "Orbit camera", OrbitView).TooltipText="Perspective camera around the focus point. Right-drag rotates, middle-drag pans, wheel zooms.";
        Ui.Button(tools, "Top view", TopView).TooltipText="Look straight down with an orthographic camera for accurate placement.";
        Ui.Button(tools, "Focus selected", FocusSelection).TooltipText="Centre and zoom onto selected spawns or objects. With no selection, frame the terrain.";
        Ui.Button(tools, "Fly camera", FreeView).TooltipText="Click the map, then use W/A/S/D to fly and Q/E to move down/up. Right-drag changes direction.";
        var grid = Ui.Add(tools, new CheckButton { Text = "Grid snap", ButtonPressed = true,TooltipText="Snap moved database objects to whole map coordinates. This does not toggle grid visibility." }); grid.Toggled += enabled => SnapGrid = enabled;
        var terrain = Ui.Add(tools, new CheckButton { Text = "Ground snap", ButtonPressed = true,TooltipText="Keep moved objects on the sampled terrain height. Spawn positions use X/Z and follow terrain." }); terrain.Toggled += enabled => SnapTerrain = enabled;
        if(!sourceOnly)
        {
            expandButton=Ui.Button(tools,"Expand map",()=>ExpandRequested?.Invoke());expandButton.TooltipText="Use the full editor workspace for the map; retain the camera and draft. Escape returns.";
            Ui.Button(tools,"Full screen",()=>FullscreenRequested?.Invoke()).TooltipText="Expand the map and toggle full screen · F11. Escape returns to the normal workspace.";
            var patrol=Ui.Add(tools,new MenuButton{Text="Patrol",CustomMinimumSize=new(60,28)});var patrolMenu=patrol.GetPopup();patrolMenu.AddItem("Edit waypoints on map",0);patrolMenu.AddItem("Edit coordinate list…",1);patrolMenu.AddItem("Reverse waypoint order",2);patrolMenu.AddItem("Clear path in draft",3);patrolMenu.IdPressed+=PatrolAction;
            Ui.Button(tools, "List", ToggleList);Ui.Button(tools,"Map asset…",()=>WorldTerrainAliasView.Show(session,DisplayZone));Ui.Button(tools,"Context",()=>ContextRequested?.Invoke());
        }
        animationChoice=Ui.Add(tools,new OptionButton{Visible=false,FitToLongestItem=false,CustomMinimumSize=new(130,28)});animationChoice.ItemSelected+=PlayPrototypeAnimation;
        layerMenu = Ui.Add(tools, new MenuButton { Text = "Layers", CustomMinimumSize = new(55, 28) });
        Ui.Button(tools,"Controls…",ShowControls).TooltipText="Camera, selection, placement and snapping help.";
        Ui.Add(this, new ActivityIndicator(session.Loading, "map"));
        canvas = Ui.Add(this, new SubViewportContainer { Name = "MapCanvas", Stretch = true, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new(220, 180), FocusMode = FocusModeEnum.All });
        viewport = new() { OwnWorld3D = true, Size = new(700, 550), RenderTargetUpdateMode = SubViewport.UpdateMode.Always }; canvas.AddChild(viewport);
        viewport.AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new("111419"), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new("d7d4cc"), AmbientLightEnergy = .8f, TonemapMode = Godot.Environment.ToneMapper.Filmic } });
        viewport.AddChild(new DirectionalLight3D { RotationDegrees = new(-45,-40,0), LightEnergy = 1.5f }); viewport.AddChild(world);
        camera = new() { Current = true, Fov = 50, Far = 20000 }; viewport.AddChild(camera); world.AddChild(handles); layers = new(world);
        LibreKO.FxLayer.Attach(viewport);
        status = Ui.Label(this, "Select a map or prototype", 11, Ui.Muted); status.AutowrapMode = TextServer.AutowrapMode.WordSmart; status.MaxLinesVisible=2;
        hover = Ui.Label(this, "Right drag: orbit · middle drag: pan · wheel: zoom · Shift: multiple selection · Esc: cancel", 10, Ui.Muted); hover.ClipText = true;
        sceneDialog=Ui.Add(session.Shell,new AcceptDialog{Name="WorldSceneList",Title="Map spawns & objects",MinSize=new(760,540)});var sceneBody=Ui.Column(sceneDialog);sceneBody.Position=new(16,40);sceneBody.CustomMinimumSize=new(720,430);
        Ui.Label(sceneBody,"Select one or more database rows to inspect and move them together. Changes stay in the draft until saved.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        sceneList = Ui.Add(sceneBody, new ItemList { SelectMode = ItemList.SelectModeEnum.Multi, CustomMinimumSize = new(710, 350), TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis });
        sceneDialog.Confirmed+=()=>sceneDialog.Hide();sceneDialog.Canceled+=()=>sceneDialog.Hide();
        sceneList.MultiSelected += (index, selected) => { var keys = rows.Keys.ToArray(); if (index >= keys.Length) return; var next = selection.Keys.ToHashSet(); if (selected) next.Add(keys[(int)index]); else next.Remove(keys[(int)index]); SetSelection(next.ToArray()); };
        canvas.GuiInput += ProcessInput; BuildLayerMenu();
    }
    public override void _Ready() => PlaceCamera();
    public void DescribeSource(string source){status.Text+="\n"+source;status.TooltipText=status.Text;}
    void BuildLayerMenu()
    {
        var popup = layerMenu.GetPopup(); var keys = layers.Roots.Keys.ToArray();
        for (var i = 0; i < keys.Length; i++) { popup.AddCheckItem(keys[i], i); popup.SetItemChecked(i, layers[keys[i]].Visible); if (keys[i].StartsWith("Server", StringComparison.Ordinal)) { popup.SetItemDisabled(i, true); popup.SetItemText(i, keys[i] + " · SMD source required"); } }
        popup.IdPressed += id => { var key = keys[(int)id]; layers[key].Visible = !layers[key].Visible; popup.SetItemChecked((int)id, layers[key].Visible); };
    }
    public void SetActive(bool active)
    { Active = active; viewport.RenderTargetUpdateMode = active ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled; world.ProcessMode = active ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled; if (!active) { sceneDialog.Hide();loading?.Cancel(); CancelManipulation(); CancelPatrol(true); } }
    public override void _ExitTree() { sourceTerrainUpdate?.Cancel();sourceTerrainUpdate?.Dispose();loading?.Cancel(); loading?.Dispose(); if(IsInstanceValid(sceneDialog))sceneDialog.QueueFree(); }
    public override void _Process(double delta)
    {
        if (!Active || !freeCamera || !canvas.HasFocus() || !session.Operations.CanMutate) return;
        var move = Vector3.Zero; if (Input.IsKeyPressed(Key.W)) move -= camera.Basis.Z; if (Input.IsKeyPressed(Key.S)) move += camera.Basis.Z; if (Input.IsKeyPressed(Key.A)) move -= camera.Basis.X; if (Input.IsKeyPressed(Key.D)) move += camera.Basis.X; if (Input.IsKeyPressed(Key.Q)) move -= Vector3.Up; if (Input.IsKeyPressed(Key.E)) move += Vector3.Up;
        if (move != Vector3.Zero) { focus += move.Normalized() * (float)delta * Math.Max(10, distance / 2); PlaceCamera(); }
    }
    public async Task LoadAsync(HeightField heights, WorldQuery query, CancellationToken ct)
        => await LoadMapAsync(heights, query, null, ct);
    public async Task LoadMapAsync(HeightField? heights, WorldQuery query, string? stem, CancellationToken ct,int? displayZone=null,bool includeDatabase=true,GroundSurface? authoredSurface=null)
    {
        using var activity = session.Loading.Begin("map", "Loading map and placements…");
        loading?.Cancel(); loading?.Dispose(); loading = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Sources.Cancellation); var token = loading.Token; var serial = ++loadSerial;
        token.ThrowIfCancellationRequested(); Loaded = false; sceneryJob++;sceneryRefreshing=false;animationChoice.Visible=false;prototypeAnimation=null;ContextZone = query.Zone; DisplayZone=displayZone??query.Zone; Stem = stem; Heights = heights;
        if(SourceLayers!=null){SourceLayers.CancelGesture();SourceLayers.Enabled=false;SourceLayers=null;}
        layers.Clear(); Ui.Clear(handles); rows.Clear(); visuals.Clear(); prototypes.Clear(); prototypeKeys.Clear(); removedInDraft.Clear(); selection.Set([]); sceneList.Clear();
        try
        {
            if (heights != null)
            {
                var terrain = new WorldTerrainPreview(); layers["Terrain"].AddChild(terrain);
                var tiles = authoredSurface==null?stem == null ? null : LibreKO.GroundTextures.LoadPng(stem):AuthoredGround(authoredSurface);
                await terrain.BuildAsync(heights, tiles, token); token.ThrowIfCancellationRequested();
                focus = new(-(heights.Size - 1) * heights.CellSize / 2, HeightSurface.Sample(heights, (heights.Size - 1) * heights.CellSize / 2, (heights.Size - 1) * heights.CellSize / 2) ?? 0, (heights.Size - 1) * heights.CellSize / 2);
                distance = Math.Max(60, (heights.Size - 1) * heights.CellSize * .6f);
            }
            else { focus = Vector3.Zero; distance = 100; AddReferenceGrid(); }
            if(includeDatabase)await LoadRowsAsync(query, token); token.ThrowIfCancellationRequested();
            if (serial != loadSerial) throw new OperationCanceledException();
            if (rows.Count > 0) { var points = rows.Values.Select(PositionOf).ToArray(); focus = points.Aggregate(Vector3.Zero, (sum, p) => sum + p) / points.Length; distance = Math.Clamp(points.Max(p => p.DistanceTo(focus)) * 2.2f, 60, 900); }
            PlaceCamera();
            if (stem != null)
            {
                await WorldSceneryPreview.BuildAsync(layers["Scenery"], stem, new(-focus.X, focus.Y, focus.Z), token);
                await WorldSceneryPreview.CollisionAsync(layers["Collision"], stem, token); await BuildWaterAndEffectsAsync(stem, token);
            }
            token.ThrowIfCancellationRequested(); if (serial != loadSerial) throw new OperationCanceledException(); sceneryFocus=focus;Loaded = true;
            status.Text = sourceOnly?(heights==null?"Source reference grid":"Source geometry · "+heights.Size+" × "+heights.Size+" height samples"):(heights == null ? "Map assets unavailable · reference grid" : stem==null?"Canonical terrain preview":"Packed client terrain · " + stem) + " · " + rows.Count + " database rows · scenery near focus (900m / 900 objects) · collision overlay up to 100,000 triangles";
        }
        catch (OperationCanceledException) { if (serial == loadSerial) status.Text = "Preview loading canceled"; throw; }
        catch (Exception e) { if (serial == loadSerial) { Loaded = true; status.Text = "Preview unavailable · " + e.Message; } }
    }
    void AddReferenceGrid()
    {
        for (var i = -10; i <= 10; i++) { layers["Terrain"].AddChild(WorldLayers.Lines([new(i * 20, 0, -200), new(i * 20, 0, 200)], new(.25f,.28f,.3f,.65f))); layers["Terrain"].AddChild(WorldLayers.Lines([new(-200, 0, i * 20), new(200, 0, i * 20)], new(.25f,.28f,.3f,.65f))); }
    }
}
