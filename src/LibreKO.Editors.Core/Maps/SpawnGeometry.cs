using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Maps;
public static class SpawnGeometry
{
    public static (MapPoint Min, MapPoint Max) Bounds(int x, int z, short range)
    { var spread = Math.Max(0, (int)range); return (new(x - spread, 0, z - spread), new(x + spread, 0, z + spread)); }
}
