using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
public class SourceDocumentTests
{
    [Fact]
    public void DirectViewOwnershipCannotBeDuplicatedOrDropItsPersistentDraft()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var key=f.Key(SourceRoot.ServerData,"Npcs.json");var doc=new SourceDocument(store.Read(key),"[]"u8.ToArray());var registry=new SourceDraftRegistry(store);var owner=registry.Register(doc,()=>{});Assert.Throws<InvalidOperationException>(()=>registry.Register(doc,()=>{}));owner.Dispose();Assert.Same(doc,Assert.Single(registry.Documents));Assert.True(registry.IsDirty);using var next=registry.Register(doc,()=>{});Assert.NotNull(registry.Capture());
    }
    [Fact]
    public void InvalidPendingInputsStayDirtyAndDiscardableWithoutBreakingNavigation()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ClientProject,"assets/terrain/fixture/collision.bin",[1]);var key=f.Key(SourceRoot.ClientProject,"assets/terrain/fixture/collision.bin");var doc=new SourceDocument(store.Read(key),[1]);var registry=new SourceDraftRegistry(store);bool invalid=true;using var owner=registry.Register(doc,()=>{if(invalid)throw new InvalidDataException("Invalid X coordinate");},()=>invalid=false);Assert.True(registry.IsDirty);var error=Assert.Throws<InvalidDataException>(()=>registry.Capture());Assert.Contains(key.RelativePath,error.Message);registry.Discard();Assert.False(registry.IsDirty);Assert.Null(registry.Capture());
    }
    [Fact]
    public void CaptureFlushesAllTabsAndAcknowledgesOnlyAppliedSubset()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);
        f.Write(SourceRoot.ServerData,"Homes.json","[]"u8.ToArray());f.Write(SourceRoot.ServerData,"Npcs.json","[]"u8.ToArray());
        var a=new SourceDocument(store.Read(f.Key(SourceRoot.ServerData,"Homes.json")),"[]"u8.ToArray());var b=new SourceDocument(store.Read(f.Key(SourceRoot.ServerData,"Npcs.json")),"[]"u8.ToArray());
        string aInput="[{}]",bInput="[{}]";var drafts=new SourceDraftRegistry(store);using var ar=drafts.Register(a,()=>a.SetText(aInput),()=>aInput=a.Text);using var br=drafts.Register(b,()=>b.SetText(bInput),()=>bInput=b.Text);
        var change=drafts.Capture()!;Assert.Equal(2,change.Files.Count);Assert.True(drafts.IsDirty);Assert.Equal("[]",File.ReadAllText(SourcePaths.Resolve(f.Profile,a.Snapshot.Key,false)));
        drafts.Acknowledge(change,new(change.OperationId,FileApplyState.Partial,[new(a.Snapshot.Key,FileApplyState.Applied),new(b.Snapshot.Key,FileApplyState.NotApplied)],[]));
        Assert.False(a.IsDirty);Assert.True(b.IsDirty);Assert.Single(drafts.Capture()!.Files);
        drafts.Discard();Assert.False(drafts.IsDirty);
        drafts.Invalidate();Assert.Throws<InvalidOperationException>(()=>drafts.Capture());
    }
    [Fact]
    public void InvalidTextRemainsAnUndoableDraftAndDeleteCanBeDiscarded()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ServerData,"Npcs.json","[]"u8.ToArray());
        var d=new SourceDocument(store.Read(f.Key(SourceRoot.ServerData,"Npcs.json")),"[]"u8.ToArray());
        d.SetText("invalid json");Assert.True(d.IsDirty);Assert.Equal("invalid json",d.Text);Assert.True(d.Undo());Assert.False(d.IsDirty);Assert.True(d.Redo());d.Discard();Assert.False(d.IsDirty);
        d.Delete();Assert.Null(d.Capture(store).After);d.Discard();Assert.False(d.IsDirty);
    }
    [Fact]
    public void DraftJsonKeepsExistingFileIdentityAfterUnsavedEdits()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ServerData,"Npcs.json","[]"u8.ToArray());var snapshot=store.Read(f.Key(SourceRoot.ServerData,"Npcs.json"));
        var d=new SourceDocument(snapshot,"[]"u8.ToArray());d.SetText("[{\"Id\":1}]");Assert.Equal(snapshot,d.Json().Snapshot);Assert.Equal(snapshot.Sha256,d.Json().Rows[0].Token.FileHash);
    }
    [Fact]
    public void HistoryRefreshesCleanOpenDocumentAndRetainsUnappliedText()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ServerData,"Npcs.json","[]"u8.ToArray());var document=new SourceDocument(store.Read(f.Key(SourceRoot.ServerData,"Npcs.json")),"[]"u8.ToArray());var registry=new SourceDraftRegistry(store);using var registration=registry.Register(document,()=>{});
        void Applied(string text){var mutation=new FileMutation(document.Snapshot,store.Blobs.Put(System.Text.Encoding.UTF8.GetBytes(text)));var change=new FileChange(Guid.NewGuid(),[mutation]);registry.Acknowledge(change,new(change.OperationId,FileApplyState.Applied,[new(mutation.Before.Key,FileApplyState.Applied)],[]));}
        Applied("[1]");Assert.Equal("[1]",document.Text);Assert.False(document.IsDirty);
        document.SetText("[2]");Applied("[3]");Assert.Equal("[2]",document.Text);Assert.True(document.IsDirty);document.Discard();Assert.Equal("[3]",document.Text);
    }
    [Fact]
    public void NewKeysCannotAdoptReplacedRootWithoutReselect()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var validators=f.Validators();var root=f.Profile.Roots[SourceRoot.ServerData];var held=root+".original";
        Assert.StartsWith(Path.GetFullPath(f.Root)+Path.DirectorySeparatorChar,Path.GetFullPath(held));Directory.Move(root,held);Directory.CreateDirectory(root);
        var fresh=f.Key(SourceRoot.ServerData,"Npcs.json");Assert.Throws<InvalidOperationException>(()=>store.Read(fresh));Assert.Throws<InvalidOperationException>(()=>validators.Codec(fresh));
    }
    [Fact]
    public void SharedDependencyDraftSurvivesItsViewAndIsCapturedOnce()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ServerData,"Npcs.json","[]"u8.ToArray());var registry=new SourceDraftRegistry(store);var key=f.Key(SourceRoot.ServerData,"Npcs.json");
        var document=registry.Open(key);Assert.Same(document,registry.Open(key));
        using(var view=registry.Register(document,()=>{},()=>{}))document.SetText("[{}]");
        Assert.True(registry.IsDirty);Assert.Single(registry.Capture()!.Files);Assert.Same(document,registry.Open(key));
    }
    [Fact]
    public void DiscardResetsAllDependenciesBeforeRebuildingViews()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);f.Write(SourceRoot.ServerData,"Npcs.json","[]"u8.ToArray());f.Write(SourceRoot.ServerData,"Homes.json","[]"u8.ToArray());var registry=new SourceDraftRegistry(store);
        var a=registry.Open(f.Key(SourceRoot.ServerData,"Npcs.json"));var b=registry.Open(f.Key(SourceRoot.ServerData,"Homes.json"));string? observed=null;
        using var view=registry.Register(a,()=>{},()=>observed=b.Text);a.SetText("[1]");b.SetText("[2]");registry.Discard();Assert.Equal("[]",observed);
    }
}
