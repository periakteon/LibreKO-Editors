using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Maps;
using LibreKO.Editors.Core.References;
namespace LibreKO.Editors.Desktop;
public partial class WorldViewport
{
    internal Vector3 PositionOf(WorldRowSnapshot row)
    {
        var v = row.Values; var x = float.Parse(v[row.Key.Table == WorldTable.NpcPositions ? "LeftX" : "X"]!.ToString(),System.Globalization.CultureInfo.InvariantCulture);
        var z = float.Parse(v[row.Key.Table == WorldTable.NpcPositions ? "TopZ" : "Z"]!.ToString(),System.Globalization.CultureInfo.InvariantCulture);
        var y = row.Key.Table == WorldTable.NpcPositions ? Heights == null ? 0 : HeightSurface.Sample(Heights, x, z) ?? 0 : float.Parse(v["Y"]?.ToString() ?? "0", System.Globalization.CultureInfo.InvariantCulture);
        return new(-x, y, z);
    }
    async Task LoadRowsAsync(WorldQuery query, CancellationToken ct)
    {
        var source = new List<WorldRowSnapshot>();
        if (session.Connected)
        {
            for (var page = 0; ; page++)
            {
                ct.ThrowIfCancellationRequested(); var batch = await session.Client.SendAsync<Page<WorldRowSnapshot>>("world.list", query with { Page = page, Size = 100 }); ct.ThrowIfCancellationRequested(); source.AddRange(batch.Rows);
                if (source.Count >= batch.Total || batch.Rows.Count == 0) break;
                if (source.Count >= 10000) throw new InvalidOperationException("Narrow the viewport query: more than 10000 rows match. All database records remain available in the paged library.");
            }
        }
        if (query.Table == WorldTable.NpcPositions && query.Zone is int zone && session.Connected)
        {
            for(var page=0;;page++)
            {
                ct.ThrowIfCancellationRequested();var objects = await session.Client.SendAsync<Page<WorldRowSnapshot>>("world.list", new WorldQuery(WorldTable.MapObjects, Page:page,Size: 100, Zone: zone));ct.ThrowIfCancellationRequested();source.AddRange(objects.Rows);
                if((page+1)*100>=objects.Total||objects.Rows.Count==0)break;if(source.Count>=10000)throw new InvalidOperationException("Narrow the viewport query: more than 10000 rows match.");
            }
        }
        var keys=source.Where(r=>r.Key.Table==WorldTable.NpcPositions).Select(r=>{var reference=ReferenceRules.ForSpawn(FieldValueParser.ReadInt32(r.Values["NpcId"]),(byte)FieldValueParser.ReadInt32(r.Values["ActType"]));return new WorldKey(WorldTable.Npcs,reference.Id,IsMonster:reference.IsMonster);}).Distinct().ToArray();
        foreach(var key in keys)prototypes[key]=null;
        foreach(var batch in keys.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();try {var loaded=await session.Client.SendAsync<WorldRowSnapshot[]>("world.get-batch",batch);ct.ThrowIfCancellationRequested();foreach(var prototype in loaded)prototypes[prototype.Key]=prototype;}
            catch(OperationCanceledException){throw;}catch{/* Typed missing/unsupported prototypes retain visible markers. */}
        }
        var built = 0;
        foreach (var row in source)
        {
            ct.ThrowIfCancellationRequested(); if (row.Key.Table is not (WorldTable.NpcPositions or WorldTable.MapObjects or WorldTable.Warps)) continue;
            rows[row.Key] = row;
            Node3D? model = null; bool monster = false; var label = WorldWorkspace.RowLabel(row);
            if (row.Key.Table == WorldTable.NpcPositions)
            {
                var reference = ReferenceRules.ForSpawn(FieldValueParser.ReadInt32(row.Values["NpcId"]), (byte)FieldValueParser.ReadInt32(row.Values["ActType"]));
                var prototypeKey = new WorldKey(WorldTable.Npcs, reference.Id, IsMonster: reference.IsMonster); prototypeKeys[row.Key] = prototypeKey; monster = reference.IsMonster == true;
                if (!prototypes.TryGetValue(prototypeKey, out var prototype))
                {
                    try { prototype = await session.Client.SendAsync<WorldRowSnapshot?>("world.get", prototypeKey); }
                    catch { prototype = null; }
                    ct.ThrowIfCancellationRequested(); prototypes[prototypeKey] = prototype;
                }
                if (prototype?.Values != null)
                {
                    label = row.Key.Id + " · " + prototype.Values["Name"] + " · " + (monster ? "Monster" : "NPC") + " · prototype " + prototypeKey.Id + " · count " + row.Values["NumNPC"];
                    if (built < 120) try { model = NpcPreviewBuilder.Build(prototype.Values); } catch { /* A missing visual never blocks a correctly typed database marker. */ }
                }
                else label += " · Missing prototype " + prototypeKey.Id;
            }
            var node = new Node3D { Name = "WorldRow_" + row.Key.Table + "_" + row.Key.Id, Position = PositionOf(row) }; visuals[row.Key] = node;
            node.AddChild(model ?? WorldLayers.Marker(monster ? new("b96b5c") : new("bfa36a"))); node.SetMeta("world_label", label); node.SetMeta("world_id", row.Key.Id);
            layers[row.Key.Table == WorldTable.NpcPositions ? monster ? "Monsters" : "NPCs" : "Map objects"].AddChild(node);
            var text = new Label3D { Text = label, Position = new(0, 4, 0), FontSize = 28, PixelSize = .012f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Modulate = monster ? new("dcbbb0") : new("e0c789"), NoDepthTest = false, VisibilityRangeEnd = 100 }; node.AddChild(text);
            sceneList.AddItem(label); if (row.Key.Table == WorldTable.NpcPositions) AddSpawnOverlays(row);
            if (++built % 10 == 0) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); ct.ThrowIfCancellationRequested(); }
        }
        if (query.Zone is int selectedZone && session.Connected) await AddStartsAsync(selectedZone, ct);
    }
    void AddSpawnOverlays(WorldRowSnapshot row)
    {
        foreach(var (layer,name) in new[]{("Spawn areas","Area_"),("Patrol routes","Path_")})if(layers[layer].GetNodeOrNull<Node3D>(name+row.Key.Id) is {} old){layers[layer].RemoveChild(old);old.QueueFree();}
        var x = FieldValueParser.ReadInt32(row.Values["LeftX"]); var z = FieldValueParser.ReadInt32(row.Values["TopZ"]); var range = (short)FieldValueParser.ReadInt32(row.Values["SpawnRange"]);
        var bounds = SpawnGeometry.Bounds(x, z, range);
        Vector3 Point(float px, float pz) => new(-px, (Heights == null ? 0 : HeightSurface.Sample(Heights, px, pz) ?? 0) + .15f, pz);
        if (range > 0){var area=WorldLayers.Lines([Point(bounds.Min.X,bounds.Min.Z),Point(bounds.Max.X,bounds.Min.Z),Point(bounds.Max.X,bounds.Max.Z),Point(bounds.Min.X,bounds.Max.Z)],new(.8f,.65f,.3f,.7f),true);area.Name="Area_"+row.Key.Id;layers["Spawn areas"].AddChild(area);}
        try
        {
            var path = PatrolPath.Decode((byte)FieldValueParser.ReadInt32(row.Values["DotCnt"]), row.Values["Path"]?.ToString());
            if (path.Count > 1){var line=WorldLayers.Lines(path.Select(p => Point(p.X,p.Z)).ToArray(),new(.35f,.75f,.85f,.8f));line.Name="Path_"+row.Key.Id;layers["Patrol routes"].AddChild(line);}
        }
        catch { /* Legacy malformed paths are preserved and repaired only through the explicit patrol editor. */ }
    }
    async Task AddStartsAsync(int zone, CancellationToken ct)
    {
        try
        {
            var context = await session.Client.SendAsync<WorldMapContext>("world.map-context", new { zone }); ct.ThrowIfCancellationRequested(); if (context.StartSourceZone == null) return;
            var start = await session.Client.SendAsync<WorldRowSnapshot>("world.get", new WorldKey(WorldTable.StartPositions, context.StartSourceZone.Value)); ct.ThrowIfCancellationRequested();
            foreach (var nation in new[] { "Karus", "Elmorad" })
            {
                var x = FieldValueParser.ReadInt32(start.Values[nation + "X"]); var z = FieldValueParser.ReadInt32(start.Values[nation + "Z"]);
                var node = WorldLayers.Marker(nation == "Karus" ? new("c95b4a") : new("6a8ccc"), 1.3f); node.Position = new(-x, Heights == null ? 0 : HeightSurface.Sample(Heights,x,z) ?? 0, z); layers["Start positions"].AddChild(node);
                node.AddChild(new Label3D { Text = nation + " start · source zone " + context.StartSourceZone, Position = new(0,6,0), Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 32 });
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { /* Schema-specific starts are an independent context layer. */ }
    }
    public void Sync(IReadOnlyList<WorldRowSnapshot> snapshots)
    {
        foreach (var row in snapshots) if (rows.TryGetValue(row.Key,out var before))
        {
            rows[row.Key] = row; if (visuals.TryGetValue(row.Key,out var node) && IsInstanceValid(node)) node.Position = PositionOf(row);
            if(row.Key.Table==WorldTable.NpcPositions&&!JsonNode.DeepEquals(before.Values,row.Values))AddSpawnOverlays(row);
            if(row.Key.Table==WorldTable.NpcPositions&&(!JsonNode.DeepEquals(before.Values["NpcId"],row.Values["NpcId"])||!JsonNode.DeepEquals(before.Values["ActType"],row.Values["ActType"])))_=RefreshPrototypeVisualAsync(row.Key);
        }
        DrawHandles();
    }
    public void ShowPrototype(JsonObject prototype)
    {
        loading?.Cancel(); ++loadSerial; ContextZone = DisplayZone = null; Stem=null;Loaded = true; Heights = null; layers.Clear(); rows.Clear(); visuals.Clear(); prototypeKeys.Clear(); sceneList.Clear(); Ui.Clear(handles);
        Node3D? model = null; try { model = NpcPreviewBuilder.Build(prototype); } catch (Exception e) { status.Text = "Visual unavailable · " + e.Message; }
        layers["NPCs"].AddChild(model ?? WorldLayers.Marker(new("bfa36a"))); focus = new(0,1,0); distance = 5;
        if(model!=null)
        {
            Aabb? bounds=null;foreach(var mesh in model.FindChildren("*","MeshInstance3D",true,false).OfType<MeshInstance3D>())
                if(mesh.Mesh!=null&&!PreviewEffects.IsEffect(mesh)){var box=mesh.GlobalTransform*mesh.GetAabb();bounds=bounds==null?box:bounds.Value.Merge(box);}
            if(bounds is {} combined){focus=combined.GetCenter();distance=Math.Max(1.5f,combined.Size.Length()*1.45f);}
        }
        PlaceCamera();
        prototypeAnimation=model is NpcPreviewRoot preview?preview.PrimaryAnimation:model==null?null:NpcPreviewBuilder.Find<AnimationPlayer>(model);animationChoice.Clear();
        if(prototypeAnimation!=null)foreach(var clip in prototypeAnimation.GetAnimationList())animationChoice.AddItem(clip);
        animationChoice.Visible=animationChoice.ItemCount>0;animationChoice.TooltipText="Available model animations · preview only";
        status.Text = model == null ? "Missing model · database definition remains editable" : "Packed client model · size, equipment, attachments and effects";
    }
    void PlayPrototypeAnimation(long index)
    {if(prototypeAnimation!=null&&IsInstanceValid(prototypeAnimation)){prototypeAnimation.Play(animationChoice.GetItemText((int)index));prototypeAnimation.Advance(0);}}
    async Task BuildWaterAndEffectsAsync(string stem, CancellationToken ct)
    {
        if (LibreKO.ZoneWater.LoadJson(stem) is { } water)
            foreach (var patch in water.Patches)
            {
                ct.ThrowIfCancellationRequested(); using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
                for (var z = 0; z < patch.Rows - 1; z++) for (var x = 0; x < patch.Cols - 1; x++)
                    foreach (var i in new[] { z*patch.Cols+x,(z+1)*patch.Cols+x,z*patch.Cols+x+1,z*patch.Cols+x+1,(z+1)*patch.Cols+x,(z+1)*patch.Cols+x+1 })
                    { var p = patch.Verts[i]; surface.SetNormal(Vector3.Up); surface.AddVertex(new(-p.X,p.Y,p.Z)); }
                layers["Water"].AddChild(new MeshInstance3D { Mesh = surface.Commit(), MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.18f,.4f,.5f,.65f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = .2f } });
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        if (LibreKO.FxPlacements.LoadJson(stem) is { } effects)
        {
            var count = 0; foreach (var fx in effects.Items.Where(f => new Vector3(-f.MapPos.X,f.MapPos.Y,f.MapPos.Z).DistanceSquaredTo(focus)<500*500).Take(120))
            {
                ct.ThrowIfCancellationRequested(); var anchor = new Node3D { Position = new(-fx.MapPos.X,fx.MapPos.Y,fx.MapPos.Z), Quaternion = new(fx.Rot.X,-fx.Rot.Y,-fx.Rot.Z,fx.Rot.W), Scale = Vector3.One * fx.Scale }; layers["Effects"].AddChild(anchor); LibreKO.Fx.Spawn(fx.Fx,anchor,Vector3.Zero);
                if (++count % 8 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }
    }
}
