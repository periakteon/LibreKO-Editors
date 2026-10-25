using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Maps;
namespace LibreKO.Editors.Desktop;
public partial class WorldViewport
{
    public void BeginManipulation()
    { if(!Active||!session.Operations.CanMutate)return;moving=selection.Keys.Where(rows.ContainsKey).Select(k=>rows[k]).Where(r=>r.Key.Table is WorldTable.NpcPositions or WorldTable.MapObjects or WorldTable.Warps).ToArray();pendingMove=null; }
    public void PreviewMove(MapPoint delta)
    {
        if(moving==null||moving.Length==0)return;
        if(SnapGrid)delta=new(MathF.Round(delta.X),MathF.Round(delta.Y),MathF.Round(delta.Z));
        try
        {
            var changes=WorldManipulationDraft.Move(moving,delta).ToArray();
            foreach(var row in changes)
            {
                if(SnapTerrain&&Heights!=null&&row.Key.Table is WorldTable.MapObjects or WorldTable.Warps)
                {
                    var position=PositionOf(new(row.Key,row.After!,row.Before!.Version));var height=HeightSurface.Sample(Heights,-position.X,position.Z);
                    if(height!=null)row.After!["Y"]=row.Key.Table==WorldTable.Warps?JsonValue.Create((int)MathF.Round(height.Value)):JsonValue.Create(height.Value);
                }
                if(visuals.TryGetValue(row.Key,out var model))model.Position=PositionOf(new(row.Key,row.After!,row.Before!.Version));
            }
            pendingMove=changes;DrawHandles();
        }
        catch(Exception e){status.Text=e.Message;}
    }
    public void CommitManipulation()
    {
        var changes=pendingMove;moving=null;pendingMove=null;if(changes==null||!Active||!session.Operations.CanMutate)return;
        try {edit(changes);foreach(var row in changes)AcceptGesture(row);status.Text=changes.Count+" positions changed in draft · Save to database to apply";}
        catch(Exception e){status.Text=e.Message;foreach(var row in changes)if(visuals.TryGetValue(row.Key,out var model))model.Position=PositionOf(row.Before!);}
        DrawHandles();
    }
    public void CancelManipulation()
    {if(moving!=null)foreach(var row in moving)if(visuals.TryGetValue(row.Key,out var model)&&IsInstanceValid(model))model.Position=PositionOf(row);moving=null;pendingMove=null;DrawHandles();}
    void AcceptGesture(WorldRowChange change)
    {var current=rows.GetValueOrDefault(change.Key)??change.Before!;var merged=WorldManipulationDraft.Merge(current,change);rows[change.Key]=new(change.Key,merged.After!,current.Version);if(change.Key.Table==WorldTable.NpcPositions)AddSpawnOverlays(rows[change.Key]);}
}
