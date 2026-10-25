using Godot;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using ClientItems=LibreKO.Domain.ItemData;
namespace LibreKO.Editors.Desktop;
public sealed class ClientCompatibilityAudit(CompatibilityReportBuilder report){
 readonly CompatibilityResourceAudit resources=new(path=>ResourceLoader.Exists(path),report);
 readonly HashSet<string> documents=new(StringComparer.Ordinal),queued=new(StringComparer.Ordinal),dependencies=new(StringComparer.Ordinal);readonly Queue<string> pending=new();
 public int Checked=>resources.Checked;
 public CompatibilityItem? Item(int id){
  var definition=ClientItems.Get(id);if(definition==null)return null;var extension=ClientItems.ExtFor(id);if(definition.Id!=id&&extension==null)return null;
  return new(id,ClientItems.DisplayName(id),definition.Kind,definition.Slot,definition.Cat,(definition.Damage>0?definition.Damage:definition.Value)+(extension?.BonusDamage??0),(definition.Ac>0?definition.Ac:definition.Value)+(extension?.BonusAc??0),ClientItems.MaxDurability(definition,extension));
 }
 public IEnumerable<int> ClientIds(){
  foreach(var item in ClientItems.All()){
   yield return item.Id;
   foreach(var extension in ClientItems.ExtsForCat(item.Cat))if(ClientItems.ExtAppliesTo(item,extension)&&CompatibilityRules.VariantId(item.Id,extension.Id) is {} id&&ClientItems.Get(id)?.Id==item.Id&&ClientItems.ExtFor(id)==extension)yield return id;
  }
  foreach(var extension in ClientItems.AllExts())if(extension.Linked>0&&ClientItems.Get(extension.Linked)!=null&&ClientItems.ExtFor(extension.Linked)==extension)yield return extension.Linked;
 }
 public void Icon(int id){var icon=ClientItems.IconId(id);if(icon>0)resources.Require($"res://assets/items/icons/{icon}.png","item icon",new(RecordKind.Item,id));else report.Add(new("asset.icon-unmapped",CompatibilitySeverity.Warning,"Assets",id.ToString(),"No client icon mapping for this item.",new(RecordKind.Item,id)));}
 void Enqueue(string file){if(queued.Add(file))pending.Enqueue(file);}
 public void Begin(){foreach(var file in CompatibilityResources.RequiredCatalogs.Concat(CompatibilityResources.OptionalCatalogs))Enqueue(file);}
 public bool HasDocuments=>pending.Count>0;
 public void NextDocument(){
  var file=pending.Dequeue();if(!documents.Add(file))return;
  if(!resources.Require("res://"+file,"catalog/effect descriptor"))return;
  try{
   var json=ContentMount.Json(file);
   foreach(var reference in CompatibilityResources.References(file,json)){
    if(resources.Require(reference.Path,reference.Kind,reference.ItemId is {} id?new(RecordKind.Item,id):null)){
     if(reference.Path.StartsWith("res://assets/fx/",StringComparison.Ordinal)&&reference.Path.EndsWith(".json",StringComparison.Ordinal))Enqueue(reference.Path[6..]);
     if(reference.Path.EndsWith(".glb",StringComparison.Ordinal))ModelDependencies(reference.Path);
    }
   }
   if(file=="assets/items/armor/index.json"&&json["items"] is JsonObject armor)foreach(var item in armor)Armor(item.Key,item.Value);
  }catch(Exception e){report.Add(new("catalog.format",CompatibilitySeverity.Warning,"Assets",file,"Cannot interpret this catalog with the bundled adapter: "+e.Message));}
 }
 void Armor(string key,JsonNode? value){
  if(!int.TryParse(key,out var id)||value==null||!int.TryParse(value.ToString(),out var resource)||resource==0)return;
  var definition=ClientItems.Get(id);var prefix=LibreKO.Domain.CostumeLook.PartPrefix(id,definition?.SaleType??0);
  foreach(var race in new[]{1,2,3,4,6,11,12,13,14}){
   var candidates=LibreKO.Domain.CostumeLook.PartStems(prefix,resource,race).Select(stem=>"res://assets/items/armor/"+stem+".glb");
   if(resources.Resolve(candidates,$"armor model for preview race {race} (all fallbacks absent)",new(RecordKind.Item,id)) is {} selected)ModelDependencies(selected);
  }
 }
 void ModelDependencies(string path){
  if(!dependencies.Add(path))return;
  foreach(var reference in ResourceLoader.GetDependencies(path)){
   var child=reference.Split("::",StringSplitOptions.None)[^1];
   if(child.StartsWith("res://assets/",StringComparison.Ordinal)&&resources.Require(child,"model texture/material dependency"))ModelDependencies(child);
  }
 }
}
