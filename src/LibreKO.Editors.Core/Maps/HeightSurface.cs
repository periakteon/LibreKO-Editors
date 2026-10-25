using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Maps;
public static class HeightSurface
{
    public static void Validate(HeightField field)
    {
        if (field.Size is < 2 or > 4097 || !float.IsFinite(field.CellSize) || field.CellSize <= 0 || field.Heights.Length != (long)field.Size * field.Size || field.Heights.Any(h => !float.IsFinite(h)))
            throw new InvalidDataException("Terrain preview requires a finite square height field of 2–4097 samples and a positive cell size.");
    }
    public static float? Sample(HeightField field, float x, float z)
    {
        if (!float.IsFinite(x) || !float.IsFinite(z)) return null;
        var gx = x / field.CellSize; var gz = z / field.CellSize; var ix = (int)MathF.Floor(gx); var iz = (int)MathF.Floor(gz);
        if (ix < 0 || iz < 0 || ix >= field.Size - 1 || iz >= field.Size - 1) return null;
        var fx = gx - ix; var fz = gz - iz;
        var a = field.Heights[iz * field.Size + ix] * (1 - fx) + field.Heights[iz * field.Size + ix + 1] * fx;
        var b = field.Heights[(iz + 1) * field.Size + ix] * (1 - fx) + field.Heights[(iz + 1) * field.Size + ix + 1] * fx;
        return a * (1 - fz) + b * fz;
    }
}
