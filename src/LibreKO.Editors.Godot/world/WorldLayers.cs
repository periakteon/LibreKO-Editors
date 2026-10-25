using Godot;
namespace LibreKO.Editors.Desktop;
public sealed class WorldLayers
{
    readonly Dictionary<string, Node3D> roots = [];
    public IReadOnlyDictionary<string, Node3D> Roots => roots;
    public Node3D this[string name] => roots[name];
    public WorldLayers(Node parent)
    {
        foreach (var name in new[] { "Terrain", "Scenery", "Water", "NPCs", "Monsters", "Spawn areas", "Patrol routes", "Start positions", "Map objects", "Collision", "Effects", "Server regene", "Server event tiles", "Server warps", "Server collision" })
        { var root = new Node3D { Name = name.Replace(' ', '_'), Visible = name is not ("Collision" or "Server collision") }; parent.AddChild(root); roots[name] = root; }
    }
    public void Clear() { foreach (var root in roots.Values) Ui.Clear(root); }
    public static MeshInstance3D Lines(IReadOnlyList<Vector3> points, Color color, bool closed = false)
    {
        var mesh = new ImmediateMesh(); var material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = color, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = true };
        if (points.Count > 1)
        { mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material); for (var i = 1; i < points.Count; i++) { mesh.SurfaceAddVertex(points[i - 1]); mesh.SurfaceAddVertex(points[i]); } if (closed) { mesh.SurfaceAddVertex(points[^1]); mesh.SurfaceAddVertex(points[0]); } mesh.SurfaceEnd(); }
        return new() { Mesh = mesh, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
    }
    public static Node3D Marker(Color color, float radius = .7f)
    {
        var marker = new Node3D(); marker.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = radius, Height = radius * 4 }, Position = new(0, radius * 2, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = color, Roughness = 1 }, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off }); return marker;
    }
    public static MeshInstance3D Segments(IReadOnlyList<Vector3> points,Color color)
    {
        var mesh=new ImmediateMesh();if(points.Count>1){mesh.SurfaceBegin(Mesh.PrimitiveType.Lines,new StandardMaterial3D{ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,AlbedoColor=color,Transparency=BaseMaterial3D.TransparencyEnum.Alpha});foreach(var point in points)mesh.SurfaceAddVertex(point);mesh.SurfaceEnd();}
        return new(){Mesh=mesh,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};
    }
}
