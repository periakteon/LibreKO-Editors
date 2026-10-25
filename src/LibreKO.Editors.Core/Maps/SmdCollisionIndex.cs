using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Maps;
public static class SmdCollisionIndex
{
    public static IReadOnlyList<ValidationIssue> Rebuild(SmdDocument doc,IReadOnlyDictionary<int,IReadOnlyList<int>>? shapeFaces)
    {
        List<ValidationIssue> Fail(string message)=>[new("Collision",message)];
        if(!doc.Sections.TryGetValue("Collision",out var section)||!section.Complete)return Fail("A complete collision section is required.");
        if(doc.CollisionVertices.Length!=doc.InitialVertices.Length||doc.CollisionVertices.Any(x=>!float.IsFinite(x)))return Fail("Relocation requires the original face count and finite coordinates.");
        if(shapeFaces==null)return Fail("Shape-to-face provenance is unknown; supply verified provenance or import a verified complete collision section.");
        int count=doc.CollisionVertices.Length/9;
        if(shapeFaces.Keys.Any(x=>x<0||x>ushort.MaxValue)||shapeFaces.Values.SelectMany(x=>x).Any(x=>x<0||x>=count)||doc.Cells.SelectMany(x=>x.Shapes).Distinct().Any(x=>!shapeFaces.ContainsKey(x))||Enumerable.Range(0,count).Except(shapeFaces.Values.SelectMany(x=>x)).Any())return Fail("Every existing shape and collision face requires verified provenance.");
        var cells=new Dictionary<(int X,int Z),List<int>[]>();
        int nx=(int)Math.Ceiling(doc.MapWidth/16d),nz=(int)Math.Ceiling(doc.MapHeight/16d);
        var old=doc.Cells.ToDictionary(c=>(c.X,c.Z));
        for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)cells[(x,z)]=Enumerable.Range(0,16).Select(_=>new List<int>()).ToArray();
        for(int face=0;face<count;face++)
        {
            int at=face*9;var xs=new[]{doc.CollisionVertices[at],doc.CollisionVertices[at+3],doc.CollisionVertices[at+6]};var zs=new[]{doc.CollisionVertices[at+2],doc.CollisionVertices[at+5],doc.CollisionVertices[at+8]};
            if(xs.Min()<0||zs.Min()<0||xs.Max()>doc.MapWidth||zs.Max()>doc.MapHeight)return Fail("Relocated collision faces must remain inside the map bounds.");
            // Conservative broad phase: include every intersected 4-unit bounding cell. Extra candidates are safe; omitted faces are not.
            int minX=Math.Max(0,(int)Math.Floor(xs.Min()/4)),maxX=Math.Min(nx*4-1,(int)Math.Floor(xs.Max()/4));
            int minZ=Math.Max(0,(int)Math.Floor(zs.Min()/4)),maxZ=Math.Min(nz*4-1,(int)Math.Floor(zs.Max()/4));
            for(int z=minZ;z<=maxZ;z++)for(int x=minX;x<=maxX;x++)cells[(x/4,z/4)][z%4*4+x%4].Add(face);
        }
        var rebuilt=new List<SmdCell>();using var bytes=new MemoryStream();using var w=new BinaryWriter(bytes);
        w.Write(doc.MapWidth);w.Write(doc.MapHeight);w.Write(count);foreach(var v in doc.CollisionVertices)w.Write(v);
        for(int z=0;z<nz;z++)for(int x=0;x<nx;x++)
        {
            var parts=cells[(x,z)];var faces=parts.SelectMany(a=>a).ToHashSet();var shapes=shapeFaces.Where(p=>p.Value.Any(faces.Contains)).Select(p=>(ushort)p.Key).Order().ToArray();
            uint exists=faces.Count==0&&shapes.Length==0?0:old.TryGetValue((x,z),out var prior)&&prior.Exists!=0?prior.Exists:1u;
            var sub=parts.Select(p=>p.SelectMany(f=>new[]{(uint)(f*3),(uint)(f*3+1),(uint)(f*3+2)}).ToArray()).ToArray();rebuilt.Add(new(x,z,exists,shapes,sub));w.Write(exists);if(exists==0)continue;
            w.Write(shapes.Length);foreach(var shape in shapes)w.Write(shape);foreach(var indices in sub){w.Write(indices.Length/3);foreach(var index in indices)w.Write(index);}
        }
        doc.Cells.Clear();doc.Cells.AddRange(rebuilt);doc.CollisionCandidate=bytes.ToArray();doc.CandidateVertices=doc.CollisionVertices.ToArray();return [];
    }
    public static void ImportSection(SmdDocument target,SmdDocument verified)
    {
        if(!verified.Sections.TryGetValue("Collision",out var section)||!section.Complete||target.MapWidth!=verified.MapWidth||target.MapHeight!=verified.MapHeight||verified.CollisionVertices.Any(x=>!float.IsFinite(x)))throw new InvalidDataException("Collision import requires a complete verified section with matching dimensions.");
        var raw=verified.CollisionCandidate.Length>0?verified.CollisionCandidate:verified.RawCollision;
        target.CollisionVertices=verified.CollisionVertices.ToArray();target.CandidateVertices=target.CollisionVertices.ToArray();target.CollisionCandidate=raw.ToArray();target.Cells.Clear();target.Cells.AddRange(verified.Cells.Select(c=>new SmdCell(c.X,c.Z,c.Exists,c.Shapes.ToArray(),c.Subcells.Select(x=>x.ToArray()).ToArray())));
    }
}
