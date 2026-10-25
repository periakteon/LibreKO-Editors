using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core;

public static class InventoryLayout {
 public const int Count=96;
 static readonly string[] Equipment=["Right ear","Helmet","Left ear","Necklace","Chest","Pet","Right hand","Belt","Left hand","Right ring","Legs","Left ring","Gloves","Boots"];
 static readonly string[] Costume=["Wings","Costume helmet","Right glove effect","Left glove effect","Costume chest","Emblem","Reserved costume","Fairy","Tattoo","Talisman","Reserved costume","Reserved costume","Reserved costume","Reserved costume","Reserved costume"];
 public static string Name(int index)=>index switch{>=0 and <14=>Equipment[index],<42=>$"Inventory {index-13}",<57=>Costume[index-42],<60=>$"Bag {index-56}",<96=>$"Bag {(index-60)/12+1} · {(index-60)%12+1}",_=>"Unknown slot"};
 public static bool FitsSlot(int storage,byte itemSlot)=>storage switch{
  0 or 2=>itemSlot==10,1=>itemSlot==7,3=>itemSlot==11,4=>itemSlot==5,5=>itemSlot==20,6=>itemSlot is 0 or 1 or 3,7=>itemSlot==14,8=>itemSlot is 0 or 2 or 4,9 or 11=>itemSlot==12,10=>itemSlot==6,12=>itemSlot==8,13=>itemSlot==9,
  >=14 and <42=>true,42=>itemSlot==110,43=>itemSlot==107,44=>itemSlot is 100 or 101,45=>itemSlot is 100 or 102,46=>itemSlot==105,47=>itemSlot==114,49=>itemSlot==111,50=>itemSlot is 112 or 127,51=>itemSlot==113,>=57 and <60=>itemSlot==25,>=60 and <96=>true,_=>false};
 public static void RequireShape(IReadOnlyList<InventorySlotDto> slots){if(slots.Count!=Count||!slots.Select(s=>s.Index).Order().SequenceEqual(Enumerable.Range(0,Count)))throw new InvalidOperationException("Inventory must contain all 96 distinct positions.");}
 public static void Validate(IReadOnlyList<InventorySlotDto> slots,Func<int,byte?> itemSlot){
  RequireShape(slots);var rows=slots.ToDictionary(s=>s.Index);
  foreach(var row in slots.Where(s=>s.ItemId!=0)){
   if(row.ItemId<0||row.Count is 0 or >9999)throw new InvalidOperationException("An item requires a positive ID and a count between 1 and 9999.");
   var code=itemSlot(row.ItemId)??throw new InvalidOperationException($"Item {row.ItemId} is missing from the database.");
   if(!FitsSlot(row.Index,code))throw new InvalidOperationException($"Item {row.ItemId} does not fit {Name(row.Index)}.");
   if(row.Index>=60&&rows[57+(row.Index-60)/12].ItemId==0)throw new InvalidOperationException("Empty this bag's contents before removing its bag item.");
  }
  byte? right=rows[6].ItemId==0?null:itemSlot(rows[6].ItemId),left=rows[8].ItemId==0?null:itemSlot(rows[8].ItemId);
  if(right==3&&left!=null||left==4&&right!=null)throw new InvalidOperationException("A two-handed weapon requires the other hand to be empty.");
 }
 public static InventorySlotDto Empty(int index)=>new(index,0,0,0,0,0,0);
}
