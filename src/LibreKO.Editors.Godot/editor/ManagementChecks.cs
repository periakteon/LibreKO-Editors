using Godot;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public static class ManagementChecks {
 public static async Task Account(EditorApp app){
  app.Module=2;await app.LoadList();await app.SelectRecord(310001);await app.PropertiesReady;
  if(app.Draft!.Original.Key.Kind!=RecordKind.Account)throw new Exception("Accounts navigation does not load account records.");
  var before=app.Record!;var authority=app.Properties.FindChildren("*","OptionButton",true,false).OfType<OptionButton>().Single(x=>x.Name=="AccountAuthority");
  authority.Select(0);authority.EmitSignal(OptionButton.SignalName.ItemSelected,0);
  var cash=app.Properties.FindChildren("*","LineEdit",true,false).OfType<LineEdit>().Single(x=>x.PlaceholderText=="KnightCash");cash.Text="8750";cash.EmitSignal(LineEdit.SignalName.TextChanged,cash.Text);
  var expiry=app.Properties.FindChildren("*","LineEdit",true,false).OfType<LineEdit>().Single(x=>x.PlaceholderText=="PremiumDate");expiry.Text="2030-01-02 03:04:05";expiry.EmitSignal(LineEdit.SignalName.TextChanged,expiry.Text);
  if(app.Draft.Values["Authority"]!.ToString()!="0"||app.Draft.Values["KnightCash"]!.ToString()!="8750")throw new Exception("Named account controls did not change the draft.");
  await app.SaveRecord();if(app.Draft!.IsDirty||app.Record!.Values["KnightCash"]!.ToString()!="8750")throw new Exception("Account form save failed.");
  foreach(var key in new[]{"Authority","KnightCash","PremiumDate"})app.Draft.Set(key,before.Values[key]);await app.SaveRecord();
 }
 public static async Task InventoryAdd(EditorApp app){
  var before=app.Draft!.Inventory!.ToArray();var clean=before.ToArray();clean[1]=LibreKO.Editors.Core.InventoryLayout.Empty(1);app.Draft.SetInventory(clean);app.BuildProperties();await app.PropertiesReady;await app.ToSignal(app.GetTree(),SceneTree.SignalName.ProcessFrame);
  if(!app.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text.Contains(" KC · ")))throw new Exception("Character view does not show its account cash and role.");
  var tile=app.InventoryView!.FindChildren("*","",true,false).OfType<InventoryTile>().Single(x=>x.Index==1);tile._GuiInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});
  await app.ToSignal(app.GetTree().CreateTimer(1),SceneTreeTimer.SignalName.Timeout);
  var picker=app.GetChildren().OfType<ConfirmationDialog>().LastOrDefault(x=>x.Title.Contains("Compatible"))??throw new Exception("Clicking an empty equipment slot did not open compatible items.");
  var list=picker.FindChildren("*","ItemList",true,false).OfType<ItemList>().Single();if(list.ItemCount!=2||!Enumerable.Range(0,list.ItemCount).All(i=>list.GetItemText(i).Contains("123450011")||list.GetItemText(i).Contains("123450013")))throw new Exception("Helmet picker displayed incompatible or missing items.");
  list.Select(0);picker.EmitSignal(AcceptDialog.SignalName.Confirmed);
  if(app.Draft.Inventory![1].ItemId!=123450011||app.Draft.Inventory[1].Durability!=100)throw new Exception("Compatible item selection did not add a full-durability item.");app.Draft.Undo();if(app.Draft.Inventory[1].ItemId!=0)throw new Exception("Adding an item was not one undo action.");app.Draft.Redo();
  await app.SaveRecord();var saved=await app.Client.SendAsync<RecordSnapshot>("characters.get",new{id=700001});if(saved.Inventory![1].ItemId!=123450011)throw new Exception("Added item did not persist.");app.Draft!.SetInventory(before);await app.SaveRecord();
 }
}
