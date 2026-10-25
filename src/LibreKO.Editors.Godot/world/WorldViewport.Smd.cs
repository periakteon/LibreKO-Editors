using Godot;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public partial class WorldViewport
{
    public SmdMapLayers? SourceLayers{get;internal set;}
    internal Func<InputEvent,bool>? AuthoringInput;
    CancellationTokenSource? sourceTerrainUpdate;
    internal async Task RefreshServerTerrainAsync(SmdMapLayers owner,HeightField heights)
    {
        if(SourceLayers!=owner||Heights!=null&&Heights.Size==heights.Size&&Heights.Heights.AsSpan().SequenceEqual(heights.Heights))return;
        sourceTerrainUpdate?.Cancel();sourceTerrainUpdate?.Dispose();sourceTerrainUpdate=CancellationTokenSource.CreateLinkedTokenSource(loading?.Token??session.Sources.Cancellation);var token=sourceTerrainUpdate.Token;Heights=heights;var root=SourceRoot("Terrain");var terrain=new WorldTerrainPreview{Visible=false};root.AddChild(terrain);
        try{await terrain.BuildAsync(heights,null,token);token.ThrowIfCancellationRequested();if(SourceLayers!=owner)return;foreach(var child in root.GetChildren().Where(n=>n!=terrain)){root.RemoveChild(child);child.QueueFree();}terrain.Visible=true;}
        catch(OperationCanceledException){}catch(Exception e){if(IsInsideTree())status.Text="Server terrain preview: "+e.Message;}
        finally{if(IsInstanceValid(terrain)&&!terrain.Visible){terrain.GetParent()?.RemoveChild(terrain);terrain.QueueFree();}}
    }
    static LibreKO.GroundTextures? AuthoredGround(GroundSurface surface)
    {
        if(surface.TileCount>512)return null;
        return new(){MapSize=surface.Size,TileCount=surface.TileCount,TilePx=surface.TilePixels,Atlas=surface.Atlas.ToArray(),CellLayer=surface.Cells.Select(c=>c.Primary<surface.TileCount?c.Primary:LibreKO.GroundTextures.NoTile).ToArray(),CellLayer2=surface.Cells.Select(c=>c.Secondary<surface.TileCount?c.Secondary:LibreKO.GroundTextures.NoTile).ToArray(),Dir1=surface.Cells.Select(c=>c.Direction1).ToArray(),Dir2=surface.Cells.Select(c=>c.Direction2).ToArray()};
    }
    internal Node3D SourceRoot(string name)=>layers[name];
    internal Vector2 SourceScreen(Vector3 point)=>camera.UnprojectPosition(point)*canvas.Size/viewport.Size;
    internal MapPoint? SourcePointer(Vector2 point){var p=PointerMap(point);return p==null?null:new(p.Value.X,p.Value.Y,p.Value.Z);}
    internal void EnableSourceLayers()
    {
        var popup=layerMenu.GetPopup();var keys=layers.Roots.Keys.ToArray();
        for(int i=0;i<keys.Length;i++)if(keys[i].StartsWith("Server",StringComparison.Ordinal)){popup.SetItemDisabled(i,false);popup.SetItemText(i,keys[i]+" · SMD source");}
    }
}
