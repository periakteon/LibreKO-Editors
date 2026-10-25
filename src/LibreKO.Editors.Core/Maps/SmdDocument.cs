using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Maps;
public sealed class SmdDocument
{
    internal byte[] Original=[];
    public byte[] OriginalBytes=>Original.ToArray();
    public HeightField Heights {get;internal set;}=null!; // Native SMD x-major; use SmdHeightAdapter for canonical data.
    internal HeightField OriginalHeights=null!;
    public List<SmdObject> Objects{get;}=[];
    public List<SmdRegene> Regene{get;}=[];
    public List<SmdWarp> Warps{get;}=[];
    internal List<SmdObject> InitialObjects=[];
    internal List<SmdRegene> InitialRegene=[];
    internal List<SmdWarp> InitialWarps=[];
    public Dictionary<string,SmdSection> Sections{get;}=[];
    public List<string> Diagnostics{get;}=[];
    public float MapWidth{get;internal set;}
    public float MapHeight{get;internal set;}
    public float[] CollisionVertices{get;internal set;}=[];
    internal float[] InitialVertices=[];
    public List<SmdCell> Cells{get;}=[];
    internal byte[] CollisionCandidate=[];
    internal float[] CandidateVertices=[];
    public short[] EventTiles{get;internal set;}=[];
    internal short[] InitialTiles=[];
    public bool Complete=>Sections.TryGetValue("Warps",out var s)&&s.Complete;
    public byte[] RawCollision=>Raw("Collision");
    public byte[] RawTiles=>Raw("Tiles");
    public byte[] Tail=>Original[Sections.Values.Select(s=>s.Offset+s.Length).DefaultIfEmpty(0).Max()..];
    byte[] Raw(string name)=>Sections.TryGetValue(name,out var s)?Original.AsSpan(s.Offset,s.Length).ToArray():[];
}
