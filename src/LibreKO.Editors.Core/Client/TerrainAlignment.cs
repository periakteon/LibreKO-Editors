using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Maps;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Client;
public static class TerrainAlignment
{
    public static (FileChange Change,IReadOnlyList<ValidationIssue> Issues) Prepare(SmdDocument server,SourceFileSnapshot smd,SourceFileSnapshot clientHeight,HeightField proposed,SourceStore store,bool mappingReviewed=false)
    {
        (FileChange,IReadOnlyList<ValidationIssue>) Error(string message)=>(new(Guid.NewGuid(),[]),[new("Terrain alignment",message)]);
        try
        {
            store.GuardKey(smd.Key);store.GuardKey(clientHeight.Key);
            if(smd.Key.Root!=SourceRoot.ServerMaps||clientHeight.Key.Root!=SourceRoot.ClientProject||smd.Content==null||clientHeight.Content==null) return Error("Link existing selected server SMD and client heightmap snapshots.");
            var parts=clientHeight.Key.RelativePath.Split('/');if(parts.Length!=4||parts[0]!="assets"||parts[1]!="terrain"||parts[3]!="heightmap.png")return Error("Select a recognised client terrain heightmap.");
            if(store.Read(smd.Key).Sha256!=smd.Sha256||store.Read(clientHeight.Key).Sha256!=clientHeight.Sha256)return Error("Sources changed externally; reopen and review their alignment.");
            var before=ClientHeightmapCodec.Decode(store.Blobs.Read(clientHeight.Content));HeightSurface.Validate(proposed);
            if(proposed.Size!=before.Size||proposed.Size!=server.Heights.Size||proposed.CellSize!=before.CellSize||proposed.CellSize!=server.Heights.CellSize)return Error("Server/client dimensions and 4-unit sample spacing must match before linking.");
            bool mapped=Path.GetFileNameWithoutExtension(smd.Key.RelativePath).Equals(parts[2],StringComparison.OrdinalIgnoreCase);
            if(!mapped&&store.Profile.TerrainAliases!=null)
            {
                var zoneSnapshot=store.Read(store.Key(SourceRoot.ServerData,"ZoneInfos.json"));
                if(zoneSnapshot.Content!=null&&JsonNode.Parse(TextDocument.Decode(store.Blobs.Read(zoneSnapshot.Content)).Text) is JsonArray zones)
                    mapped=zones.OfType<JsonObject>().Any(z=>int.TryParse(z["ZoneNo"]?.ToString(),out var id)&&store.Profile.TerrainAliases.TryGetValue(id,out var alias)&&alias.Equals(parts[2],StringComparison.OrdinalIgnoreCase)&&z["SmdName"]?.ToString().Trim().Equals(smd.Key.RelativePath,StringComparison.OrdinalIgnoreCase)==true);
            }
            if(!mapped&&!mappingReviewed)return Error("The server/client map association needs an explicit reviewed mapping.");
            if(!SmdHeightAdapter.ToCanonical(server).Heights.AsSpan().SequenceEqual(before.Heights)&&!mappingReviewed)return Error("Existing grid heights differ. Review the discrepancy before replacing both representations.");
            var changed=SmdCodec.Read(SmdCodec.Write(server));SmdHeightAdapter.ApplyCanonical(changed,proposed);
            return (new(Guid.NewGuid(),[new(smd,store.Blobs.Put(SmdCodec.Write(changed))),new(clientHeight,store.Blobs.Put(ClientHeightmapCodec.Encode(proposed)))]),[]);
        }
        catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or ArgumentException){return Error(e.Message);}
    }
}
