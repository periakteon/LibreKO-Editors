using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Maps;
namespace LibreKO.Editors.Desktop;
public partial class WorldViewport
{
    bool pathEditing,pathDragging;
    WorldKey? pathKey;
    List<MapPoint> pathPoints=[];
    int pathIndex=-1;
    void PatrolAction(long action)
    {
        if(selection.Keys.Count!=1||rows.GetValueOrDefault(selection.Keys[0]) is not {Key.Table:WorldTable.NpcPositions} row){status.Text="Select one spawn to edit its patrol.";return;}
        try
        {
            if(action==1){WorldPatrolEditor.Show(session,row,change=>{edit([change]);rows[row.Key]=new(row.Key,change.After!,row.Version);AddSpawnOverlays(rows[row.Key]);});return;}
            var points=PatrolPath.Decode((byte)FieldValueParser.ReadInt32(row.Values["DotCnt"]),row.Values["Path"]?.ToString()).ToArray();
            if(action==2){ApplyPatrolPoints(row.Key,points.AsEnumerable().Reverse().ToArray());return;}if(action==3){ApplyPatrolPoints(row.Key,[]);return;}
            pathEditing=!pathEditing;pathKey=row.Key;pathPoints=points.ToList();DrawHandles();status.Text=pathEditing?"Patrol mode · click terrain to add · drag a waypoint · right-click a point to remove · Escape to leave":"Patrol mode closed";
        }
        catch(Exception e){status.Text="Path preserved · "+e.Message+" Use the coordinate list's explicit repair action.";}
    }
    Vector3 PathPoint(MapPoint p)=>new(-p.X,(Heights==null?0:HeightSurface.Sample(Heights,p.X,p.Z)??0)+.4f,p.Z);
    void DrawPatrolHandles()
    {
        if(!pathEditing)return;
        for(var i=0;i<pathPoints.Count;i++)
        {var node=WorldLayers.Marker(i==pathIndex?new("f3d78c"):new("6fb8cb"),.35f);node.Position=PathPoint(pathPoints[i]);handles.AddChild(node);node.AddChild(new Label3D{Text=(i+1).ToString(),Position=new(0,1.5f,0),FontSize=34,Billboard=BaseMaterial3D.BillboardModeEnum.Enabled,NoDepthTest=true});}
        if(pathPoints.Count>1)handles.AddChild(WorldLayers.Lines(pathPoints.Select(PathPoint).ToArray(),new("72c3d4")));
    }
    int PickWaypoint(Vector2 position)
    {var pixel=ViewPixel(position);for(var i=0;i<pathPoints.Count;i++)if(!camera.IsPositionBehind(PathPoint(pathPoints[i]))&&camera.UnprojectPosition(PathPoint(pathPoints[i])).DistanceTo(pixel)<18)return i;return -1;}
    bool PatrolInput(InputEvent e)
    {
        if(!pathEditing||pathKey==null||!rows.ContainsKey(pathKey))return false;
        if(e is InputEventMouseButton b)
        {
            if(b.ButtonIndex==MouseButton.Right&&b.Pressed&&PickWaypoint(b.Position) is var remove&&remove>=0){pathPoints.RemoveAt(remove);ApplyPatrolPoints(pathKey,pathPoints);DrawHandles();return true;}
            if(b.ButtonIndex==MouseButton.Left)
            {
                if(!b.Pressed){CommitPatrol();return true;}
                pathIndex=PickWaypoint(b.Position);
                if(pathIndex<0&&PointerMap(b.Position) is {} point){pathPoints.Add(new(MathF.Round(point.X),0,MathF.Round(point.Z)));pathIndex=pathPoints.Count-1;}
                pathDragging=pathIndex>=0;DrawHandles();return true;
            }
        }
        if(e is InputEventMouseMotion m&&pathDragging&&(m.ButtonMask&MouseButtonMask.Left)!=0&&PointerMap(m.Position) is {} next)
        {pathPoints[pathIndex]=new(MathF.Round(next.X),0,MathF.Round(next.Z));DrawHandles();return true;}
        return false;
    }
    public void ApplyPatrolPoints(WorldKey key,IReadOnlyList<MapPoint> points)
    {
        if(!Active||!session.Operations.CanMutate||!rows.TryGetValue(key,out var before)||key.Table!=WorldTable.NpcPositions)return;
        var encoded=PatrolPath.Encode(points);var values=(JsonObject)before.Values.DeepClone();values["DotCnt"]=encoded.Count;values["Path"]=encoded.Path;
        var change=new WorldRowChange(key,before,values);edit([change]);AcceptGesture(change);if(pathKey==key)pathPoints=points.ToList();status.Text="Patrol updated in draft · one undo step";
    }
    void CommitPatrol(){if(!pathDragging||pathKey==null)return;pathDragging=false;try{ApplyPatrolPoints(pathKey,pathPoints);}catch(Exception e){status.Text=e.Message;CancelPatrol(false);}}
    void CancelPatrol(bool close)
    {pathDragging=false;pathIndex=-1;if(pathKey!=null&&rows.TryGetValue(pathKey,out var row))try{pathPoints=PatrolPath.Decode((byte)FieldValueParser.ReadInt32(row.Values["DotCnt"]),row.Values["Path"]?.ToString()).ToList();}catch{pathPoints=[];}if(close)pathEditing=false;DrawHandles();}
    public void FlushManipulations(){CommitPatrol();CommitManipulation();}
    public override void _Input(InputEvent e)
    {
        if(!Active||!session.Operations.CanMutate)return;
        if(e is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:false}){CommitPatrol();CommitManipulation();}
        if(e is InputEventKey{Pressed:true,Keycode:Key.Escape}&&(moving!=null||pathEditing)){CancelManipulation();CancelPatrol(true);GetViewport().SetInputAsHandled();}
    }
}
