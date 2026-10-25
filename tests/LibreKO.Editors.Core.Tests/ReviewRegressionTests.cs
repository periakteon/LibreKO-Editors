using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
public class ReviewRegressionTests {
 static InventorySlotDto Slot(int i,int id)=>new(i,id,0,(ushort)(id==0?0:1),0,0,0);
 static RecordSnapshot Player(params InventorySlotDto[] slots)=>new(new(RecordKind.Character,1),new(){["Money"]=10,["Name"]="Player"},"v",Inventory:slots);
 [Fact] public void ScalarReversalNeverIncludesInventory(){var before=Player(Slot(0,1));var change=new RecordChange(Guid.NewGuid(),before,new(){["Money"]=20});var saved=before with{Values=new(){["Money"]=20}};var current=saved with{Inventory=[Slot(0,99)]};var reverse=ChangeReplay.Prepare(change,current,saved,true);Assert.Null(reverse.Inventory);Assert.Equal(10,reverse.ChangedValues["Money"]!.GetValue<int>());}
 [Fact] public void InventoryReversalPreservesUnrelatedLaterSlots(){var before=Player(Slot(0,1),Slot(1,2));var change=new RecordChange(Guid.NewGuid(),before,new(),[Slot(0,3),Slot(1,2)]);var saved=before with{Inventory=change.Inventory};var current=saved with{Inventory=[Slot(0,3),Slot(1,9)]};var reverse=ChangeReplay.Prepare(change,current,saved,true);Assert.Equal(1,reverse.Inventory![0].ItemId);Assert.Equal(9,reverse.Inventory![1].ItemId);}
 [Fact] public void InventoryReplayRejectsChangedTargetSlot(){var before=Player(Slot(0,1));var change=new RecordChange(Guid.NewGuid(),before,new(),[Slot(0,3)]);Assert.Throws<InvalidOperationException>(()=>ChangeReplay.Prepare(change,before with{Inventory=[Slot(0,9)]},before with{Inventory=change.Inventory},false));}
 [Fact] public void EquipmentUndoRedoTracksDirty(){var draft=new RecordDraft(Player(Slot(0,1)));draft.SetInventorySlot(Slot(0,2));Assert.True(draft.IsDirty);draft.Undo();Assert.Equal(1,draft.Inventory![0].ItemId);Assert.False(draft.IsDirty);draft.Redo();Assert.Equal(2,draft.Inventory![0].ItemId);}
 [Fact] public void UiOperationGateRejectsMutationDuringAsyncWork(){var gate=new OperationGate();using(gate.Enter()){Assert.False(gate.CanMutate);Assert.Throws<InvalidOperationException>(()=>gate.RequireIdle());}Assert.True(gate.CanMutate);}
}
