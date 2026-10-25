using System.Numerics;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Client;
public static class CollisionEdits
{
    public static IReadOnlyList<MapPoint> Transform(IReadOnlyList<MapPoint> vertices,IReadOnlyList<int> triangleIndices,MapPoint translation,float[]? rotation=null,MapPoint? scale=null,MapPoint? pivot=null)
    {
        if(vertices.Count%3!=0||!CollisionCodec.Finite(translation))throw new InvalidDataException("Select complete finite collision triangles.");
        var q=ClientMapDocument.Quaternion(rotation??[0,0,0,1]);var s=scale??new(1,1,1);var center=pivot??new(0,0,0);if(!CollisionCodec.Finite(s)||!CollisionCodec.Finite(center)||s.X==0||s.Y==0||s.Z==0)throw new InvalidDataException("Scale and pivot must be finite, with nonzero scale.");
        var result=vertices.ToArray();foreach(var triangle in triangleIndices.Distinct())
        {
            if(triangle<0||triangle>=vertices.Count/3)throw new InvalidDataException("Collision triangle selection is out of range.");
            for(int i=triangle*3;i<triangle*3+3;i++){var p=vertices[i];var v=new Vector3((p.X-center.X)*s.X,(p.Y-center.Y)*s.Y,(p.Z-center.Z)*s.Z);v=Vector3.Transform(v,q);var next=new MapPoint(v.X+center.X+translation.X,v.Y+center.Y+translation.Y,v.Z+center.Z+translation.Z);if(!CollisionCodec.Finite(next))throw new InvalidDataException("Collision transform overflowed.");result[i]=next;}
        }return result;
    }
}
