using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Maps;
public abstract record SmdRecord { internal int SourceOrdinal{get;set;}=-1; }
public record SmdWarp(short Id,string Name,string Announcement,uint Fee,short Zone,MapPoint Position,float Radius,short Nation):SmdRecord;
public record SmdObject(int Belong,short Index,short Type,short ControlTarget,short Status,MapPoint Position):SmdRecord;
public record SmdRegene(MapPoint Position,float AreaX,float AreaZ):SmdRecord;
public record SmdSection(int Offset,int Length,bool Complete,bool CanWrite,int DeclaredCount=0);
public sealed record SmdCell(int X,int Z,uint Exists,ushort[] Shapes,IReadOnlyList<uint[]> Subcells);
