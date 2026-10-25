using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Maps;
public record TerrainChoice(string? Stem,string Source,bool Available);
public static class TerrainMapping
{
    public static TerrainChoice Resolve(int zone,string serverSmd,IReadOnlyDictionary<int,string>? aliases,IReadOnlyDictionary<int,string> client,Func<string,bool> exists)
    {
        if(aliases?.TryGetValue(zone,out var custom)==true){SourcePaths.ValidateRelative(custom);return new(custom,"Explicit source-profile alias",exists(custom));}
        var stem=Path.GetFileNameWithoutExtension(serverSmd.Replace('\\','/')).ToLowerInvariant();
        if(stem!=""){SourcePaths.ValidateRelative(stem);if(exists(stem))return new(stem,"Server SMD filename match",true);}
        if(client.TryGetValue(zone,out var mapped)&&(stem.Equals(mapped,StringComparison.OrdinalIgnoreCase)||zone==21&&stem=="moradon2"&&mapped=="moradon")){SourcePaths.ValidateRelative(mapped);if(exists(mapped))return new(mapped,"Client zone mapping (audited SMD association) · "+zone+" → "+mapped,true);}
        return new(stem==""?null:stem,"Missing terrain mapping · configure a source-profile alias",false);
    }
}
