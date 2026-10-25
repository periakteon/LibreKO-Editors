using Godot;
namespace LibreKO.Editors.Desktop;
public static class WorldSceneryPreview
{
    public static async Task BuildAsync(Node3D parent, string stem, Vector3 focusMap, CancellationToken ct)
    {
        var objects = LibreKO.ZoneObjects.LoadJson(stem); if (objects == null) return;
        var cache = new Dictionary<string, PackedScene?>(); var count = 0;
        foreach (var item in objects.Items.OrderBy(o => o.MapPos.DistanceSquaredTo(focusMap)).Take(900))
        {
            ct.ThrowIfCancellationRequested(); if (item.MapPos.DistanceSquaredTo(focusMap) > 900 * 900) continue;
            var key = new string(item.Name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) || ch is '_' or '-' or '.' ? ch : '_').ToArray());
            if (!cache.TryGetValue(key, out var scene)) { var path = "res://assets/objects/" + key + ".glb"; cache[key] = scene = ResourceLoader.Exists(path) ? ResourceLoader.Load<PackedScene>(path) : null; }
            if (scene == null) continue; var instance = scene.Instantiate<Node3D>();
            var rotation = new Quaternion(item.Rot.X, -item.Rot.Y, -item.Rot.Z, item.Rot.W); if (rotation.LengthSquared() < .00001f) rotation = Quaternion.Identity;
            instance.Transform = new(new Basis(rotation.Normalized()) * Basis.FromScale(item.Scale), new(-item.MapPos.X, item.MapPos.Y, item.MapPos.Z)); parent.AddChild(instance);
            if (NpcPreviewBuilder.Find<AnimationPlayer>(instance) is { } animation && animation.GetAnimationList().FirstOrDefault() is string clip) animation.Play(clip);
            if (++count % 12 == 0) { await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame); ct.ThrowIfCancellationRequested(); }
        }
    }
    public static async Task CollisionAsync(Node3D parent, string stem, CancellationToken ct)
    {
        var collision = LibreKO.ZoneCollision.Load(stem); if (collision == null) return;
        for (var offset = 0; offset < Math.Min(collision.Verts.Length, 300000); offset += 3000)
        {
            ct.ThrowIfCancellationRequested(); var points = new List<Vector3>();
            for (var i = offset; i + 2 < Math.Min(collision.Verts.Length, offset + 3000); i += 3)
            {
                Vector3 Mirror(Vector3 v) => new(-v.X, v.Y, v.Z);
                var a = Mirror(collision.Verts[i]); var b = Mirror(collision.Verts[i + 1]); var c = Mirror(collision.Verts[i + 2]);
                points.AddRange(new[] { a, b, b, c, c, a });
            }
            // Explicit pairs, rather than a connected polyline between unrelated triangles.
            var mesh = new ImmediateMesh(); var material = new StandardMaterial3D { AlbedoColor = new(1,.35f,.15f,.35f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha };
            if (points.Count > 0) { mesh.SurfaceBegin(Mesh.PrimitiveType.Lines, material); foreach (var point in points) mesh.SurfaceAddVertex(point); mesh.SurfaceEnd(); parent.AddChild(new MeshInstance3D { Mesh = mesh }); }
            await parent.ToSignal(parent.GetTree(), SceneTree.SignalName.ProcessFrame);
        }
    }
}
