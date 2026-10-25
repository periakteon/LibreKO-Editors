namespace LibreKO.Editors.Core.Sources;

public static class SourceLimits
{
    public const long MaximumFileBytes = 512L * 1024 * 1024;
    public const int MaximumJsonRows = 1_000_000;
    public const int MaximumJsonDepth = 64;

    public static int CheckedLength(long length)
    {
        if (length < 0 || length > MaximumFileBytes)
            throw new InvalidDataException("Source files are limited to 512 MiB.");
        return checked((int)length);
    }
}
