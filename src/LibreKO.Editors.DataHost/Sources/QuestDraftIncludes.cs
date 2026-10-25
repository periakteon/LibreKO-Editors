using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
using LibreKO.Quests;
namespace LibreKO.Editors.DataHost.Sources;

public sealed class QuestDraftIncludes: IQuestIncludes
{
    readonly Shared shared; readonly string file; readonly int depth;
    public QuestDraftIncludes(SourceStore store,IReadOnlyList<SourceValidationTicket> tickets,IReadOnlyList<SourceFileKey>? deleted=null)
    {
        shared=new(store,tickets,deleted??[]);file="";
        foreach(var ticket in tickets){store.GuardKey(ticket.Key);store.Blobs.Read(ticket.Candidate);}
        foreach(var key in deleted??[]){store.GuardKey(key);SourcePaths.Resolve(store.Profile,key,true);}
    }
    QuestDraftIncludes(Shared shared,string file,int depth){this.shared=shared;this.file=file;this.depth=depth;}
    public IQuestIncludes ForFile(string fileName)
    {if(depth>=64)throw new InvalidDataException("Quest include depth exceeds 64.");return new QuestDraftIncludes(shared,QuestManifest.Path(fileName),depth+1);}
    public bool TryRead(string path,out string text,out string fileName)
    {
        if(++shared.Reads>8192)throw new InvalidDataException("Quest validation exceeded the 8192 include-read budget.");
        path=path.Replace('\\','/');if(!System.IO.Path.HasExtension(path))path+=".quest";QuestManifest.Path(path);
        var directory=System.IO.Path.GetDirectoryName(file)?.Replace('\\','/')??"";
        for(int level=0;level<=3;level++)
        {
            var relative=directory.Length==0?path:directory+"/"+path;var bytes=Read(SourceRoot.ServerQuests,relative);
            if(bytes!=null){text=TextDocument.Decode(bytes).Text;CheckText(text);fileName=relative;return true;}
            if(directory.Length==0)break;directory=System.IO.Path.GetDirectoryName(directory)?.Replace('\\','/')??"";
        }
        text="";fileName=path;return false;
    }
    public byte[]? Read(SourceRoot root,string relative)
    {
        var store=shared.Store;SourcePaths.ValidateRelative(relative);
        var key=store.Key(root,relative);
        SourcePaths.Resolve(store.Profile,key,false);
        if(shared.Deleted.Contains(key))return null;
        if(shared.Tickets.TryGetValue(key,out var ticket))return store.Blobs.Read(ticket.Candidate);
        var snapshot=store.Read(key);return snapshot.Content==null?null:store.Blobs.Read(snapshot.Content);
    }
    public static void CheckText(string text)
    {
        if(text.Length>2*1024*1024)throw new InvalidDataException("Quest scripts are limited to 2 MiB of text.");
        foreach(var line in text.Split('\n'))if(line.TakeWhile(c=>c is ' ' or '\t').Count()>256)throw new InvalidDataException("Quest indentation exceeds the supported compiler nesting bound.");
    }
    sealed class Shared
    {
        public SourceStore Store{get;}
        public Dictionary<SourceFileKey,SourceValidationTicket> Tickets{get;}
        public HashSet<SourceFileKey> Deleted{get;}
        public int Reads;
        public Shared(SourceStore store,IReadOnlyList<SourceValidationTicket> tickets,IReadOnlyList<SourceFileKey> deleted)
        {
            Store=store;if(tickets.Count>4096||deleted.Count>4096)throw new InvalidDataException("Quest validation batches are limited to 4096 targets.");
            Tickets=tickets.ToDictionary(x=>x.Key);Deleted=deleted.ToHashSet();
            if(Tickets.Keys.Any(Deleted.Contains))throw new InvalidDataException("A source cannot be staged and deleted together.");
        }
    }
}
