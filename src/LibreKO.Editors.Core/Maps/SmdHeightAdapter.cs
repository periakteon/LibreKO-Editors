using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Maps;
public static class SmdHeightAdapter
{
    public static HeightField ToCanonical(SmdDocument doc)
    {
        int n=doc.Heights.Size; var values=new float[n*n]; for(int x=0;x<n;x++) for(int z=0;z<n;z++) values[z*n+x]=doc.Heights.Heights[x*n+z];
        return new(n,doc.Heights.CellSize,values);
    }
    public static void ApplyCanonical(SmdDocument doc,HeightField field)
    {
        HeightSurface.Validate(field); if(field.Size!=doc.Heights.Size||field.CellSize!=doc.Heights.CellSize) throw new InvalidDataException("Server terrain dimensions must match; importing a complete SMD is required to resize.");
        int n=field.Size; for(int x=0;x<n;x++) for(int z=0;z<n;z++) doc.Heights.Heights[x*n+z]=field.Heights[z*n+x];
    }
    public static float Sample(SmdDocument doc,float x,float z)
    {
        var h=doc.Heights; if(!float.IsFinite(x)||!float.IsFinite(z))return float.MinValue;
        int ix=(int)(x/h.CellSize),iz=(int)(z/h.CellSize); float dx=(x-ix*h.CellSize)/h.CellSize,dz=(z-iz*h.CellSize)/h.CellSize;
        if(dx<0||dz<0||dx>=1||dz>=1||ix<0||iz<0||ix>=h.Size-1||iz>=h.Size-1)return float.MinValue;
        float a=h.Heights[ix*h.Size+iz],b=h.Heights[(ix+1)*h.Size+iz],c=h.Heights[ix*h.Size+iz+1],d=h.Heights[(ix+1)*h.Size+iz+1];
        // Keep the stock operation order to agree at floating-point triangle boundaries.
        if((ix+iz)%2==1)
        {
            if(dx+dz<1) {var h12=c+(b-c)*dx;var h32=a+(b-a)*dx;return h32+(h12-h32)*(dz/(1-dx));}
            if(dx==0)return c;var h12b=c+(b-c)*dx;var h13=c+(d-c)*dx;return h13+(h12b-h13)*((1-dz)/dx);
        }
        if(dz>dx) {var h12=c+(d-c)*dx;var h32=a+(d-a)*dx;return h12+(h32-h12)*((1-dz)/(1-dx));}
        if(dx==0)return a;var h12a=a+(b-a)*dx;var h13a=a+(d-a)*dx;return h12a+(h13a-h12a)*(dz/dx);
    }
    public static bool SameTerrain(SmdDocument a,SmdDocument b)=>a.Heights.Size==b.Heights.Size&&a.Heights.CellSize==b.Heights.CellSize&&a.Heights.Heights.AsSpan().SequenceEqual(b.Heights.Heights);
}
