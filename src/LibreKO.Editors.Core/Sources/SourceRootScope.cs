using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Sources;
// Captures physical root identity at selection; newly made keys cannot silently adopt a replaced directory.
public sealed class SourceRootScope
{
    readonly SourceProfile profile;readonly Dictionary<SourceRoot,string> identities=[];readonly Dictionary<SourceRoot,string> errors=[];
    public SourceRootScope(SourceProfile profile)
    {
        this.profile=profile;
        foreach(var entry in profile.Roots)
        {try{identities[entry.Key]=SourceIdentity.Root(entry.Value);}catch(Exception e)when(e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException){errors[entry.Key]=e.Message;}}
    }
    public SourceFileKey Key(SourceRoot root,string relative)
    {
        if(!identities.TryGetValue(root,out var identity))throw new InvalidOperationException("Reselect the unavailable source root before use: "+root+" · "+errors.GetValueOrDefault(root));
        var key=new SourceFileKey(profile.Id,root,identity,relative);SourcePaths.Resolve(profile,key,false);return key;
    }
    public void Guard(SourceFileKey key)
    {if(key.ProfileId!=profile.Id||!identities.TryGetValue(key.Root,out var identity)||identity!=key.RootIdentity)throw new InvalidOperationException("Source root identity differs from the selected profile. Reselect and rescan its roots.");}
}
