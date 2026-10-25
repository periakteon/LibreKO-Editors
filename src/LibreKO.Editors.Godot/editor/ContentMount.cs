using Godot;
using System.Text.Json.Nodes;
namespace LibreKO.Editors.Desktop;
public static class ContentMount {
 public static readonly string[] BaseNames=["knightonline.pck","content/terrain.pck","content/characters.pck","content/armor.pck","content/weapons.pck","content/npcs.pck","content/objects.pck"];
 public static string[] Patches(string root)=>System.IO.Directory.Exists(root)?System.IO.Directory.GetFiles(root,"*.pck").OrderBy(System.IO.Path.GetFileName,StringComparer.CurrentCulture).ToArray():[];
 public static void Mount(string root,IEnumerable<string>? patches=null){
  if(!System.IO.File.Exists(System.IO.Path.Combine(root,"knightonline.pck")))throw new InvalidDataException("Select the packed base build containing knightonline.pck.");
  PreviewEffects.WarmShaders();
  foreach(var file in BaseNames){var path=System.IO.Path.Combine(root,file);if(System.IO.File.Exists(path)&&!ProjectSettings.LoadResourcePack(path))throw new InvalidDataException("Cannot mount "+file);}
  foreach(var path in patches??[])if(!ProjectSettings.LoadResourcePack(path))throw new InvalidDataException("Cannot mount patch "+path);
  foreach(var path in LibreKO.Editors.Core.CompatibilityResources.RequiredCatalogs)_=Json(path);
  var loader=new LibreKO.Content.GameContent();loader._EnterTree();loader.Free();
 }
 public static JsonObject Json(string path){var text=Godot.FileAccess.GetFileAsString("res://"+path);if(string.IsNullOrEmpty(text))throw new InvalidDataException("Missing content: "+path);return JsonNode.Parse(text)!.AsObject();}
}
