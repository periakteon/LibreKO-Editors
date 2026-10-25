using LibreKO.Editors.Core;
public class HistoryScopeTests {
 [Fact] public void DifferentDatabasesCannotReadEachOthersJournal(){var root=Path.Combine(Path.GetTempPath(),"history-"+Guid.NewGuid());var first=DatabaseHistory.DirectoryFor(root,"server:3306/first");var second=DatabaseHistory.DirectoryFor(root,"server:3306/second");new OperationJournal(first).Prepare(new(Guid.NewGuid(),new(new(LibreKO.Editors.Contracts.RecordKind.Item,1),new(),"v"),new(){["Damage"]=2}));Assert.Empty(new OperationJournal(second).Pending());Assert.Single(new OperationJournal(DatabaseHistory.DirectoryFor(root,"server:3306/first")).Pending());}
}
