namespace LibreKO.Editors.Contracts;

public record MapPoint(float X, float Y, float Z);
public record MapFamilyEntry(int ZoneId, string MapName, string SmdName);
public record WorldMapContext(int ZoneId, int? StartSourceZone, int SpawnSourceZone, IReadOnlyList<int> TerrainCandidates, IReadOnlyList<string> Diagnostics);
public record HeightField(int Size, float CellSize, float[] Heights);
public record RgbaImage(int Width,int Height,byte[] Pixels);
public record GroundCell(int Primary,int Secondary,byte Direction1,byte Direction2);
public record GroundSurface(int Size,int TileCount,int TilePixels,GroundCell[] Cells,byte[] Atlas);
public record TerrainStroke(string Tool,int X,int Z,int Radius,float Strength,float Target);
