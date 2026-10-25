using Godot;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public static class EditorChecks {
 public static async Task Run(EditorApp app,string mode){
  // Headless windows default to 64×64; layout checks need the normal desktop size.
  if(DisplayServer.GetName()=="headless"){app.GetWindow().Size=new Vector2I(1500,950);await app.ToSignal(app.GetTree(),SceneTree.SignalName.ProcessFrame);}
  if(mode=="boot")return;
  if(mode=="compatibility-writes"){await CompatibilityWrites(app);return;}
  if(mode is "compatibility" or "compatibility-gallery" or "compatibility-cancel" or "compatibility-schema" or "compatibility-content"){
   if(mode=="compatibility-gallery"){var candidates=await app.Client.SendAsync<DockerDatabase[]>("docker.discover",new{});var prepared=await app.Client.SendAsync<DockerDatabase>("docker.prepare",new{containerId=candidates.First(c=>c.Label.Contains("libreko-db")).ContainerId});app.Session.ConnectionString=prepared.ConnectionString;await app.Connect();}
   if(!app.Connected)throw new Exception("Use the isolated fixture for compatibility checks.");
   var button=app.Session.Shell.FindChildren("*","Button",true,false).OfType<Button>().SingleOrDefault(b=>b.Text=="Compatibility");if(button==null)throw new Exception("Compatibility report is not available in the editor.");
   button.EmitSignal(BaseButton.SignalName.Pressed);var dialog=app.GetChildren().OfType<ConfirmationDialog>().Single(d=>d.Title.StartsWith("Compatibility"));dialog.GuiDisableInput=true;
   if(mode=="compatibility-schema"){if(app.List.ItemCount!=0)throw new Exception("An unsupported schema was loaded into the record browser.");var issues=dialog.FindChildren("*","ItemList",true,false).OfType<ItemList>().Single();if(!Enumerable.Range(0,issues.ItemCount).Any(i=>issues.GetItemText(i).Contains("schema.column")))throw new Exception("Schema errors were hidden.");return;}
   if(mode=="compatibility-content"){var issues=dialog.FindChildren("*","ItemList",true,false).OfType<ItemList>().Single();if(!Enumerable.Range(0,issues.ItemCount).Any(i=>issues.GetItemText(i).Contains("content.unavailable")))throw new Exception("Missing client content did not remain diagnosable.");return;}
   var scan=dialog.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Scan database + client");scan.EmitSignal(BaseButton.SignalName.Pressed);
   if(mode=="compatibility-cancel")dialog.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Stop scan").EmitSignal(BaseButton.SignalName.Pressed);
   var deadline=DateTime.UtcNow.AddSeconds(120);while(scan.Disabled&&DateTime.UtcNow<deadline)await app.ToSignal(app.GetTree().CreateTimer(0.05),SceneTreeTimer.SignalName.Timeout);
   if(scan.Disabled)throw new Exception("Compatibility scan did not finish.");
   var messages=dialog.FindChildren("*","Label",true,false).OfType<Label>().Select(l=>l.Text).ToArray();if(mode=="compatibility-cancel"){if(!messages.Any(t=>t.Contains("Cancelled"))||!app.Connected||app.Client.IsClosed)throw new Exception("Cancelling did not preserve the live connection and partial report.");return;}if(!messages.Any(t=>t.Contains("Completed")))throw new Exception("Compatibility report was not completed.");
   var list=dialog.FindChildren("*","ItemList",true,false).OfType<ItemList>().Single();if(mode=="compatibility"&&!Enumerable.Range(0,list.ItemCount).Any(i=>list.GetItemText(i).Contains("inventory.orphan")))throw new Exception("Orphan inventory reference missing from the report.");
   await Shot("compatibility-report.png");app.GetWindow().Size=new(1280,800);dialog.PopupCentered(new(1040,690));await app.ToSignal(app.GetTree(),SceneTree.SignalName.ProcessFrame);if(dialog.Size.X>1280||dialog.Size.Y>800)throw new Exception("Compatibility report does not fit the compact desktop.");await Shot("compatibility-report-compact.png");return;
  }
  if(mode=="effects-diagnostic"){
   foreach(var name in new[]{"fx_common.gdshaderinc","fx_surface.gdshaderinc","fx_particle.gdshaderinc","fx_mesh.gdshaderinc","fx_orbit_process.gdshader","item_shine.gdshader"})if(ResourceLoader.Load("res://shaders/"+name)==null)throw new Exception("Missing renderer dependency: "+name);
   if(!RenderingServer.GlobalShaderParameterGetList().Any(x=>x.ToString()=="item_shine_classic"))throw new Exception("Missing shine global.");return;
  }
  if(mode=="docker"||mode=="ui-gallery"||mode=="inventory-style"||mode=="accounts-gallery"||mode=="studio-gallery"){
   var rows=await app.Client.SendAsync<DockerDatabase[]>("docker.discover",new{});var db=rows.First(x=>x.Label.Contains("libreko-db"));var prepared=await app.Client.SendAsync<DockerDatabase>("docker.prepare",new{containerId=db.ContainerId});app.Session.ConnectionString=prepared.ConnectionString;await app.Connect();if(mode=="docker")return;
   if(mode=="studio-gallery"){
    foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,800)}){app.GetWindow().Size=size;
     foreach(var module in new[]{0,1,2}){app.Module=module;app.PageNumber=0;app.search.Text=module==0?"Raptor":"";await app.LoadList();var route=module==0?"items":module==1?"characters":"accounts";var records=await app.Client.SendAsync<Page<RecordSummary>>(route+".list",new RecordQuery(app.search.Text));await app.SelectRecord(records.Rows.First().Key.Id);await app.PropertiesReady;await app.ToSignal(app.GetTree(),SceneTree.SignalName.ProcessFrame);await app.ToSignal(app.GetTree(),SceneTree.SignalName.ProcessFrame);StudioLayoutChecks.Verify(app);await Shot("libreko-"+route+(size.X==1280?"-compact":"")+".png");}
    }
    app.ShowReferencePicker("items",null,_=>{});await Shot("libreko-item-picker.png");return;
   }
   if(mode=="accounts-gallery"){app.Module=2;await app.LoadList();var accounts=await app.Client.SendAsync<Page<RecordSummary>>("accounts.list",new RecordQuery());await app.SelectRecord(accounts.Rows.First().Key.Id);await app.PropertiesReady;await Shot("accounts-editor.png");return;}
   if(mode=="inventory-style"){app.Module=1;await app.LoadList();var playersForStyle=await app.Client.SendAsync<Page<RecordSummary>>("characters.list",new RecordQuery());await app.SelectRecord(playersForStyle.Rows.First().Key.Id);await app.PropertiesReady;await app.ToSignal(app.GetTree(),SceneTree.SignalName.ProcessFrame);await InventoryStyleChecks.Verify(app);await Shot("client-inventory-editor.png");var tooltip=await InventoryStyleChecks.TooltipForCapture(app);await Shot("client-inventory-tooltip.png");tooltip.QueueFree();return;}
   app.Module=0;await app.LoadList();var items=await app.Client.SendAsync<Page<RecordSummary>>("items.list",new RecordQuery("Raptor"));if(items.Rows.Count>0)await app.SelectRecord(items.Rows.First().Key.Id);await Shot("items-database-editor.png");
   app.Module=1;await app.LoadList();var players=await app.Client.SendAsync<Page<RecordSummary>>("characters.list",new RecordQuery());if(players.Rows.Count>0)await app.SelectRecord(players.Rows.First().Key.Id);await app.PropertiesReady;await Shot("character-inventory-editor.png");app.Inspector.CurrentTab=1;await Shot("character-properties-editor.png");app.ShowReferencePicker("items",null,_=>{});await Shot("item-reference-picker.png");return;
  }
  if(mode=="content"){var body=PreviewViewport.Character(12,0,0);if(body==null)throw new Exception("No body model.");body.Free();return;}
  if(mode=="references"){
   app.Module=0;await app.SelectRecord(123450001);LineEdit? input=null;
   for(int frame=0;frame<180&&input==null;frame++){input=app.Properties.FindChildren("*","LineEdit",true,false).OfType<LineEdit>().FirstOrDefault(x=>x.PlaceholderText=="ItemPlusId");if(input==null)await app.ToSignal(app.GetTree(),SceneTree.SignalName.ProcessFrame);}
   if(input==null)throw new Exception("Reference field did not appear.");input.Text="123450001";input.EmitSignal(LineEdit.SignalName.TextChanged,input.Text);
   if(app.Draft!.Values["ItemPlusId"]?.ToString()!="123450001")throw new Exception("Visible reference input silently lost its edit while loading names.");await app.PropertiesReady;
   ReferenceEntry? chosen=null;app.ShowReferencePicker("items",null,row=>chosen=row);var dialog=app.GetChildren().OfType<ConfirmationDialog>().Last();var query=dialog.FindChildren("*","LineEdit",true,false).OfType<LineEdit>().First();query.Text="123450001";
   await app.ToSignal(app.GetTree().CreateTimer(1),SceneTreeTimer.SignalName.Timeout);query.EmitSignal(LineEdit.SignalName.TextSubmitted,query.Text);await app.ToSignal(app.GetTree().CreateTimer(1),SceneTreeTimer.SignalName.Timeout);
   var list=dialog.FindChildren("*","ItemList",true,false).OfType<ItemList>().Single();if(list.ItemCount!=1||!list.GetItemText(0).Contains("123450001 · "))throw new Exception("Picker did not show ID and name.");list.Select(0);dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);if(chosen?.Id!=123450001)throw new Exception("Picker selection was lost.");
   app.Module=1;await app.SelectRecord(700001);await app.PropertiesReady;app.Inspector.CurrentTab=1;
   var map=app.Properties.FindChildren("*","LineEdit",true,false).OfType<LineEdit>().Single(x=>x.PlaceholderText=="MapId");map.Text="1";map.EmitSignal(LineEdit.SignalName.TextChanged,map.Text);
   var bind=app.Properties.FindChildren("*","LineEdit",true,false).OfType<LineEdit>().Single(x=>x.PlaceholderText=="Bind");bind.GetParent().GetChildren().OfType<Button>().Single().EmitSignal(BaseButton.SignalName.Pressed);
   await app.ToSignal(app.GetTree().CreateTimer(1),SceneTreeTimer.SignalName.Timeout);var points=app.GetChildren().OfType<ConfirmationDialog>().Last().FindChildren("*","ItemList",true,false).OfType<ItemList>().Single();if(points.ItemCount==0||!points.GetItemText(0).Contains("Zone 1"))throw new Exception("Bind lookup failed after editing map ID.");return;
  }
  if(mode=="effects"){
   var effectDatabases=await app.Client.SendAsync<DockerDatabase[]>("docker.discover",new{});var effectDatabase=effectDatabases.First(x=>x.Label.Contains("libreko-db"));var preparedEffectDatabase=await app.Client.SendAsync<DockerDatabase>("docker.prepare",new{containerId=effectDatabase.ContainerId});app.Session.ConnectionString=preparedEffectDatabase.ConnectionString;await app.Connect();
   var glow=ContentMount.Json("assets/items/weapon/glow.json");GD.Print("Glow catalogue keys: "+string.Join(",",glow.Take(4).Select(x=>x.Key)));
   foreach(var id in new[]{160450276,160450277,160450278,156210048}){app.Module=0;await app.SelectRecord(id);if(app.Preview.Model==null)throw new Exception("No effect test model for "+id);if(!app.Preview.Model.FindChildren("*","",true,false).Any(x=>x is LibreKO.FxWeaponGlow))throw new Exception("Actual weapon glow was not attached for "+id);GD.Print("Effect fixture "+id+" · "+LibreKO.Domain.ItemData.DisplayName(id));await Shot("weapon-effects-"+id+".png");}
   var gear=new int[17];gear[7]=160450277;app.Preview.ShowModel(PreviewViewport.Character(12,0,0,gear));await Shot("equipped-effects.png");return;
  }
  if(mode is "item-editor" or "character-editor" or "inventory" or "inventory-add" or "account-editor" or "shutdown-pending"){
   if(!app.Connected)throw new Exception("Connect the fixture database.");var cs=System.Environment.GetEnvironmentVariable(app.Profile.ConnectionEnvironmentVariable)??"";if(!cs.Contains("Database=libreko_editor_test_",StringComparison.OrdinalIgnoreCase))throw new Exception("Write checks require the isolated fixture database.");
   if(mode=="shutdown-pending"){
    app.Module=0;await app.SelectRecord(123450001);await app.PropertiesReady;app.BuildProperties();var pending=app.PropertiesReady;
    app.ShowReferencePicker("items",null,_=>{});var choices=app.GetChildren().OfType<ConfirmationDialog>().Last().FindChildren("*","ItemList",true,false).OfType<ItemList>().Single();
    var tree=app.GetTree();var shutdown=app.QuitEditor();await pending;await tree.ToSignal(tree.CreateTimer(0.2),SceneTreeTimer.SignalName.Timeout);
    if(choices.ItemCount!=0)throw new Exception("A pending lookup updated the UI after shutdown began.");
    GD.Print("CHECK shutdown-pending PASS");await shutdown;return;
   }
   if(mode=="account-editor"){await ManagementChecks.Account(app);await Shot("account-editor.png");return;}
   if(mode=="item-editor"){app.Module=0;await app.SelectRecord(123450001);var value=app.Draft!.Values["Damage"]!.GetValue<int>()+1;app.Draft.Set("Damage",value);await app.SaveRecord();if(app.Record!.Values["Damage"]!.GetValue<int>()!=value)throw new Exception("Item save failed.");return;}
   app.Module=1;await app.LoadList();await app.SelectRecord(700001);if(app.Draft!.Values["Experience"]!.GetValue<long>()!=9007199254740993L)throw new Exception("Experience lost precision.");
   await app.PropertiesReady;if(mode=="inventory-add"){await ManagementChecks.InventoryAdd(app);return;}
   if(mode=="inventory"){
    var original=app.Draft.Inventory!.ToArray();var slots=original.ToArray();slots[14]=new(14,123450001,75,3,4,9007199254740993L,87);slots[15]=new(15,123450001,15,2,9,123456,42);app.Draft.SetInventory(slots);app.Inventory=slots.ToList();app.BuildProperties();
    await app.ToSignal(app.GetTree().CreateTimer(2),SceneTreeTimer.SignalName.Timeout);await app.InventoryView!.LoadItems();
    var tiles=app.InventoryView.FindChildren("*","",true,false).OfType<InventoryTile>().ToArray();if(tiles.Length!=96)throw new Exception("Not all 96 positions are visible/available.");
    var addItem=app.InventoryView.FindChildren("*","Button",true,false).OfType<Button>().First(x=>x.Text.Contains("Add item")||x.Text.Contains("Replace item"));if(addItem.GlobalPosition.Y+addItem.Size.Y>app.GetViewportRect().Size.Y)throw new Exception($"Add item actions are hidden below the visible inventory. Viewport {app.GetViewportRect().Size}, window {app.GetWindow().Size}, action {addItem.GetGlobalRect()}.");
    var data=new Godot.Collections.Dictionary{{"inventory",app.InventoryView.GetInstanceId()},{"slot",14}};var targetTile=tiles.Single(t=>t.Index==15);if(!targetTile._CanDropData(Vector2.Zero,data))throw new Exception("Valid UI drag was rejected.");targetTile._DropData(Vector2.Zero,data);
    if(app.Draft.Inventory![15]!=(slots[14] with{Index=15})||app.Draft.Inventory[14]!=(slots[15] with{Index=14}))throw new Exception("UI swap lost metadata.");
    app.Draft.Undo();if(!app.Draft.Inventory.SequenceEqual(slots))throw new Exception("UI swap was not one undo action.");app.Draft.Redo();
    if(!app.InventoryView.Tooltip(app.Draft.Inventory[15]).Contains("9007199254740993"))throw new Exception("Hover metadata lost precision.");
    var beforeClear=app.Draft.Inventory.ToArray();app.InventoryView.RequestClear(15);var clear=app.GetChildren().OfType<ConfirmationDialog>().Last();var changed=beforeClear.ToArray();changed[15]=changed[15] with{Count=7};app.Draft.SetInventory(changed);clear.EmitSignal(AcceptDialog.SignalName.Confirmed);if(app.Draft.Inventory[15].ItemId==0)throw new Exception("A stale clear confirmation removed a changed inventory slot.");app.Draft.SetInventory(beforeClear);
    await app.SaveRecord();var saved=await app.Client.SendAsync<RecordSnapshot>("characters.get",new{id=700001});if(saved.Inventory![15].UniqueId!=87||saved.Inventory[14].Count!=2)throw new Exception("Inventory did not persist.");app.Draft!.SetInventory(original);await app.SaveRecord();return;
   }
   var money=app.Draft.Values["Money"]!.GetValue<int>()+1;app.Draft.Set("Money",money);await app.SaveRecord();if(app.Record!.Values["Money"]!.GetValue<int>()!=money)throw new Exception("Character save failed.");await Shot("fixture-character.png");return;
  }
  throw new Exception("Unknown check mode: "+mode);
  async Task Shot(string file){await app.ToSignal(app.GetTree().CreateTimer(2),SceneTreeTimer.SignalName.Timeout);if(DisplayServer.GetName()=="headless")return;System.IO.Directory.CreateDirectory(System.IO.Path.Combine(app.Root,"artifacts"));app.GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(app.Root,"artifacts",file));}
 }
 static async Task CompatibilityWrites(EditorApp app){
  var connection=System.Environment.GetEnvironmentVariable(app.Profile.ConnectionEnvironmentVariable)??"";if(!connection.Contains("Database=libreko_editor_test_",StringComparison.OrdinalIgnoreCase))throw new Exception("Write checks require the isolated fixture database.");
  app.Module=0;await app.SelectRecord(123450001);await app.PropertiesReady;
  app.Draft!.Set("Damage",app.Draft.Values["Damage"]!.GetValue<int>()+1);await app.SaveRecord();await Report(true);
  app.Session.Shell.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="History").EmitSignal(BaseButton.SignalName.Pressed);var history=app.GetChildren().OfType<AcceptDialog>().Single(d=>d.Title=="Database history and recovery");history.GuiDisableInput=true;var rows=history.FindChildren("*","ItemList",true,false).OfType<ItemList>().Single();rows.Select(Enumerable.Range(0,rows.ItemCount).First(i=>rows.GetItemText(i).StartsWith("Item 123450001 ·")));history.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Reverse selected").EmitSignal(BaseButton.SignalName.Pressed);await Wait();history.QueueFree();await Frame();await Report(true);
  await app.SelectRecord(123450001);await app.PropertiesReady;var cloneId=198700001;while((await app.Client.SendAsync<Page<RecordSummary>>("items.list",new RecordQuery(cloneId.ToString()))).Rows.Any(r=>r.Key.Id==cloneId))cloneId++;
  app.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Clone item…").EmitSignal(BaseButton.SignalName.Pressed);var clone=app.GetChildren().OfType<ConfirmationDialog>().Single(d=>d.Title=="Clone database item");clone.GuiDisableInput=true;clone.FindChildren("*","LineEdit",true,false).OfType<LineEdit>().Single().Text=cloneId.ToString();clone.EmitSignal(AcceptDialog.SignalName.Confirmed);await Wait();if(app.Record?.Key.Id!=cloneId)throw new Exception("Clone check did not save its record.");await Report(true);
  async Task Frame()=>await app.ToSignal(app.GetTree(),SceneTree.SignalName.ProcessFrame);
  async Task Wait(){var deadline=DateTime.UtcNow.AddSeconds(15);while(!app.Operations.CanMutate&&DateTime.UtcNow<deadline)await app.ToSignal(app.GetTree().CreateTimer(.05),SceneTreeTimer.SignalName.Timeout);if(!app.Operations.CanMutate)throw new Exception("Database write did not finish.");await Frame();}
  async Task Report(bool stale){app.ShowCompatibility();var dialog=app.GetChildren().OfType<ConfirmationDialog>().Single(d=>d.Title.StartsWith("Compatibility"));dialog.GuiDisableInput=true;if(!dialog.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text.Contains("STALE"))&&stale)throw new Exception("A database write left the compatibility report current.");dialog.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text=="Check schema").EmitSignal(BaseButton.SignalName.Pressed);await Wait();if(dialog.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.Text.Contains("STALE")))throw new Exception("Schema refresh did not reset report invalidation.");dialog.EmitSignal(AcceptDialog.SignalName.Confirmed);await Frame();}
 }
}
