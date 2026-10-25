using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Maps;

public static class MapSpace
{
    public static MapPoint ToGodot(MapPoint point) { RequireFinite(point); return new(-point.X, point.Y, point.Z); }
    public static MapPoint FromGodot(MapPoint point) => ToGodot(point);
    public static void RequireFinite(MapPoint point)
    {
        if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || !float.IsFinite(point.Z)) throw new InvalidDataException("Map coordinates must be finite.");
    }
}
