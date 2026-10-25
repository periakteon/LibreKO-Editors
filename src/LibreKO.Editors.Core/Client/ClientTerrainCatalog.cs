using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Client;
public static class ClientTerrainCatalog
{
    public static bool Accepts(SourceFileKey key)
    {var parts=key.RelativePath.Split('/');return key.Root==SourceRoot.ClientProject&&parts.Length==4&&parts[0]=="assets"&&parts[1]=="terrain"&&parts[2].Length>0&&parts[3] is "heightmap.png" or "splat.png" or "tiles.png";}
    public static void Register(SourceValidationRegistry registry,BlobStore blobs)=>registry.Register("client-terrain/1",Accepts,t=>
    {
        var bytes=blobs.Read(t.Candidate);if(t.Key.RelativePath.EndsWith("heightmap.png",StringComparison.Ordinal)){ClientHeightmapCodec.Decode(bytes);return [];}
        var image=RgbaPngCodec.Decode(bytes);
        if(t.Key.RelativePath.EndsWith("splat.png",StringComparison.Ordinal)&& (image.Width!=image.Height||image.Width is <2 or >4097))return [new("Splat","A 2–4097 square RGBA8 splat grid is required.")];
        if(t.Key.RelativePath.EndsWith("tiles.png",StringComparison.Ordinal)&& (image.Height%image.Width!=0||image.Height/image.Width is <1 or >8192))return [new("Atlas","Use a vertical RGBA8 atlas of 1–8192 square tiles.")];return [];
    });
}
