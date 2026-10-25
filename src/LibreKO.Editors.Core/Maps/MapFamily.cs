using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Maps;
public static class MapFamily
{
    static readonly string[] suffixes = [" VIII", " VII", " III", " II", " IV", " VI", " IX", " V", " X", " I"];
    public static string Normalize(string name)
    { var value = name.Trim(); foreach (var suffix in suffixes) if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return value[..^suffix.Length].TrimEnd(); return value; }
    static IEnumerable<MapFamilyEntry> Family(int zone, IReadOnlyList<MapFamilyEntry> maps, IReadOnlyList<int> data)
    {
        var current = maps.SingleOrDefault(x => x.ZoneId == zone);
        if (current == null || string.IsNullOrWhiteSpace(current.SmdName)) return [];
        return maps.Where(x => x.ZoneId != zone && data.Contains(x.ZoneId) && Normalize(x.MapName).Equals(Normalize(current.MapName), StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.ZoneId);
    }
    public static int Resolve(int zone, IReadOnlyList<MapFamilyEntry> maps, IReadOnlyList<int> data)
    {
        if (data.Contains(zone)) return zone;
        var current = maps.SingleOrDefault(x => x.ZoneId == zone);
        return Family(zone, maps, data).FirstOrDefault(x => x.SmdName.Trim().Equals(current!.SmdName.Trim(), StringComparison.OrdinalIgnoreCase))?.ZoneId ?? zone;
    }
    // The server accepts different-SMD siblings only after comparing their actual SMD heights.
    public static int[] TerrainCandidates(int zone, IReadOnlyList<MapFamilyEntry> maps, IReadOnlyList<int> data)
        => Resolve(zone, maps, data) != zone || data.Contains(zone) ? [] : Family(zone, maps, data).Select(x => x.ZoneId).ToArray();
}
