using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Maps;
namespace LibreKO.Editors.Desktop;
public partial class WorldTerrainPreview : Node3D
{
    public int PreviewStep { get; private set; }
    public async Task BuildAsync(HeightField field, LibreKO.GroundTextures? tiles, CancellationToken ct)
    {
        HeightSurface.Validate(field); var n = field.Size; var step = 1; while ((n - 1) / step > 512) step *= 2; PreviewStep = step;
        var minimum = field.Heights.Min(); var maximum = field.Heights.Max(); var span = Math.Max(maximum - minimum, 1);
        Material material;
        if (tiles != null && tiles.MapSize == n)
        {
            var shader = ResourceLoader.Load<Shader>("res://shaders/terrain_tile.gdshader"); var tiled = new ShaderMaterial { Shader = shader };
            tiled.SetShaderParameter("tiles", tiles.BuildTextureArray()); tiled.SetShaderParameter("cell_size", field.CellSize); material = tiled;
        }
        else material = new StandardMaterial3D { VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled, Roughness = 1 };
        var chunks = 0;
        for (var z = 0; z < n - 1; z += 32 * step)
            for (var x = 0; x < n - 1; x += 32 * step)
            {
                ct.ThrowIfCancellationRequested(); var origin = new Vector3(-x * field.CellSize, 0, z * field.CellSize);
                using var surface = new SurfaceTool(); surface.Begin(Mesh.PrimitiveType.Triangles);
                if (tiles != null) surface.SetCustomFormat(0, SurfaceTool.CustomFormat.RgbaFloat);
                var xEnd = Math.Min(n - 1, x + 32 * step); var zEnd = Math.Min(n - 1, z + 32 * step);
                void Vertex(int px, int pz, int cx, int cz)
                {
                    var h = field.Heights[pz * n + px]; var ix0 = Math.Max(0, px - 1); var ix1 = Math.Min(n - 1, px + 1); var iz0 = Math.Max(0, pz - 1); var iz1 = Math.Min(n - 1, pz + 1);
                    var dx = (field.Heights[pz * n + ix1] - field.Heights[pz * n + ix0]) / ((ix1 - ix0) * field.CellSize);
                    var dz = (field.Heights[iz1 * n + px] - field.Heights[iz0 * n + px]) / ((iz1 - iz0) * field.CellSize);
                    surface.SetNormal(new Vector3(dx, 1, -dz).Normalized());
                    surface.SetColor(new Color("5b704b").Lerp(new("a5a08c"), (h - minimum) / span));
                    if (tiles != null && tiles.MapSize == n)
                    {
                        var index = cz * n + cx; var corner = (px == cx, pz == cz) switch { (true, true) => 2, (true, false) => 0, (false, false) => 1, _ => 3 };
                        var a = tiles.Dir1[index]; var b = tiles.Dir2[index]; var l1 = tiles.CellLayer[index]; var l2 = tiles.CellLayer2[index];
                        surface.SetUV(new(LibreKO.GroundTextures.TileDirU[a, corner], LibreKO.GroundTextures.TileDirV[a, corner]));
                        surface.SetUV2(new(LibreKO.GroundTextures.TileDirU[b, corner], LibreKO.GroundTextures.TileDirV[b, corner]));
                        surface.SetCustom(0, new Color(l1 == LibreKO.GroundTextures.NoTile ? 0 : l1, l2 == LibreKO.GroundTextures.NoTile ? 0 : l2, l2 == LibreKO.GroundTextures.NoTile ? 0 : 1, 0));
                    }
                    else surface.SetUV(new((px - cx) / (float)step, (pz - cz) / (float)step));
                    surface.AddVertex(new Vector3(-px * field.CellSize, h, pz * field.CellSize) - origin);
                }
                for (var cz = z; cz < zEnd; cz += step) for (var cx = x; cx < xEnd; cx += step)
                {
                    var nx = Math.Min(n - 1, cx + step); var nz = Math.Min(n - 1, cz + step);
                    Vertex(cx, cz, cx, cz); Vertex(cx, nz, cx, cz); Vertex(nx, cz, cx, cz);
                    Vertex(nx, cz, cx, cz); Vertex(cx, nz, cx, cz); Vertex(nx, nz, cx, cz);
                }
                AddChild(new MeshInstance3D { Mesh = surface.Commit(), MaterialOverride = material, Position = origin });
                if (++chunks % 2 == 0) { await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); ct.ThrowIfCancellationRequested(); }
            }
    }
}
