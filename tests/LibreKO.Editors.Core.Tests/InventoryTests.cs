using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;

public class InventoryTests {
 static InventorySlotDto[] Slots()=>Enumerable.Range(0,96).Select(i=>new InventorySlotDto(i,0,0,0,0,0,0)).ToArray();
 static RecordDraft Draft(InventorySlotDto[] slots)=>new(new(new(RecordKind.Character,1),new(),"v",Inventory:slots));
 [Fact] public void ArrangeOnlyMovesMainBagAndPreservesMetadataWithOneUndo(){var slots=Slots();slots[6]=new(6,50,77,1,4,123,8);slots[14]=new(14,20,10,3,4,9007199254740993L,7);slots[18]=new(18,10,20,1,8,1234,9);var draft=Draft(slots);draft.ArrangeInventory(_=>1);Assert.Equal(slots[18] with{Index=14},draft.Inventory![14]);Assert.Equal(slots[14] with{Index=15},draft.Inventory[15]);Assert.Equal(slots[6],draft.Inventory[6]);Assert.Equal(slots.Skip(42),draft.Inventory.Skip(42));draft.Undo();Assert.Equal(slots,draft.Inventory);Assert.False(draft.IsDirty);}
 [Fact] public void MovePreservesBothItemsMetadataAndUndoesAsOneAction(){
  var slots=Slots();slots[14]=new(14,1,72,3,4,9007199254740993L,87);slots[15]=new(15,2,15,1,9,123456,42);
  var d=Draft(slots);d.MoveInventory(14,15,_=>5);
  Assert.Equal(slots[14] with{Index=15},d.Inventory![15]);Assert.Equal(slots[15] with{Index=14},d.Inventory[14]);
  d.Undo();Assert.Equal(slots,d.Inventory);Assert.False(d.IsDirty);d.Redo();Assert.Equal(1,d.Inventory![15].ItemId);
 }
 [Fact] public void InvalidDisplacedEquipmentLeavesDraftUnchanged(){var slots=Slots();slots[6]=new(6,1,1,1,0,0,0);slots[1]=new(1,2,1,1,0,0,0);var d=Draft(slots);Assert.Throws<InvalidOperationException>(()=>d.MoveInventory(6,1,id=>(byte)(id==1?1:7)));Assert.Equal(slots,d.Inventory);Assert.False(d.IsDirty);}
 [Fact] public void TwoHandedMoveRejectsOccupiedOtherHand(){var slots=Slots();slots[8]=new(8,1,1,1,0,0,0);slots[14]=new(14,2,1,1,0,0,0);var d=Draft(slots);Assert.Throws<InvalidOperationException>(()=>d.MoveInventory(14,6,id=>(byte)(id==1?2:3)));Assert.Equal(slots,d.Inventory);}
 [Fact] public void BagCannotBeRemovedWhileItsContentsRemain(){var slots=Slots();slots[57]=new(57,1,1,1,0,0,0);slots[60]=new(60,2,1,1,0,0,0);Assert.Throws<InvalidOperationException>(()=>Draft(slots).MoveInventory(57,14,id=>(byte)(id==1?25:5)));}
 [Theory][InlineData(42,110,true)][InlineData(43,107,true)][InlineData(44,100,true)][InlineData(45,100,true)][InlineData(46,105,true)][InlineData(47,114,true)][InlineData(49,111,true)][InlineData(50,127,true)][InlineData(51,113,true)][InlineData(57,25,true)][InlineData(57,5,false)][InlineData(48,5,false)]
 public void CostumeAndBagSlotsUseGameRules(int storage,int code,bool expected)=>Assert.Equal(expected,InventoryLayout.FitsSlot(storage,(byte)code));
}
