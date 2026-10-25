using LibreKO.Editors.DataHost;
public class EquipmentTests {
 [Theory][InlineData(3,2,false)][InlineData(1,4,false)][InlineData(3,null,true)][InlineData(null,4,true)][InlineData(1,2,true)]public void TwoHandWeaponsRequireOtherHandEmpty(int? right,int? left,bool expected)=>Assert.Equal(expected,EditorDatabase.HandsCompatible((byte?)right,(byte?)left));
 [Theory][InlineData(1,7,true)][InlineData(1,5,false)][InlineData(6,3,true)][InlineData(8,3,false)][InlineData(10,6,true)][InlineData(14,5,true)]public void EquipmentUsesStorageSlotRules(int storage,byte itemSlot,bool expected)=>Assert.Equal(expected,EditorDatabase.FitsSlot(storage,itemSlot));
}
