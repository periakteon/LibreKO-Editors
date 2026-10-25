using System.Security.Cryptography;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Sources;

public sealed class SourceStore
{
    public SourceProfile Profile { get; }
    public BlobStore Blobs { get; }
    readonly SourceRootScope scope;
    public SourceFileKey Key(SourceRoot root,string relative)=>scope.Key(root,relative);
    public void GuardKey(SourceFileKey key){scope.Guard(key);SourcePaths.Resolve(Profile,key,false);}

    public SourceStore(SourceProfile profile, string workspaceRoot)
    {
        SourcePaths.Validate(profile, workspaceRoot);
        Profile = profile;
        scope=new(profile);
        Blobs = new(workspaceRoot);
    }

    public SourceFileSnapshot Read(SourceFileKey key)
    {
        GuardKey(key);
        var path = SourcePaths.Resolve(Profile, key, false);
        if (Directory.Exists(path)) throw new InvalidOperationException("A source document must be a file.");
        if (!File.Exists(path)){var missing=new SourceFileSnapshot(key,false,"",null);SourceReadScope.Record(missing);return missing;}
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bytes = new byte[SourceLimits.CheckedLength(stream.Length)];
            stream.ReadExactly(bytes);
        }
        SourcePaths.Resolve(Profile, key, false);
        var blob = Blobs.Put(bytes);
        var snapshot=new SourceFileSnapshot(key, true, Convert.ToHexString(SHA256.HashData(bytes)), blob);SourceReadScope.Record(snapshot);return snapshot;
    }

    public FileMutation Stage(SourceFileKey key, byte[]? after)
    {
        SourcePaths.Resolve(Profile, key, true);
        return new(Read(key), after is null ? null : Blobs.Put(after));
    }
}
