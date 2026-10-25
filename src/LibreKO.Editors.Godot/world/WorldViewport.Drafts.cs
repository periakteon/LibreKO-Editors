using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.References;
using LibreKO.Editors.Core.World;
namespace LibreKO.Editors.Desktop;
public partial class WorldViewport
{
    readonly HashSet<WorldKey> removedInDraft=[];
    public void SyncDraft(WorldDraft? draft)
    {
        var snapshots=draft?.Rows??[];var keys=snapshots.Select(r=>r.Key).ToHashSet();var removed=draft?.Build(Guid.Empty).Rows.Where(r=>r.After==null).Select(r=>r.Key).ToHashSet()??[];
        foreach(var key in rows.Where(p=>p.Value.Version=="missing"&&!keys.Contains(p.Key)).Select(p=>p.Key).ToArray())
        {
            if(visuals.Remove(key,out var model)&&IsInstanceValid(model)){model.GetParent()?.RemoveChild(model);model.QueueFree();}rows.Remove(key);prototypeKeys.Remove(key);RemoveOverlays(key);
        }
        foreach(var row in snapshots.Where(r=>r.Version=="missing"&&!rows.ContainsKey(r.Key)&&r.Key.Table is WorldTable.NpcPositions or WorldTable.MapObjects or WorldTable.Warps))
        {
            var zone=FieldValueParser.ReadInt32(row.Values["ZoneId"]);if(zone!=ContextZone&&zone!=DisplayZone)continue;
            rows[row.Key]=row;var monster=row.Key.Table==WorldTable.NpcPositions&&FieldValueParser.ReadInt32(row.Values["ActType"])<100;
            var node=new Node3D{Name="WorldRow_"+row.Key.Table+"_"+row.Key.Id,Position=PositionOf(row)};node.AddChild(WorldLayers.Marker(new("c8aa6b")));node.SetMeta("world_label",WorldWorkspace.RowLabel(row)+" · Draft");
            layers[row.Key.Table==WorldTable.NpcPositions?monster?"Monsters":"NPCs":"Map objects"].AddChild(node);visuals[row.Key]=node;if(row.Key.Table==WorldTable.NpcPositions){AddSpawnOverlays(row);_=RefreshPrototypeVisualAsync(row.Key);}
        }
        foreach(var key in removedInDraft.Except(removed))if(visuals.TryGetValue(key,out var node)&&IsInstanceValid(node)){node.Visible=true;if(rows.TryGetValue(key,out var row)&&key.Table==WorldTable.NpcPositions)AddSpawnOverlays(row);}
        removedInDraft.Clear();removedInDraft.UnionWith(removed);
        foreach(var key in removed)if(visuals.TryGetValue(key,out var node)&&IsInstanceValid(node)){node.Visible=false;RemoveOverlays(key);}
        Sync(snapshots);sceneList.Clear();var index=0;
        foreach(var key in rows.Keys){var text=visuals.TryGetValue(key,out var node)&&node.HasMeta("world_label")?node.GetMeta("world_label").AsString():WorldWorkspace.RowLabel(rows[key]);sceneList.AddItem(text);sceneList.SetItemDisabled(index++,removed.Contains(key));}
        selection.Set(selection.Keys.Where(rows.ContainsKey).Where(k=>!removed.Contains(k)).ToArray());DrawHandles();
    }
    void RemoveOverlays(WorldKey key)
    {if(key.Table!=WorldTable.NpcPositions)return;foreach(var (layer,name) in new[]{("Spawn areas","Area_"),("Patrol routes","Path_")})if(layers[layer].GetNodeOrNull<Node3D>(name+key.Id) is {} node){layers[layer].RemoveChild(node);node.QueueFree();}}
    async Task RefreshPrototypeVisualAsync(WorldKey key)
    {
        if(!rows.TryGetValue(key,out var row)||key.Table!=WorldTable.NpcPositions)return;
        var reference=ReferenceRules.ForSpawn(FieldValueParser.ReadInt32(row.Values["NpcId"]),(byte)FieldValueParser.ReadInt32(row.Values["ActType"]));var prototypeKey=new WorldKey(WorldTable.Npcs,reference.Id,IsMonster:reference.IsMonster);prototypeKeys[key]=prototypeKey;
        var serial=loadSerial;var source=session.Sources.Generation;
        try
        {
            if(!prototypes.TryGetValue(prototypeKey,out var prototype))
            {var found=await session.Client.SendAsync<WorldRowSnapshot[]>("world.get-batch",new[]{prototypeKey});prototype=found.SingleOrDefault();}
            if(!Active||serial!=loadSerial||source!=session.Sources.Generation||!rows.TryGetValue(key,out var latest)||!visuals.TryGetValue(key,out var node)||!IsInstanceValid(node))return;
            var latestRef=ReferenceRules.ForSpawn(FieldValueParser.ReadInt32(latest.Values["NpcId"]),(byte)FieldValueParser.ReadInt32(latest.Values["ActType"]));if(latestRef!=reference)return;
            prototypes[prototypeKey]=prototype;Node3D? model=null;if(prototype?.Values!=null&&visuals.Count<150)try{model=NpcPreviewBuilder.Build(prototype.Values);}catch{}
            Ui.Clear(node);node.AddChild(model??WorldLayers.Marker(reference.IsMonster==true?new("b96b5c"):new("bfa36a")));
            var parent=layers[reference.IsMonster==true?"Monsters":"NPCs"];if(node.GetParent()!=parent){node.GetParent()?.RemoveChild(node);parent.AddChild(node);}
            var label=key.Id+" · "+(prototype?.Values?["Name"]?.ToString()??"Missing prototype")+" · "+(reference.IsMonster==true?"Monster":"NPC")+" · "+reference.Id+(row.Version=="missing"?" · Draft":"");
            node.SetMeta("world_label",label);node.AddChild(new Label3D{Text=label,Position=new(0,4,0),FontSize=28,PixelSize=.012f,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,VisibilityRangeEnd=100});
        }
        catch(Exception e){if(serial==loadSerial&&Active)status.Text="Prototype visual unavailable · "+e.Message;}
    }
}
