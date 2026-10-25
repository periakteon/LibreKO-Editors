using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core;
public static class ChangeReplay {
 public static RecordChange Prepare(RecordChange original,RecordSnapshot? current,RecordSnapshot saved,bool reverse){
  if(original.Create){if(reverse)throw new InvalidOperationException("Creation reversal requires reference-aware deletion; automatic item deletion is disabled.");if(current?.Values!=null)throw new InvalidOperationException("The creation target already exists. Reconcile or choose an unused item ID.");return original with{OperationId=Guid.NewGuid()};}
  if(current?.Values==null)throw new InvalidOperationException("The record no longer exists.");
  if(current.Key!=original.Before.Key)throw new InvalidOperationException("History record identity differs.");
  var expected=reverse?saved:original.Before;
  var values=new JsonObject();
  foreach(var field in original.ChangedValues){
   if(!JsonNode.DeepEquals(current.Values[field.Key],expected.Values[field.Key]))throw new InvalidOperationException("Replay conflict in "+field.Key);
   values[field.Key]=(reverse?original.Before.Values[field.Key]:field.Value)?.DeepClone();
  }
  IReadOnlyList<InventorySlotDto>? inventory=null;
  if(original.Inventory!=null){
   if(original.Before.Inventory==null||current.Inventory==null)throw new InvalidOperationException("Inventory layout is unavailable.");
   var updated=current.Inventory.ToDictionary(s=>s.Index);
   foreach(var after in original.Inventory){
    var before=original.Before.Inventory.Single(s=>s.Index==after.Index);
    if(before==after)continue;
    var from=reverse?after:before;var to=reverse?before:after;
    if(!updated.TryGetValue(after.Index,out var actual)||actual!=from)throw new InvalidOperationException("Replay conflict in inventory slot "+after.Index);
    updated[after.Index]=to;
   }
   inventory=updated.Values.OrderBy(s=>s.Index).ToArray();
  }
  return new(Guid.NewGuid(),current,values,inventory);
 }
}
public sealed class OperationGate {
 bool active;public bool CanMutate=>!active;
 public void RequireIdle(){if(active)throw new InvalidOperationException("Wait for the current operation to finish.");}
 public IDisposable Enter(){RequireIdle();active=true;return new Lease(this);}
 sealed class Lease(OperationGate owner):IDisposable{public void Dispose(){owner.active=false;}}
}
