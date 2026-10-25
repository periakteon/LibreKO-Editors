using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Maps;
public static class SmdSpatialEdits
{
    static void Finite(MapPoint p) {if(!float.IsFinite(p.X)||!float.IsFinite(p.Y)||!float.IsFinite(p.Z))throw new InvalidDataException("Map coordinates must be finite.");}
    public static void MoveWarp(SmdDocument doc,int ordinal,MapPoint point){Finite(point);doc.Warps[ordinal]=doc.Warps[ordinal] with{Position=point};}
    public static void MoveRegene(SmdDocument doc,int ordinal,MapPoint point){Finite(point);doc.Regene[ordinal]=doc.Regene[ordinal] with{Position=point};}
    public static void SetEventTile(SmdDocument doc,int x,int z,short value)
    {if(x<0||z<0||x>=doc.Heights.Size||z>=doc.Heights.Size||doc.EventTiles.Length!=doc.Heights.Size*doc.Heights.Size)throw new ArgumentOutOfRangeException(nameof(x),"A complete event-tile section and valid coordinates are required.");doc.EventTiles[x*doc.Heights.Size+z]=value;}
}
public sealed class SmdDraft
{
    readonly byte[] original; readonly Stack<byte[]> undo=[],redo=[];
    public SmdDocument Document{get;private set;}
    public SmdDraft(SmdDocument document){original=SmdCodec.Write(document);Document=SmdCodec.Read(original);}
    public bool IsDirty=>!SmdCodec.Write(Document).AsSpan().SequenceEqual(original);
    public void Edit(Action<SmdDocument> action)
    {
        var before=SmdCodec.Write(Document);var candidate=SmdCodec.Read(before);action(candidate);var after=SmdCodec.Write(candidate);
        if(before.AsSpan().SequenceEqual(after))return;undo.Push(before);redo.Clear();Document=candidate;
    }
    public bool Undo(){if(!undo.TryPop(out var previous))return false;redo.Push(SmdCodec.Write(Document));Document=SmdCodec.Read(previous);return true;}
    public bool Redo(){if(!redo.TryPop(out var next))return false;undo.Push(SmdCodec.Write(Document));Document=SmdCodec.Read(next);return true;}
}
