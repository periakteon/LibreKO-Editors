using Godot;
namespace LibreKO.Editors.Desktop;
public static class StudioLayoutChecks {
 public static void Verify(EditorApp app){
  var screen=app.GetViewportRect();
  foreach(var button in app.FindChildren("*","Button",true,false).OfType<Button>().Where(b=>b.IsVisibleInTree())){
   bool scroll=false;for(Node? parent=button.GetParent();parent!=null&&parent!=app;parent=parent.GetParent())if(parent is ScrollContainer||parent is Window){scroll=true;break;}
   if(scroll)continue;var rect=button.GetGlobalRect();if(rect.Position.X<0||rect.End.X>screen.End.X+1||rect.Position.Y<0||rect.End.Y>screen.End.Y+1)throw new Exception("An editor action is clipped at "+screen.Size+": "+button.Text);
  }
  if(app.List.Size.X<200)throw new Exception("The record browser became too narrow.");
  if(app.Module!=2&&(app.Preview.Size.X<300||app.Preview.Size.Y<300))throw new Exception("The model preview became too small.");
  if(app.Module==1){var add=app.InventoryView!.FindChildren("*","Button",true,false).OfType<Button>().First(b=>b.Text.Contains("Add item")||b.Text.Contains("Replace item"));if(add.GetGlobalRect().End.Y>screen.End.Y-24)throw new Exception("Inventory actions are outside the visible inspector.");}
 }
}
