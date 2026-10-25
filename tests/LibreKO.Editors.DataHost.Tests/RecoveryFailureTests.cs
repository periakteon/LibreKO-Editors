using LibreKO.Editors.Core;
using LibreKO.Editors.DataHost;
using LibreKO.Editors.Contracts;
public class RecoveryFailureTests{
 [Fact]public async Task HistoryFailureAfterCommitRetainsReconciliationJournal(){await FixtureRows.EnsureItem();const string cs="Server=127.0.0.1;Port=33307;Database=libreko_editor_test_native;User=root;Pooling=false;";var root=Path.Combine(Path.GetTempPath(),"editor-history-fault-"+Guid.NewGuid());Directory.CreateDirectory(root);File.WriteAllText(Path.Combine(root,"changes"),"injected publication failure");var db=new EditorDatabase(cs,root);var row=(await db.GetAsync(new(RecordKind.Item,123450001)))!;var change=new RecordChange(Guid.NewGuid(),row,new(){["Damage"]=row.Values["Damage"]!.GetValue<int>()+1});var result=await db.SaveAsync(change);Assert.Equal(SaveStatus.Saved,result.Status);Assert.Contains("recovery",result.Message);var pending=Assert.Single(new OperationJournal(root).Pending());Assert.Equal(RecoveryDisposition.Applied,RecoveryResolver.Resolve(pending,(await db.GetAsync(row.Key))!));}
}
