using LibreKO.Editors.Contracts;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Game.World;
namespace LibreKO.Editors.DataHost;
public static class InventoryAdapter {
 public static IReadOnlyList<InventorySlotDto> Decode(byte[] bytes){if(!LibreKO.Editors.Core.CompatibilityRules.SupportsInventory(bytes.Length))throw new InvalidDataException("Unknown inventory layout. Inventory editing is disabled to preserve it.");var slots=Enumerable.Range(0,96).Select(_=>new ItemSlot()).ToArray();UserSessionBinaryState.LoadItems(slots,bytes);return slots.Select((s,i)=>new InventorySlotDto(i,s.ItemId,s.Durability,s.Count,s.Flag,s.ExpiresAt,s.UniqueId)).ToArray();}
 public static byte[] Encode(IReadOnlyList<InventorySlotDto> values){if(values.Count!=96||!values.Select(x=>x.Index).Order().SequenceEqual(Enumerable.Range(0,96)))throw new InvalidDataException("Inventory must contain each of the 96 storage slots once.");var slots=values.OrderBy(s=>s.Index).Select(s=>new ItemSlot{ItemId=s.ItemId,Durability=s.Durability,Count=s.Count,Flag=s.Flag,ExpiresAt=s.ExpiresAt,UniqueId=s.UniqueId}).ToArray();return UserSessionBinaryState.SerializeItems(slots);}
}

