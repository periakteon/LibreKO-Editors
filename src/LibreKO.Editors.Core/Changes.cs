using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Core;
public sealed class RecordDraft(RecordSnapshot snapshot) {
 readonly Stack<(JsonObject Values,IReadOnlyList<InventorySlotDto>? Slots)> undo=new(),redo=new();
 public RecordSnapshot Original=>snapshot;
 public IReadOnlyList<InventorySlotDto>? Inventory {get;private set;}=snapshot.Inventory?.ToArray();
 void Checkpoint(){undo.Push(((JsonObject)Values.DeepClone(),Inventory?.ToArray()));redo.Clear();}
 public void SetInventory(IReadOnlyList<InventorySlotDto> slots){InventoryLayout.RequireShape(slots);if(Inventory==null)throw new InvalidOperationException("Unknown inventory layout.");var ordered=slots.OrderBy(s=>s.Index).ToArray();if(Inventory.SequenceEqual(ordered))return;Checkpoint();Inventory=ordered;}
 public void MoveInventory(int source,int target,Func<int,byte?> itemSlot){
  if(Inventory==null)throw new InvalidOperationException("Unknown inventory layout.");if(source==target)return;
  var next=Inventory.OrderBy(s=>s.Index).ToArray();InventoryLayout.RequireShape(next);
  if(source is <0 or >=96||target is <0 or >=96)throw new InvalidOperationException("Unknown inventory position.");
  if(next[source].ItemId==0)throw new InvalidOperationException("The source slot is empty.");
  (next[source],next[target])=(next[target] with{Index=source},next[source] with{Index=target});
  InventoryLayout.Validate(next,itemSlot);SetInventory(next);
 }
 public void ArrangeInventory(Func<int,byte?> itemSlot){
  if(Inventory==null)throw new InvalidOperationException("Unknown inventory layout.");InventoryLayout.RequireShape(Inventory);
  var next=Inventory.OrderBy(s=>s.Index).ToArray();var main=next.Skip(14).Take(28).OrderBy(s=>s.ItemId==0?1:0).ThenBy(s=>s.ItemId).ToArray();
  for(int i=0;i<main.Length;i++)next[14+i]=main[i] with{Index=14+i};InventoryLayout.Validate(next,itemSlot);SetInventory(next);
 }
 public void SetInventorySlot(InventorySlotDto slot){if(Inventory==null)throw new InvalidOperationException("Unknown inventory layout.");var list=Inventory.ToArray();var index=Array.FindIndex(list,s=>s.Index==slot.Index);if(index<0)throw new InvalidOperationException("Unknown inventory slot.");if(list[index]==slot)return;Checkpoint();list[index]=slot;Inventory=list;}
 public JsonObject Values {get;private set;}=(JsonObject)snapshot.Values.DeepClone();
 public void Set(string field,JsonNode? value){if(JsonNode.DeepEquals(Values[field],value))return;Checkpoint();Values[field]=value?.DeepClone();}
 public void Undo(){if(undo.TryPop(out var v)){redo.Push((Values,Inventory));Values=v.Values;Inventory=v.Slots;}}
 public void Redo(){if(redo.TryPop(out var v)){undo.Push((Values,Inventory));Values=v.Values;Inventory=v.Slots;}}
 public RecordChange BuildChange(Guid id){var changes=new JsonObject();foreach(var p in Values)if(!JsonNode.DeepEquals(snapshot.Values[p.Key],p.Value))changes[p.Key]=p.Value?.DeepClone();var slots=Inventory!=null&&snapshot.Inventory!=null&&!Inventory.SequenceEqual(snapshot.Inventory)?Inventory:null;return new(id,snapshot,changes,slots);}
 public bool IsDirty {get{var c=BuildChange(Guid.Empty);return c.ChangedValues.Count>0||c.Inventory!=null;}}
}
public static class RecordFingerprint {
 static JsonNode? Sort(JsonNode? n)=>n switch {JsonObject o=>new JsonObject(o.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>new KeyValuePair<string,JsonNode?>(p.Key,Sort(p.Value)))),JsonArray a=>new JsonArray(a.Select(Sort).ToArray()),_=>n?.DeepClone()};
 public static string Compute(JsonObject values)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sort(values)!.ToJsonString())));
}
public enum RecoveryDisposition {NotApplied,Applied,Conflict}
public static class RecoveryResolver {
 public static RecoveryDisposition Resolve(RecordChange c,RecordSnapshot? row) {
  if(row?.Values==null)return c.Create?RecoveryDisposition.NotApplied:RecoveryDisposition.Conflict;
  bool before=true,after=true;
  foreach(var p in c.ChangedValues){before&=JsonNode.DeepEquals(row.Values[p.Key],c.Before.Values[p.Key]);after&=JsonNode.DeepEquals(row.Values[p.Key],p.Value);}
  if(c.Inventory!=null){before&=JsonSerializer.Serialize(row.Inventory)==JsonSerializer.Serialize(c.Before.Inventory);after&=JsonSerializer.Serialize(row.Inventory)==JsonSerializer.Serialize(c.Inventory);}
  return after?RecoveryDisposition.Applied:before?RecoveryDisposition.NotApplied:RecoveryDisposition.Conflict;
 }
}
public static class FieldValueParser {
 public static int ReadInt32(JsonNode? value)=>int.Parse(value?.ToString()??"0",NumberStyles.Integer,CultureInfo.InvariantCulture);
 public static JsonNode? Parse(FieldDescriptor field,string value) {
  if(field.Kind==FieldKind.Text)return JsonValue.Create(value);
  if(field.Kind==FieldKind.Boolean)return JsonValue.Create(bool.Parse(value));
  if(field.Kind==FieldKind.DateOnly){if(string.IsNullOrWhiteSpace(value))return null;if(!DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var date)||date.Year<1000)throw new FormatException("Enter a calendar date as yyyy-MM-dd, or clear a nullable date.");return JsonValue.Create(date.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture));}
  if(field.Kind==FieldKind.DateTime){if(string.IsNullOrWhiteSpace(value))return null;if(!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var date)||date.Year<1000)throw new FormatException("Enter a UTC date such as 2030-01-02 03:04:05, or clear a nullable expiry.");return JsonValue.Create(date.UtcDateTime.ToString("O",CultureInfo.InvariantCulture));}
  if(field.Kind==FieldKind.Decimal){if(!decimal.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out var d))throw new FormatException("Enter a finite number.");if(field.Minimum is {} min&&d<min||field.Maximum is {} max&&d>max)throw new FormatException("Value is outside the supported range.");return JsonValue.Create(d);}
  if(!long.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var n))throw new FormatException("Enter a whole number.");
  if(field.Minimum is {} lo&&n<lo||field.Maximum is {} hi&&n>hi)throw new FormatException("Value is outside the supported range.");return JsonValue.Create(n);
 }
}
public sealed class OperationJournal(string root) {
 public void Abort(Guid id){var path=Path.Combine(root,"journal",id+".pending.json");if(File.Exists(path))File.Delete(path);}
 public void Prepare(RecordChange c)=>WorkspacePaths.AtomicWrite(Path.Combine(root,"journal",c.OperationId+".pending.json"),JsonSerializer.Serialize(c,JsonConfig.Options));
 public void Commit(RecordChange c,RecordSnapshot row){WorkspacePaths.AtomicWrite(Path.Combine(root,"changes",c.OperationId+".json"),JsonSerializer.Serialize(new{change=c,result=row},JsonConfig.Options));File.Move(Path.Combine(root,"journal",c.OperationId+".pending.json"),Path.Combine(root,"journal",c.OperationId+".committed.json"),true);}
 public IReadOnlyList<RecordChange> Pending()=>System.IO.Directory.Exists(Path.Combine(root,"journal"))?System.IO.Directory.GetFiles(Path.Combine(root,"journal"),"*.pending.json").Select(p=>JsonSerializer.Deserialize<RecordChange>(File.ReadAllText(p),JsonConfig.Options)!).ToArray():[];
}
