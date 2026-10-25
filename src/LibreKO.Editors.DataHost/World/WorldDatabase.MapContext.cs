using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Maps;
using LibreKO.Editors.Core.Sources;
using Microsoft.EntityFrameworkCore;
namespace LibreKO.Editors.DataHost.World;
public sealed partial class WorldDatabase
{
    public async Task<WorldMapContext> MapContextAsync(int zone, SourceStore? sources = null)
    {
        var report = await CheckCompatibilityAsync(); Require(report, [WorldTable.ZoneInfos]); await using var db = Open();
        var rows = await ReadAsync(db, WorldTable.ZoneInfos); var maps = rows.Select(r => new MapFamilyEntry(r.Key.Id, r.Values["MapName"]?.ToString() ?? "", r.Values["SmdName"]?.ToString() ?? "")).ToArray();
        var diagnostics = new List<string>(); int? startSource = null; var spawnSource = zone; int[] candidates = [];
        if (report.Tables.Single(t => t.Table == WorldTable.StartPositions).CanBrowse)
        {
            var starts = (await ReadAsync(db, WorldTable.StartPositions)).Select(r => r.Key.Id).ToArray();
            var resolved = MapFamily.Resolve(zone, maps, starts); if (starts.Contains(resolved)) startSource = resolved;
        }
        else diagnostics.Add("Start positions are unavailable for this database schema.");
        if (report.Tables.Single(t => t.Table == WorldTable.NpcPositions).CanBrowse)
        {
            await db.Database.OpenConnectionAsync(); await using var cmd = Command(db, "SELECT DISTINCT " + Column(db, WorldTable.NpcPositions, "ZoneId") + " FROM " + Table(db, WorldTable.NpcPositions), []);
            await using var reader = await cmd.ExecuteReaderAsync(); var zones = new List<int>(); while (await reader.ReadAsync()) zones.Add(Convert.ToInt32(reader.GetValue(0)));
            spawnSource = MapFamily.Resolve(zone, maps, zones); candidates = MapFamily.TerrainCandidates(zone, maps, zones);
            if(candidates.Length>0&&sources!=null)
            {
                SmdDocument Load(int id)
                {
                    var relative=maps.Single(m=>m.ZoneId==id).SmdName.Trim();
                    var key=new SourceFileKey(sources.Profile.Id,SourceRoot.ServerMaps,SourceIdentity.Root(sources.Profile.Roots[SourceRoot.ServerMaps]),relative);
                    var snapshot=sources.Read(key);if(!snapshot.Exists)throw new FileNotFoundException("Server map unavailable: "+relative);
                    return SmdCodec.Read(sources.Blobs.Read(snapshot.Content!));
                }
                try
                {
                    var current=Load(zone);
                    foreach(var candidate in candidates)
                    {
                        try {if(SmdHeightAdapter.SameTerrain(current,Load(candidate))){spawnSource=candidate;diagnostics.Add("Inherited spawn owner "+candidate+" verified against selected server SMD terrain; database edits target that owner.");candidates=[];break;}}
                        catch(Exception e)when(e is IOException or InvalidOperationException or KeyNotFoundException){diagnostics.Add("Cannot verify sibling "+candidate+": "+e.Message);}
                    }
                }
                catch(Exception e)when(e is IOException or InvalidOperationException or KeyNotFoundException){diagnostics.Add("Cannot verify server terrain: "+e.Message);}
            }
            if (candidates.Length > 0) diagnostics.Add("Different-SMD spawn inheritance requires comparing the actual server map terrain: " + string.Join(", ", candidates) + ". No sibling is assumed without that evidence.");
        }
        else diagnostics.Add("Spawn positions are unavailable for this database schema.");
        return new(zone, startSource, spawnSource, candidates, diagnostics);
    }
}
