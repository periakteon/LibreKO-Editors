using System.Security.Cryptography;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Sources;

public sealed class BlobStore
{
    readonly string root;
    public BlobStore(string workspaceRoot)
    {
        SourcePaths.AssertNoLinks(workspaceRoot);
        root = Path.GetFullPath(workspaceRoot);
    }

    public BlobRef Put(byte[] bytes)
    {
        SourceLimits.CheckedLength(bytes.LongLength);
        var blob = new BlobRef(Convert.ToHexString(SHA256.HashData(bytes)), bytes.LongLength);
        var path = PathFor(blob);
        if (File.Exists(path)) { Read(blob); return blob; }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        SourcePaths.AssertNoLinks(path);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(bytes); stream.Flush(true); }
        try { File.Move(temp, path); }
        catch (IOException) when (File.Exists(path)) { Read(blob); File.Delete(temp); }
        return blob;
    }

    public byte[] Read(BlobRef blob)
    {
        var path = PathFor(blob);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var length = SourceLimits.CheckedLength(stream.Length);
        if (length != blob.Length) throw new InvalidDataException("Stored source blob length mismatch.");
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        if (!Convert.ToHexString(SHA256.HashData(bytes)).Equals(blob.Sha256, StringComparison.Ordinal))
            throw new InvalidDataException("Stored source blob hash mismatch.");
        return bytes;
    }

    string PathFor(BlobRef blob)
    {
        SourceLimits.CheckedLength(blob.Length);
        if (blob.Sha256.Length != 64 || blob.Sha256.Any(c => !char.IsAsciiHexDigit(c)) || blob.Sha256 != blob.Sha256.ToUpperInvariant())
            throw new InvalidDataException("Invalid source blob identity.");
        var path = WorkspacePaths.ResolveOwnedPath(root, "source-blobs/" + blob.Sha256[..2] + "/" + blob.Sha256);
        SourcePaths.AssertNoLinks(path);
        return path;
    }
}
