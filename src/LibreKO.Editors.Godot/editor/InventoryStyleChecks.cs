using Godot;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;

public static class InventoryStyleChecks {
 public static async Task Verify(EditorApp app){
  await app.PropertiesReady;var panel=app.InventoryView??throw new Exception("No player inventory.");
  var cells=panel.FindChildren("*","",true,false).OfType<LibreKO.ItemSlotView>().ToDictionary(x=>x.Index);
  if(cells.Count!=96)throw new Exception("Inventory must reuse the client's slot renderer for all 96 positions.");
  if(cells[1].GlobalPosition.Y>=cells[4].GlobalPosition.Y||cells[6].GlobalPosition.X>=cells[8].GlobalPosition.X||cells[1].GlobalPosition.Y>=cells[6].GlobalPosition.Y)throw new Exception("Equipment does not use the client paperdoll positions.");
  if(cells[14].GlobalPosition.Y<=cells[6].GlobalPosition.Y||Math.Abs(cells[14].GlobalPosition.Y-cells[20].GlobalPosition.Y)>1||cells[21].GlobalPosition.Y<=cells[14].GlobalPosition.Y)throw new Exception("The bag must have seven compact columns below equipment.");
  if(cells[14].Size.X>44||Math.Abs(cells[14].Size.X-cells[14].Size.Y)>1)throw new Exception("Bag slots must be compact squares.");
  var trash=panel.FindChildren("*","",true,false).OfType<InventoryTrash>().Single();if(app.GetViewportRect().Size.Y>=900&&trash.GetGlobalRect().End.Y>app.GetViewportRect().Size.Y-24)throw new Exception("The inventory footer and trash must fit the normal editor window.");
  if(app.GetViewportRect().Size.Y>=900)for(Node? ancestor=trash.GetParent();ancestor!=null;ancestor=ancestor.GetParent())if(ancestor is ScrollContainer scroll&&trash.GetGlobalRect().End.Y>scroll.GetGlobalRect().End.Y-2)throw new Exception("The inventory footer is clipped by the inspector scroll panel.");
  foreach(var slot in new[]{0,1,2,3,4,7,9,10,11,12,13})if(LibreKO.UiIcons.Equipment(slot)==null)throw new Exception("Missing client equipment silhouette for "+slot);
  var occupied=app.Draft!.Inventory!.FirstOrDefault(x=>x.Index==6&&x.ItemId>0)??app.Draft.Inventory!.FirstOrDefault(x=>x.ItemId>0)??throw new Exception("Select a player with an item for the tooltip check.");
  var tile=FindTile(panel,occupied.Index);var tip=tile._MakeCustomTooltip(tile.TooltipText) as Control??throw new Exception("No rich item tooltip.");
  var host=new Control();app.AddChild(host);host.AddChild(tip);var labels=tip.FindChildren("*","Label",true,false).OfType<Label>().ToArray();
  if(!labels.Any(x=>x.Text.Contains("Item ID: "+occupied.ItemId)))throw new Exception("Rich tooltip is missing the item ID.");
  if(labels.Any(x=>x.Text.Contains("<font",StringComparison.OrdinalIgnoreCase)||x.Text.Contains("<p>",StringComparison.OrdinalIgnoreCase)))throw new Exception("Tooltip must format client item descriptions instead of displaying HTML tags.");
  if(!labels.Any(x=>x.GetThemeColor("font_color").G>x.GetThemeColor("font_color").B))throw new Exception("Tooltip rarity/stat colours were lost.");
  if(!tip.FindChildren("*","",true,false).Any(x=>x is LibreKO.UpgradeBadge))throw new Exception("Tooltip must use the client's upgrade badge.");
  host.QueueFree();
 }
 public static async Task<Control> TooltipForCapture(EditorApp app){
  var slot=app.Draft!.Inventory!.FirstOrDefault(x=>x.Index==6&&x.ItemId>0)??app.Draft.Inventory!.First(x=>x.ItemId>0);
  var tile=FindTile(app.InventoryView!,slot.Index);var tip=(Control)tile._MakeCustomTooltip(tile.TooltipText);tip.ZIndex=1000;app.AddChild(tip);tip.ResetSize();await app.ToSignal(app.GetTree(),SceneTree.SignalName.ProcessFrame);
  var screen=app.GetViewportRect().Size;tip.Position=new(Math.Max(8,app.InventoryView!.GetGlobalRect().Position.X-tip.Size.X-10),Math.Clamp(tile.GetGlobalRect().Position.Y-140,8,Math.Max(8,screen.Y-tip.Size.Y-12)));return tip;
 }
 static Control FindTile(Node panel,int index)=>panel.FindChildren("*","",true,false).OfType<Control>().Single(x=>x.Name=="InventorySlot_"+index);
}
