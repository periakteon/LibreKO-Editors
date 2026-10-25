using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Sources;

public static class SeedShardResolver
{
    public static IReadOnlyList<string> Active(WorldTable table,IEnumerable<string> files)
    {
        var matches=files.Where(f=>ServerJsonCatalog.Format(f)?.Table==table).ToArray();
        var shards=table==WorldTable.NpcPositions?matches.Where(f=>!f.Equals(table+".json",StringComparison.OrdinalIgnoreCase)).ToArray():[];
        return (shards.Length>0?shards:matches.Where(f=>f.Equals(table+".json",StringComparison.OrdinalIgnoreCase))).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public static string StructureWarning(WorldTable table)=>table==WorldTable.NpcPositions
        ?"Insertion, removal or reordering can renumber every following generated spawn Index during a future seed run. Database rows are not synchronized."
        :"File order and duplicates are preserved; compare and apply each destination explicitly. Server seed policies may keep the first duplicate.";
}
