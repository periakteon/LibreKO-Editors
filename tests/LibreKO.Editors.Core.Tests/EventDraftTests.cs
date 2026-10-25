using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Events;
using System.Text.Json.Nodes;
public class EventDraftTests {
 [Fact] public void CompoundKeysAndAtomicDraftUndo(){var key=new EventRowKey(EventTable.GameEvents,7,1);Assert.NotEqual(key,new(EventTable.GameEvents,7,2));var row=new EventRowSnapshot(key,new(){["ZoneNum"]=1,["EventNum"]=7,["Type"]=99,["Exec1"]=1},"v");var draft=new EventDraft(new(new(EventFamily.MapEvent,7,1),[row],"v"));draft.Set(key,"Exec1",2);Assert.Single(draft.BuildChange(Guid.NewGuid()).Rows);Assert.Equal("99",draft.Rows.Single().Values["Type"]!.ToString());draft.Undo();Assert.False(draft.IsDirty);draft.Redo();Assert.True(draft.IsDirty);Assert.Throws<InvalidOperationException>(()=>draft.Set(key,"ZoneNum",2));draft.Remove(key);draft.Undo();Assert.Single(draft.Rows);draft.Set(key,"Exec1",2);Assert.Single(draft.BuildChange(Guid.NewGuid()).Rows);Assert.Equal("1",row.Values["Exec1"]!.ToString());}
 [Fact] public void AddedRowsUndoAsOneAction(){var draft=new EventDraft(new(new(EventFamily.Lottery,3),[],"missing"));var key=new EventRowKey(EventTable.LotteryEvents,3);draft.Add(key,new(){["Id"]=3,["Name"]="Lottery"});Assert.True(draft.IsDirty);draft.Undo();Assert.False(draft.IsDirty);draft.Redo();Assert.Equal("Lottery",draft.Rows.Single().Values["Name"]!.ToString());}
}
