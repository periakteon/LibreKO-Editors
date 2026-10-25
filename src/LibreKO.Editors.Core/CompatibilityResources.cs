using System.Text.Json.Nodes;
namespace LibreKO.Editors.Core;
public record CompatibilityResourceReference(string Path,string Kind,int? ItemId=null);
public static class CompatibilityResources {
 public static readonly string[] RequiredCatalogs=["assets/items/items.json","assets/characters/index.json","assets/characters/faces.json","assets/items/weapon/index.json","assets/items/armor/index.json"];
 public static readonly string[] OptionalCatalogs=["assets/items/weapon/glow.json","assets/items/weapon/visual_aliases.json","assets/wings/index.json","assets/fx/ids.json"];
 public static IEnumerable<CompatibilityResourceReference> References(string file,JsonObject document){
  if(file=="assets/items/weapon/index.json"){foreach(var entry in document){if(entry.Value is JsonObject item&&item["stem"]?.GetValue<string>() is {Length:>0} stem)yield return new("res://assets/items/weapon/"+stem+".glb","weapon model",int.TryParse(entry.Key,out var id)?id:null);}}
  else if(file=="assets/characters/index.json"){foreach(var entry in document){if(entry.Value?.GetValue<string>() is {Length:>0} stem)yield return new("res://assets/characters/"+stem+".glb","character model");}}
  else if(file=="assets/characters/faces.json"&&document["races"] is JsonObject races){foreach(var race in races)if(race.Value is JsonObject parts)foreach(var key in new[]{"facePart","hairPart"})if(parts[key]?.GetValue<string>() is {Length:>0} prefix)yield return new("res://assets/characters/"+prefix+"00.glb","default face/hair model");}
  else if(file=="assets/wings/index.json"){foreach(var entry in document){if(entry.Value is JsonObject item&&item["model"]?.GetValue<string>() is {Length:>0} stem)yield return new("res://assets/wings/"+stem+".glb","wing/costume model",int.TryParse(entry.Key,out var id)?id:null);}}
  else if(file=="assets/items/weapon/glow.json"){foreach(var section in new[]{"cats","tails"})if(document[section] is JsonObject categories)foreach(var category in categories)if(category.Value is JsonObject variants)foreach(var variant in variants)if(variant.Value?.GetValue<string>() is {Length:>0} name)yield return Effect(name);}
  else if(file=="assets/fx/ids.json"){foreach(var entry in document){if(entry.Value?.GetValue<string>() is {Length:>0} name)yield return Effect(name);}}
  else if(file.StartsWith("assets/fx/",StringComparison.Ordinal)){
   foreach(var reference in Walk(document))yield return reference;
  }
 }
 static CompatibilityResourceReference Effect(string name)=>new("res://assets/fx/"+name.ToLowerInvariant()+".json","effect descriptor");
 static IEnumerable<CompatibilityResourceReference> Walk(JsonNode node){
  if(node is JsonObject obj)foreach(var property in obj){
   if(property.Key is "tex" or "texs"&&property.Value is JsonArray textures){foreach(var texture in textures)if(texture is JsonValue value&&value.TryGetValue<string>(out var stem)&&stem.Length>0)yield return new("res://assets/fx/tex/"+stem+".png","effect texture");}
   else if(property.Key=="tex"&&property.Value is JsonValue texture&&texture.TryGetValue<string>(out var textureStem)&&textureStem.Length>0)yield return new("res://assets/fx/tex/"+textureStem+".png","effect mesh texture");
   else if(property.Key is "meshRef" or "emitterRef"&&property.Value is JsonValue value&&value.TryGetValue<string>(out var stem)&&stem.Length>0)yield return new("res://assets/fx/mesh/"+stem+".json","effect mesh descriptor");
   else if(property.Value!=null)foreach(var child in Walk(property.Value))yield return child;
  }
  else if(node is JsonArray array)foreach(var child in array)if(child!=null)foreach(var reference in Walk(child))yield return reference;
 }
}
