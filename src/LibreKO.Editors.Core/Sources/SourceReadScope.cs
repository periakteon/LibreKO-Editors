using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Sources;

// The validation transaction carries exact evidence for every source it consumed,
// including missing include candidates. AsyncLocal keeps independent applies apart.
public sealed class SourceReadScope:IDisposable
{
    static readonly AsyncLocal<SourceReadScope?> current=new();readonly SourceReadScope? previous;readonly Dictionary<SourceFileKey,SourceFileSnapshot> reads=[];bool sealedScope;
    SourceReadScope(){previous=current.Value;current.Value=this;}
    public static SourceReadScope Begin()=>new();
    public static void Record(SourceFileSnapshot snapshot)
    {
        var scope=current.Value;if(scope==null)return;lock(scope.reads){if(scope.sealedScope)return;if(scope.reads.TryGetValue(snapshot.Key,out var before)&&before!=snapshot)throw new SourceConflictException("Source dependency changed during validation: "+snapshot.Key.RelativePath);scope.reads[snapshot.Key]=snapshot;}
    }
    public IReadOnlyList<SourceFileSnapshot> Seal(){lock(reads){sealedScope=true;return reads.Values.ToArray();}}
    public void Dispose(){current.Value=previous;}
    public static void Verify(SourceStore store,IReadOnlyList<SourceFileSnapshot> snapshots)
    {
        foreach(var before in snapshots)
        {
            try{var now=store.Read(before.Key);if(now.Exists!=before.Exists||now.Sha256!=before.Sha256)throw new SourceConflictException("Source dependency changed after review: "+before.Key.RelativePath);}
            catch(Exception e)when(e is IOException or InvalidOperationException or UnauthorizedAccessException){throw new SourceConflictException("Source dependency is no longer contained/unchanged: "+before.Key.RelativePath+" · "+e.Message);}
        }
    }
}
