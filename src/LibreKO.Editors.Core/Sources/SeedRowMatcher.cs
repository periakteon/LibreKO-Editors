using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Sources;

public enum SeedMatchState { Unique,Missing,Ambiguous }
public record SeedMatch(SeedMatchState State,IReadOnlyList<SourceRowToken> Candidates);
public static class SeedRowMatcher
{
    public static SeedMatch Match(WorldRowSnapshot row,IReadOnlyList<JsonSourceDocument> sources)
    {
        var active=SeedShardResolver.Active(row.Key.Table,sources.Select(x=>x.Snapshot.Key.RelativePath));
        var matches=sources.Where(x=>active.Contains(x.Snapshot.Key.RelativePath,StringComparer.OrdinalIgnoreCase))
            .SelectMany(x=>x.Rows).Where(x=>row.Values.All(field=>row.Key.Table==WorldTable.NpcPositions && field.Key=="Index" || Equal(field.Value,Value(x.Values,field.Key))))
            .Select(x=>x.Token).ToArray();
        return new(matches.Length==0?SeedMatchState.Missing:matches.Length==1?SeedMatchState.Unique:SeedMatchState.Ambiguous,matches);
    }
    internal static JsonNode? Value(JsonObject row,string field)
    {
        var matches=row.Where(x=>string.Equals(x.Key,field,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(matches.Length>1) throw new InvalidDataException("Ambiguous seed field: "+field);
        return matches.SingleOrDefault().Value;
    }
    internal static bool Equal(JsonNode? expected,JsonNode? source)
    {
        if(expected==null) return source==null;
        var text=source?.ToString();
        return expected.GetValueKind() switch
        {
            JsonValueKind.True or JsonValueKind.False=>expected.GetValue<bool>() ? text is "1" or "true" : text is null or "0" or "false",
            JsonValueKind.String=>expected.GetValue<string>()==(text??""),
            JsonValueKind.Number=>decimal.TryParse(expected.ToString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var a) && decimal.TryParse(text??"0",NumberStyles.Float,CultureInfo.InvariantCulture,out var b) && a==b,
            _=>JsonNode.DeepEquals(expected,source)
        };
    }
}
