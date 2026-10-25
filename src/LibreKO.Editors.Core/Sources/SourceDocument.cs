using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core.Sources;
public sealed class SourceDocument
{
    byte[] original,current;bool deleted,creation;readonly Stack<(byte[] Bytes,bool Deleted)> undo=[],redo=[];
    TextDocument? encoding;
    public SourceFileSnapshot Snapshot{get;private set;}
    public bool IsDeleted=>deleted;
    public bool IsDirty=>creation||(deleted&&Snapshot.Exists)||!current.AsSpan().SequenceEqual(original);
    public event Action? Changed;
    public SourceDocument(SourceFileSnapshot snapshot,byte[] bytes)
    {Snapshot=snapshot;current=bytes.ToArray();original=snapshot.Exists?bytes.ToArray():[];creation=!snapshot.Exists;try{encoding=TextDocument.Decode(bytes);}catch(InvalidDataException){}}
    public string Text=>TextDocument.Decode(current).Text;
    public byte[] Render()=>current.ToArray();
    public JsonSourceDocument Json()=>JsonSourceDocument.OpenDraft(Snapshot,current);
    public void SetText(string text)=>EditBytes((encoding??throw new InvalidDataException("Unsupported encoding; explicitly convert a backed-up candidate before editing text.")).Encode(text));
    public void EditBytes(byte[] bytes)
    {SourceLimits.CheckedLength(bytes.LongLength);if(!deleted&&current.AsSpan().SequenceEqual(bytes))return;undo.Push((current.ToArray(),deleted));redo.Clear();current=bytes.ToArray();RefreshEncoding();deleted=false;Changed?.Invoke();}
    public void Delete(){if(deleted)return;undo.Push((current.ToArray(),deleted));redo.Clear();deleted=true;Changed?.Invoke();}
    public bool Undo(bool forward=false)
    {var from=forward?redo:undo;var to=forward?undo:redo;if(!from.TryPop(out var state))return false;to.Push((current.ToArray(),deleted));current=state.Bytes;RefreshEncoding();deleted=state.Deleted;Changed?.Invoke();return true;}
    public bool Redo()=>Undo(true);
    public void Discard(){current=original.ToArray();RefreshEncoding();creation=false;deleted=false;undo.Clear();redo.Clear();Changed?.Invoke();}
    void RefreshEncoding(){try{encoding=TextDocument.Decode(current);}catch(InvalidDataException){encoding=null;}}
    public FileMutation Capture(SourceStore store)=>new(Snapshot,deleted?null:store.Blobs.Put(current));
    internal void Acknowledge(FileMutation mutation,BlobStore blobs)
    {
        if(Snapshot!=mutation.Before)throw new InvalidOperationException("Applied evidence does not match the current draft baseline.");
        bool clean=!IsDirty;
        original=mutation.After==null?[]:blobs.Read(mutation.After);Snapshot=new(Snapshot.Key,mutation.After!=null,mutation.After?.Sha256??"",mutation.After);
        if(clean||deleted){current=original.ToArray();RefreshEncoding();deleted=false;}creation=false;undo.Clear();redo.Clear();Changed?.Invoke();
    }
}
public sealed class SourceDraftRegistry(SourceStore store)
{
    readonly List<Registration> documents=[];bool invalid;
    public event Action? Changed;public long Version{get;private set;}
    void Notify(){Version++;Changed?.Invoke();}
    void Track(Registration entry){documents.Add(entry);entry.Document.Changed+=Notify;Notify();}
    void Ensure(){if(invalid)throw new InvalidOperationException("Source draft registry belongs to an old profile generation.");}
    public IDisposable Register(SourceDocument document,Action flush,Action? reset=null)
    {
        Ensure();var existing=documents.SingleOrDefault(x=>x.Document.Snapshot.Key==document.Snapshot.Key);
        if(existing==null){existing=new(document,()=>{},null);Track(existing);}
        if(!ReferenceEquals(existing.Document,document)||existing.Owner!=Guid.Empty)throw new InvalidOperationException("This source already has an input owner.");
        var owner=Guid.NewGuid();existing.Owner=owner;existing.Flush=flush;existing.Reset=reset;
        return new Lease(()=>{if(existing.Owner==owner){existing.Owner=Guid.Empty;existing.Flush=()=>{};existing.Reset=null;}});
    }
    public SourceDocument Open(SourceFileKey key)
    {
        Ensure();var found=documents.SingleOrDefault(x=>x.Document.Snapshot.Key==key);if(found!=null)return found.Document;
        var snapshot=store.Read(key);if(!snapshot.Exists)throw new FileNotFoundException("Source dependency is missing: "+key.RelativePath);
        var document=new SourceDocument(snapshot,store.Blobs.Read(snapshot.Content!));Track(new(document,()=>{},null));return document;
    }
    public void ForgetMissing(SourceDocument document)
    {Ensure();var entry=documents.SingleOrDefault(x=>ReferenceEquals(x.Document,document));if(entry!=null&&!document.Snapshot.Exists&&!document.IsDirty&&entry.Owner==Guid.Empty){document.Changed-=Notify;documents.Remove(entry);Notify();}}
    public IReadOnlyList<SourceDocument> Documents=>documents.Select(x=>x.Document).ToArray();
    IReadOnlyList<string> FlushInputs()
    {var errors=new List<string>();foreach(var entry in documents.ToArray())try{entry.Flush();}catch(Exception e)when(e is InvalidDataException or FormatException or OverflowException or InvalidOperationException){errors.Add(entry.Document.Snapshot.Key.RelativePath+": "+e.Message);}return errors;}
    public bool IsDirty{get{Ensure();var errors=FlushInputs();return errors.Count>0||documents.Any(x=>x.Document.IsDirty);}}
    public FileChange? Capture()
    {Ensure();var errors=FlushInputs();if(errors.Count>0)throw new InvalidDataException("Fix pending source inputs before capture:\n"+string.Join("\n",errors));var files=documents.Where(x=>x.Document.IsDirty).Select(x=>x.Document.Capture(store)).ToArray();return files.Length==0?null:new(Guid.NewGuid(),files);}
    public void Discard(){Ensure();var all=documents.ToArray();foreach(var entry in all)entry.Document.Discard();foreach(var entry in all)entry.Reset?.Invoke();}
    public void Acknowledge(FileChange change,FileApplyResult result)
    {
        var reset=new List<Registration>();
        Ensure();foreach(var outcome in result.Targets.Where(x=>x.State==FileApplyState.Applied))
        {var mutation=change.Files.Single(f=>f.Before.Key==outcome.Key);var entry=documents.SingleOrDefault(x=>x.Document.Snapshot.Key==outcome.Key);if(entry!=null){entry.Document.Acknowledge(mutation,store.Blobs);reset.Add(entry);}}
        foreach(var entry in reset)entry.Reset?.Invoke();
    }
    public void Invalidate(){invalid=true;foreach(var entry in documents)entry.Document.Changed-=Notify;documents.Clear();}
    sealed class Registration(SourceDocument document,Action flush,Action? reset)
    {public SourceDocument Document{get;}=document;public Action Flush=flush;public Action? Reset=reset;public Guid Owner;}
    sealed class Lease(Action close):IDisposable{Action dispose=close;public void Dispose(){dispose();dispose=()=>{};}}
}
