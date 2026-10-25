using System.Globalization;
using System.Text;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Maps;

public static class PatrolPath
{
    public static IReadOnlyList<MapPoint> Decode(byte count, string? path)
    {
        path ??= "";
        if (path.Length != count * 8) throw new InvalidDataException("Patrol data must contain exactly eight characters per waypoint. Unchanged legacy data can be preserved.");
        var points = new List<MapPoint>(count);
        for (var i = 0; i < count; i++)
        {
            if (!short.TryParse(path.AsSpan(i * 8, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var x) ||
                !short.TryParse(path.AsSpan(i * 8 + 4, 4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var z))
                throw new InvalidDataException("Invalid four-character patrol coordinate.");
            points.Add(new(x, 0, z));
        }
        return points;
    }

    public static (byte Count, string Path) Encode(IReadOnlyList<MapPoint> points)
    {
        if (points.Count > byte.MaxValue) throw new InvalidDataException("A patrol has at most 255 waypoints.");
        var text = new StringBuilder(points.Count * 8);
        string Coordinate(float coordinate)
        {
            if (!float.IsFinite(coordinate) || coordinate != MathF.Truncate(coordinate) || coordinate is < -999 or > 9999)
                throw new InvalidDataException("Patrol coordinates must fit signed four-character integer entries (-999…9999).");
            var value = (int)coordinate;
            return value < 0 ? "-" + (-value).ToString("D3", CultureInfo.InvariantCulture) : value.ToString("D4", CultureInfo.InvariantCulture);
        }
        foreach (var point in points) { MapSpace.RequireFinite(point); text.Append(Coordinate(point.X)); text.Append(Coordinate(point.Z)); }
        return ((byte)points.Count, text.ToString());
    }
}
