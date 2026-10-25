using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.DataHost;
public class JournalTests {
 [Fact]public async Task DefinitivelyRejectedSaveDoesNotLeavePendingCommit(){await FixtureRows.EnsureItem();const string cs="Server=127.0.0.1;Port=33307;Database=libreko_editor_test_native;User=root;Pooling=false;";var root=Path.Combine(Path.GetTempPath(),"editor-reject-"+Guid.NewGuid());var db=new EditorDatabase(cs,root);var row=await db.GetAsync(new(RecordKind.Item,123450001));Assert.NotNull(row);var result=await db.SaveAsync(new(Guid.NewGuid(),row! with{Version="stale"},new(){["Damage"]=50}));Assert.Equal(SaveStatus.Conflict,result.Status);Assert.Empty(new OperationJournal(root).Pending());}
}
