using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Sources;

public static class SourcePaths
{
    public static string Canonical(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return OperatingSystem.IsWindows() ? full.ToUpperInvariant() : full;
    }

    public static string Resolve(SourceProfile profile, SourceFileKey key, bool write)
    {
        if (key.ProfileId != profile.Id || !profile.Roots.TryGetValue(key.Root, out var selected))
            throw new InvalidOperationException("The document belongs to a different source profile.");
        if (write && key.Root is SourceRoot.Pipeline or SourceRoot.KoInput or SourceRoot.BuildOutput)
            throw new InvalidOperationException("This source root is read-only; builds use a separate output workflow.");
        ValidateRelative(key.RelativePath);
        var root = Canonical(selected);
        if (!string.Equals(SourceIdentity.Root(root), key.RootIdentity, StringComparison.Ordinal))
            throw new InvalidOperationException("The source root was replaced. Reopen and review this document.");
        var path = Path.GetFullPath(Path.Combine(root, key.RelativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
        if (!Contains(root, path) || Canonical(path) == root)
            throw new InvalidOperationException("The path must stay beneath the selected source root.");
        AssertNoLinks(path);
        return path;
    }

    public static void Validate(SourceProfile profile, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(profile.Id) || profile.Id.Length > 128 || profile.Roots.Count == 0)
            throw new InvalidOperationException("A source profile needs an identity and at least one selected root.");
        var roots = profile.Roots.Select(x => (x.Key, Path: Canonical(x.Value))).ToArray();
        var workspace = Canonical(workspaceRoot);
        AssertNoLinks(workspace);
        foreach (var (kind, root) in roots)
        {
            if (!Enum.IsDefined(kind)) throw new InvalidOperationException("Unknown source root.");
            if (Overlaps(root, workspace)) throw new InvalidOperationException("Source and editor history/workspace roots must not overlap.");
        }
        for (var i = 0; i < roots.Length; i++)
            for (var j = i + 1; j < roots.Length; j++)
                if (Overlaps(roots[i].Path, roots[j].Path))
                    throw new InvalidOperationException("Selected source/build roots must not overlap.");
    }

    public static void AssertNoLinks(string path)
    {
        for (var part = Path.GetFullPath(path); part != null; part = Path.GetDirectoryName(part))
        {
            try
            {
                if ((File.GetAttributes(part) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Linked files and source path components are not supported.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    internal static bool Contains(string root, string path) => Canonical(path).StartsWith(
        Canonical(root) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    internal static bool Overlaps(string a, string b) => Canonical(a) == Canonical(b) || Contains(a, b) || Contains(b, a);

    public static void ValidateRelative(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains(':') || Path.IsPathRooted(relative) || relative.StartsWith('\\'))
            throw new InvalidOperationException("Use a relative source path without drive names or alternate streams.");
        foreach (var segment in relative.Replace('\\', '/').Split('/'))
        {
            var stem = segment.Split('.')[0].ToUpperInvariant();
            if (segment is "" or "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.Any(c => c < 32 || "<>\"|?*".Contains(c)) ||
                stem is "CON" or "PRN" or "AUX" or "NUL" ||
                (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9'))
                throw new InvalidOperationException("Unsafe or noncanonical source filename.");
        }
    }
}
