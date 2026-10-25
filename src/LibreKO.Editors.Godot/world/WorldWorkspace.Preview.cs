using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Sources;
using LibreKO.Editors.Core.Maps;
namespace LibreKO.Editors.Desktop;
public partial class WorldWorkspace
{
    int? previewZone, previewOwner;
    int previewSource=-1, previewDatabase=-1;
    string previewReferences="";
    string prototypeVisual="";
    async Task RefreshMapPreviewAsync(WorldKey? key,int view)
    {
        if(sourceMode){MapView.SetActive(false);return;}
        var row=Draft?.Rows.FirstOrDefault(r=>r.Key==key);
        if(row==null||!Active){MapView.Visible=false;MapView.SetActive(false);previewDetails.Visible=true;return;}
        var supported=key!.Table is WorldTable.ZoneInfos or WorldTable.StartPositions or WorldTable.NpcPositions or WorldTable.MapObjects or WorldTable.Warps or WorldTable.GameEvents or WorldTable.Npcs;
        MapView.Visible=supported;MapView.SetActive(supported);previewDetails.Visible=!supported;
        if(!supported)return;
        try
        {
            if(key.Table==WorldTable.Npcs)
            {
                var signature=string.Join("/",new[]{"Id","IsMonster","ModelId","Size","WeaponType1","WeaponType2","NpcType"}.Select(f=>row.Values[f]?.ToString()));
                if(prototypeVisual!=signature||MapView.DisplayZone!=null||!MapView.Loaded){MapView.ShowPrototype(row.Values);prototypeVisual=signature;}
                previewZone=null;return;
            }
            var zone=key.Table switch{WorldTable.ZoneInfos or WorldTable.StartPositions=>key.Id,WorldTable.GameEvents=>key.Zone!.Value,_=>FieldValueParser.ReadInt32(row.Values["ZoneId"])};
            if(previewZone==zone&&previewOwner==MapView.ContextZone&&previewSource==Session.Sources.Generation&&previewDatabase==Session.Generation&&previewReferences==Session.References.Scope&&MapView.Loaded)
            {MapView.Sync(Draft!.Rows);return;}
            var source=Session.Sources.Generation;var database=Session.Generation;
            var zoneRow=await Session.Client.SendAsync<WorldRowSnapshot?>("world.get",new WorldKey(WorldTable.ZoneInfos,zone));
            if(view!=ViewGeneration||!Active||sourceMode||source!=Session.Sources.Generation||database!=Session.Generation)return;
            var smd=zoneRow?.Values?["SmdName"]?.ToString()??"";
            var choice=TerrainMapping.Resolve(zone,smd,Session.Sources.Profile?.TerrainAliases,LibreKO.ZoneCatalog.All.GroupBy(z=>z.Id).ToDictionary(g=>g.Key,g=>g.First().Stem),stem=>Godot.FileAccess.FileExists("res://assets/terrain/"+stem+"/heightmap.png"));
            var stem=choice.Stem;
            LibreKO.Heightmap? height=null;
            if(choice.Available&&stem!=null)height=LibreKO.Heightmap.LoadPng(stem);
            var context=await Session.Client.SendAsync<WorldMapContext>("world.map-context",new{zone});
            if(view!=ViewGeneration||!Active||sourceMode||source!=Session.Sources.Generation||database!=Session.Generation)return;
            previewZone=zone;previewOwner=context.SpawnSourceZone;previewSource=source;previewDatabase=database;previewReferences=Session.References.Scope;
            await MapView.LoadMapAsync(height==null?null:new(height.MapSize,height.CellSize,height.Heights),new(WorldTable.NpcPositions,Zone:context.SpawnSourceZone),height==null?null:stem,Session.Sources.Cancellation,zone);
            if(view==ViewGeneration&&Active)MapView.DescribeSource(choice.Source+" · server SMD: "+smd);
            if(view==ViewGeneration&&Active&&Draft!=null)MapView.Sync(Draft.Rows);
        }
        catch(OperationCanceledException){}
        catch(Exception e){if(view==ViewGeneration&&Active)Session.Shell.SetStatus("World preview unavailable · "+e.Message);}
    }
    void ApplySpatialBatch(IReadOnlyList<WorldRowChange> changes)
    {
        if(!Active||!Session.Operations.CanMutate||Draft==null)return;
        foreach(var row in changes)if(row.Before!=null&&Draft.Rows.All(r=>r.Key!=row.Key)){Draft.Include([row.Before]);baseline.Add(row.Before);}
        ApplyBatch(changes.Select(change=>change.Before!=null&&change.After!=null?WorldManipulationDraft.Merge(Draft.Rows.Single(r=>r.Key==change.Key),change):change).ToArray());
    }
    async Task SelectSpatialAsync(IReadOnlyList<WorldKey> keys)
    {
        if(!Active||Draft==null||!Session.Operations.CanMutate)return;
        try
        {
            foreach(var key in keys)if(MapView.Rows.TryGetValue(key,out var row))await IncludeAsync(row);
            if(keys.FirstOrDefault() is {} selected&&Selected!=selected){Selected=selected;RenderDocument();var index=Documents.FindIndex(r=>r.Key==selected);if(index>=0)Library.Select(index);}
        }
        catch(Exception e){Session.Shell.SetStatus(e.Message);}
    }
}
