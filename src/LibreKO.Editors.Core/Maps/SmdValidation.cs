using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Core.Maps;
public static class SmdValidation
{
    public static IReadOnlyList<ValidationIssue> Validate(SmdDocument doc)
    {
        var issues=new List<ValidationIssue>();
        void Error(string f,string m)=>issues.Add(new(f,m));
        bool Point(MapPoint p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);
        void Count(string name,int count,int initial)
        {
            if(!doc.Sections.TryGetValue(name,out var section)) { if(count!=0) Error(name,"Section boundaries are unknown."); return; }
            if(count>SourceLimits.MaximumJsonRows) Error(name,"Too many records.");
            if(count!=initial && !doc.Complete) Error(name,"Structural edits require a complete SMD.");
            if(!section.CanWrite && count>0) Error(name,"Section is not independently writable.");
        }
        if(doc.Heights.Size!=doc.OriginalHeights.Size || doc.Heights.CellSize!=doc.OriginalHeights.CellSize || doc.Heights.Heights.Length!=doc.OriginalHeights.Heights.Length) Error("Terrain","Changing dimensions requires importing a verified complete SMD.");
        if(doc.Heights.Heights.Any(x=>!float.IsFinite(x))) Error("Terrain","Heights must be finite.");
        Count("Objects",doc.Objects.Count,doc.InitialObjects.Count); Count("Regene",doc.Regene.Count,doc.InitialRegene.Count); Count("Warps",doc.Warps.Count,doc.InitialWarps.Count);
        void Reorder<T>(string section,List<T> current,List<T> initial) where T:SmdRecord
        { if(doc.Complete) return; for(int i=0;i<current.Count;i++) { int old=current[i].SourceOrdinal;if(old!=i)Error(section,"Reordering/replacing rows requires a complete SMD."); } }
        Reorder("Objects",doc.Objects,doc.InitialObjects); Reorder("Regene",doc.Regene,doc.InitialRegene); Reorder("Warps",doc.Warps,doc.InitialWarps);
        if(doc.EventTiles.Length!=doc.InitialTiles.Length) Error("Tiles","Tile dimensions cannot change independently.");
        if(!doc.CollisionVertices.SequenceEqual(doc.InitialVertices) && doc.CollisionCandidate.Length==0) Error("Collision","Rebuild collision cells with verified shape/face provenance before saving changed vertices.");
        if(doc.CollisionCandidate.Length>0&&!doc.CollisionVertices.SequenceEqual(doc.CandidateVertices)) Error("Collision","Vertices changed after the verified rebuild; rebuild again.");
        for(int i=0;i<doc.Objects.Count;i++) if(!Point(doc.Objects[i].Position)) Error("Objects/"+i,"Coordinates must be finite.");
        for(int i=0;i<doc.Regene.Count;i++) { var r=doc.Regene[i]; if(!Point(r.Position)||!float.IsFinite(r.AreaX)||!float.IsFinite(r.AreaZ)||r.AreaX<0||r.AreaZ<0) Error("Regene/"+i,"Coordinates/ranges must be finite, with nonnegative ranges."); }
        for(int i=0;i<doc.Warps.Count;i++)
        {
            var w=doc.Warps[i]; if(!Point(w.Position)||!float.IsFinite(w.Radius)||w.Radius<0) Error("Warps/"+i,"Coordinates/radius must be finite, with nonnegative radius.");
            if(w.Name.Length>32||w.Announcement.Length>256||w.Name.Concat(w.Announcement).Any(c=>c>127||c=='\0')) Error("Warps/"+i,"Use ASCII without NUL: name32 and announcement256 bytes maximum.");
        }
        return issues;
    }
    public static void Register(SourceValidationRegistry registry,BlobStore blobs)=>registry.Register("server-smd/1",k=>k.Root==SourceRoot.ServerMaps&&k.RelativePath.EndsWith(".smd",StringComparison.OrdinalIgnoreCase),t=>Validate(SmdCodec.Read(blobs.Read(t.Candidate))));
}
