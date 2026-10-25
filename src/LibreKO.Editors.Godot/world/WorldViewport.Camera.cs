using Godot;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public partial class WorldViewport
{
    void OrbitView(){freeCamera=false;camera.Projection=Camera3D.ProjectionType.Perspective;PlaceCamera();}
    void FreeView(){freeCamera=true;canvas.GrabFocus();}
    void ToggleList(){if(Active)sceneDialog.PopupCentered();}
    void PlaceCamera()
    {
        camera.Position = focus + new Vector3(MathF.Sin(yaw) * MathF.Cos(pitch), -MathF.Sin(pitch), MathF.Cos(yaw) * MathF.Cos(pitch)) * distance;
        camera.LookAt(focus, Vector3.Up); if (camera.Projection == Camera3D.ProjectionType.Orthogonal) camera.Size = distance; DrawHandles();RefreshSceneryIfNeeded();
    }
    public void TopView() { freeCamera = false; pitch = -1.565f; yaw = 0; camera.Projection = Camera3D.ProjectionType.Orthogonal; PlaceCamera(); }
    public void FocusSelection()
    {
        var points = selection.Keys.Where(visuals.ContainsKey).Select(k => visuals[k].Position).ToArray(); if (points.Length == 0) {FrameTerrain();return;}
        focus = points.Aggregate(Vector3.Zero,(sum,p)=>sum+p)/points.Length; distance = Math.Clamp(points.Max(p=>p.DistanceTo(focus))*3+20,15,1000); PlaceCamera();
    }
    public void FrameTerrain()
    {
        if(Heights==null)return;var extent=(Heights.Size-1)*Heights.CellSize;var min=Heights.Heights.Min();var max=Heights.Heights.Max();focus=new(-extent/2,(float)(((double)min+max)/2),extent/2);distance=(float)Math.Clamp(Math.Max(extent,(double)max-min)*1.8,5,18000);PlaceCamera();
    }
    internal ViewCameraPose CaptureCamera()=>new(focus,distance,yaw,pitch,freeCamera,camera.Projection,camera.Size);
    internal void RestoreCamera(ViewCameraPose pose){focus=pose.Focus;distance=pose.Distance;yaw=pose.Yaw;pitch=pose.Pitch;freeCamera=pose.Free;camera.Projection=pose.Projection;PlaceCamera();camera.Size=pose.Size;}
    public void SetSelection(IReadOnlyList<WorldKey> keys, bool notify = true)
    {
        selection.Set(keys.Where(rows.ContainsKey).ToArray()); DrawHandles();
        sceneList.DeselectAll(); var all = rows.Keys.ToArray(); foreach(var key in selection.Keys) { var index=Array.IndexOf(all,key);if(index>=0)sceneList.Select(index,false); }
        if(notify)SelectionChanged?.Invoke(selection.Keys);
    }
    void DrawHandles()
    {
        if(!IsInstanceValid(handles))return;Ui.Clear(handles);var length=Math.Clamp(distance*.06f,2,20);
        foreach(var key in selection.Keys)
        {
            if(!visuals.TryGetValue(key,out var node)||!IsInstanceValid(node))continue;var point=node.Position+new Vector3(0,.3f,0);
            handles.AddChild(WorldLayers.Lines([point,point+Vector3.Left*length],new("d26c62")));
            if(key.Table!=WorldTable.NpcPositions)handles.AddChild(WorldLayers.Lines([point,point+Vector3.Up*length],new("78ad6b")));
            handles.AddChild(WorldLayers.Lines([point,point+new Vector3(0,0,length)],new("6d9ccc")));
            handles.AddChild(WorldLayers.Lines([point+new Vector3(-1,0,-1),point+new Vector3(1,0,-1),point+new Vector3(1,0,1),point+new Vector3(-1,0,1)],Ui.Accent,true));
        }
        DrawPatrolHandles();
    }
    Vector2 ViewPixel(Vector2 position) => new(position.X * viewport.Size.X / Math.Max(1,canvas.Size.X),position.Y * viewport.Size.Y / Math.Max(1,canvas.Size.Y));
    WorldKey? Pick(Vector2 position)
    {
        var pixel=ViewPixel(position);WorldKey? best=null;var nearest=24f*24;
        foreach(var (key,node) in visuals)
        {
            if(!IsInstanceValid(node)||!node.IsVisibleInTree()||camera.IsPositionBehind(node.GlobalPosition))continue;
            var distance=camera.UnprojectPosition(node.GlobalPosition+Vector3.Up*1.5f).DistanceSquaredTo(pixel);if(distance<nearest){nearest=distance;best=key;}
        }
        return best;
    }
    Vector3? PointerMap(Vector2 position)
    {
        var pixel=ViewPixel(position);var origin=camera.ProjectRayOrigin(pixel);var ray=camera.ProjectRayNormal(pixel);if(Math.Abs(ray.Y)<.00001f)return null;
        var height=selection.Keys.FirstOrDefault() is {} key&&visuals.TryGetValue(key,out var node)?node.Position.Y:0;
        Vector3 point=Vector3.Zero;
        for(var pass=0;pass<4;pass++) {var t=(height-origin.Y)/ray.Y;if(t<0)return null;point=origin+ray*t;if(Heights!=null)height=Core.Maps.HeightSurface.Sample(Heights,-point.X,point.Z)??height;}
        return new(-point.X,point.Y,point.Z);
    }
    int HandleAt(Vector2 position)
    {
        if(selection.Keys.Count==0)return -1;var key=selection.Keys[0];if(!visuals.TryGetValue(key,out var node))return -1;
        var point=node.Position+Vector3.Up*.3f;var length=Math.Clamp(distance*.06f,2,20);var axes=new[]{Vector3.Left,Vector3.Up,new Vector3(0,0,1)};var pixel=ViewPixel(position);
        for(var i=0;i<3;i++)if((i!=1||key.Table!=WorldTable.NpcPositions)&&camera.UnprojectPosition(point+axes[i]*length).DistanceTo(pixel)<14)return i;return -1;
    }
    public void ProcessInput(InputEvent e)
    {
        if(!Active||!session.Operations.CanMutate)return;
        if(AuthoringInput?.Invoke(e)==true){canvas.AcceptEvent();return;}
        if(SourceLayers?.Input(e)==true){canvas.AcceptEvent();return;}
        if(e is InputEventKey{Pressed:true,Keycode:Key.Escape}){CancelManipulation();canvas.AcceptEvent();return;}
        if(PatrolInput(e)){canvas.AcceptEvent();return;}
        if(e is InputEventMouseButton button)
        {
            if(button.Pressed)canvas.GrabFocus();
            if(button.Pressed&&button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown){distance=Math.Clamp(distance*(button.ButtonIndex==MouseButton.WheelUp?.88f:1.12f),2,18000);PlaceCamera();canvas.AcceptEvent();return;}
            if(button.ButtonIndex==MouseButton.Left)
            {
                if(!button.Pressed){CommitManipulation();return;}
                dragAxis=HandleAt(button.Position);var picked=Pick(button.Position);
                if(dragAxis<0&&picked!=null){var selected=selection.Keys.ToHashSet();if(button.ShiftPressed){if(!selected.Add(picked))selected.Remove(picked);}else if(!selected.Contains(picked))selected=[picked];SetSelection(selected.ToArray());}
                if(selection.Keys.Count>0&&(dragAxis>=0||picked!=null)&&PointerMap(button.Position) is {} start){dragStart=start;verticalDrag=0;BeginManipulation();}
            }
        }
        if(e is InputEventMouseMotion motion)
        {
            if((motion.ButtonMask&MouseButtonMask.Right)!=0){yaw-=motion.Relative.X*.008f;pitch=Math.Clamp(pitch+motion.Relative.Y*.006f,-1.565f,-.05f);PlaceCamera();}
            else if((motion.ButtonMask&MouseButtonMask.Middle)!=0){focus+=(-camera.Basis.X*motion.Relative.X+camera.Basis.Y*motion.Relative.Y)*distance*.0015f;PlaceCamera();}
            else if(moving!=null&&(motion.ButtonMask&MouseButtonMask.Left)!=0&&PointerMap(motion.Position) is {} point)
            {
                verticalDrag-=motion.Relative.Y*distance*.004f;var delta=point-dragStart;delta=dragAxis switch{0=>new(delta.X,0,0),1=>new(0,verticalDrag,0),2=>new(0,0,delta.Z),_=>new(delta.X,0,delta.Z)};
                PreviewMove(new(delta.X,delta.Y,delta.Z));
            }
            else if(Pick(motion.Position) is {} hovered&&visuals.TryGetValue(hovered,out var model))hover.Text=model.GetMeta("world_label").AsString()+" · Database";
        }
    }
}
internal record ViewCameraPose(Vector3 Focus,float Distance,float Yaw,float Pitch,bool Free,Camera3D.ProjectionType Projection,float Size);
