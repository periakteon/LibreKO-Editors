using Godot;
using System.Reflection;
namespace LibreKO.Editors.Desktop;

// Installed packs can predate the referenced game assembly. Cache its matching
// renderer resources before mounting those packs; assets remain read-only.
public static class PreviewEffects {
 static readonly List<Resource> RendererResources=new();
 static readonly MethodInfo Attach=typeof(LibreKO.CharacterPreview).Assembly.GetType("LibreKO.WeaponGlow")!.GetMethod("Attach",BindingFlags.Static|BindingFlags.NonPublic)!;
 static readonly Type Catalog=typeof(LibreKO.CharacterPreview).Assembly.GetType("LibreKO.WeaponCatalog")!;
 public static Node3D? Weapon(int itemId){
  if(LibreKO.Domain.ItemData.Get(itemId) is not {} definition||definition.Kind is not (11 or 21 or 22 or 31 or 32 or 41 or 42 or 51 or 52 or 60 or 61 or 62 or 63 or 70 or 71 or 80 or 100 or 110 or 140 or 181))return null;
  var id=(int)Catalog.GetMethod("ResolveBaseId",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[itemId])!;
  var index=(System.Collections.IDictionary)Catalog.GetProperty("Index",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
  if(index[id] is not {} info)return null;var stem=(string)info.GetType().GetField("Stem")!.GetValue(info)!;
  var model=ResourceLoader.Load<PackedScene>("res://assets/items/weapon/"+stem+".glb")?.Instantiate<Node3D>();if(model!=null)AttachWeapon(model,itemId);return model;
 }
 public static void WarmShaders(){
  if(RendererResources.Count>0)return;
  using var dir=DirAccess.Open("res://shaders");
  foreach(var file in dir!.GetFiles().Where(x=>x.EndsWith(".gdshaderinc")))RendererResources.Add(ResourceLoader.Load<ShaderInclude>("res://shaders/"+file));
  foreach(var file in dir.GetFiles().Where(x=>x.EndsWith(".gdshader")))RendererResources.Add(ResourceLoader.Load<Shader>("res://shaders/"+file));
 }
 public static void AttachWeapon(Node3D model,int itemId){
  // Reuse the game's catalogue-driven unique/elemental glow rules, including
  // extension-specific variants. This is the only bridge to its internal API.
  Attach.Invoke(null,[model,itemId]);LibreKO.ItemShine.Apply(model,itemId,0);
 }
 public static bool IsEffect(Node node){for(Node? p=node;p!=null;p=p.GetParent()){if(p is LibreKO.FxWeaponGlow||p.GetType().Namespace=="LibreKO"&&p.GetType().Name.StartsWith("Fx"))return true;}return false;}
}
