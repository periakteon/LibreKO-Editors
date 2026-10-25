using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Maps;
namespace LibreKO.Editors.Core.Client;
public static class TerrainBrush
{
    public static HeightField Apply(HeightField original,TerrainStroke stroke)=>ApplyStroke(original,[stroke]);
    public static HeightField ApplyStroke(HeightField original,IReadOnlyList<TerrainStroke> strokes)
    {
        HeightSurface.Validate(original);var result=original.Heights.ToArray();if(strokes.Count==0)return original with{Heights=result};
        var first=strokes[0];var weights=new Dictionary<int,float>();int n=original.Size;
        foreach(var stroke in strokes)
        {
            if(stroke.Tool!=first.Tool||stroke.Strength!=first.Strength||stroke.Target!=first.Target)throw new InvalidDataException("A brush stroke must use one tool and fixed parameters.");
            if(stroke.Tool is not ("raise" or "lower" or "flatten" or "smooth")||stroke.X<0||stroke.Z<0||stroke.X>=n||stroke.Z>=n||stroke.Radius is <0 or >4096||!float.IsFinite(stroke.Strength)||stroke.Strength<0||!float.IsFinite(stroke.Target))throw new InvalidDataException("Invalid terrain brush or grid bounds.");
            if(stroke.Strength==0)continue;
            for(int z=Math.Max(0,stroke.Z-stroke.Radius);z<=Math.Min(n-1,stroke.Z+stroke.Radius);z++)for(int x=Math.Max(0,stroke.X-stroke.Radius);x<=Math.Min(n-1,stroke.X+stroke.Radius);x++)
            {
                var distance=MathF.Sqrt((x-stroke.X)*(x-stroke.X)+(z-stroke.Z)*(z-stroke.Z));if(distance>stroke.Radius)continue;float weight=stroke.Radius==0?1:1-distance/(stroke.Radius+1f);int at=z*n+x;
                weights[at]=Math.Max(weights.GetValueOrDefault(at),weight);
            }
        }
        foreach(var (at,weight) in weights)
        {
            double value=original.Heights[at],amount=first.Strength*weight,target=first.Target;
            if(first.Tool=="smooth"){int x=at%n,z=at/n,count=0;double sum=0;for(int dz=-1;dz<=1;dz++)for(int dx=-1;dx<=1;dx++)if(x+dx>=0&&z+dz>=0&&x+dx<n&&z+dz<n){sum+=original.Heights[(z+dz)*n+x+dx];count++;}target=sum/count;}
            var changed=(float)(first.Tool switch{"raise"=>value+amount,"lower"=>value-amount,_=>value+(target-value)*Math.Clamp(amount,0,1)});
            if(!float.IsFinite(changed))throw new InvalidDataException("Terrain brush exceeds finite height range.");if(changed!=original.Heights[at])result[at]=changed;
        }
        return original with{Heights=result};
    }
    static void Region(HeightField field,int x,int z,int width,int height)
    {HeightSurface.Validate(field);if(x<0||z<0||width<=0||height<=0||(long)x+width>field.Size||(long)z+height>field.Size)throw new InvalidDataException("Terrain region exceeds grid bounds.");}
    public static float[] ExportRegion(HeightField field,int x,int z,int width,int height)
    {Region(field,x,z,width,height);var region=new float[width*height];for(int row=0;row<height;row++)Array.Copy(field.Heights,(z+row)*field.Size+x,region,row*width,width);return region;}
    public static HeightField ImportRegion(HeightField field,int x,int z,int width,int height,float[] values)
    {Region(field,x,z,width,height);if(values.Length!=(long)width*height||values.Any(v=>!float.IsFinite(v)))throw new InvalidDataException("Terrain region values are invalid.");var copy=field.Heights.ToArray();for(int row=0;row<height;row++)Array.Copy(values,row*width,copy,(z+row)*field.Size+x,width);return field with{Heights=copy};}
}
