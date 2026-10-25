using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using GameUi=LibreKO.UiTheme;
namespace LibreKO.Editors.Desktop;

public partial class InventoryPanel {
 // Ported from Client/Features/Windows/InventoryFeature.cs. Storage indices,
 // radii, angles and cell sizes deliberately match the actual game window.
 const float DollCell=42,OuterRadius=146,InnerRadius=77,BoardSpan=338;
 static readonly (int Slot,string Hint,float Angle)[] OuterRing=[
  (1,"Helmet",90),(10,"Pants",56),(9,"Ring",29),(11,"Ring",5),(7,"Belt",-17),
  (13,"Boots",-41),(8,"L hand",-75),(6,"R hand",-105),(12,"Gloves",-139),
  (3,"Neck",-163),(2,"Ear",175),(0,"Ear",151),(4,"Pauldron",124)];
 static readonly (int Slot,string Hint,float Angle)[] InnerRing=[
  (47,"Emblem",22.5f),(46,"Top",67.5f),(43,"Mask",112.5f),(42,"Wings",157.5f),
  (50,"Tattoo",-157.5f),(45,"Pathos",-112.5f),(44,"Pathos",-67.5f),(51,"Talis",-22.5f)];
 LineEdit bagSearch=null!;Label goldLabel=null!,weightLabel=null!,usageLabel=null!;
 ProgressBar usageBar=null!;double footerElapsed;

 void BuildGameLayout(){
  AddThemeConstantOverride("separation",8);
  detail=Ui.Column(this);ShowSelected();
  var frame=Ui.Add(this,new PanelContainer{Name="ClientInventory",SizeFlagsHorizontal=SizeFlags.ShrinkCenter,CustomMinimumSize=new(354,0)});
  frame.AddThemeStyleboxOverride("panel",GameUi.WindowPanel());
  var layout=Ui.Column(frame);layout.AddThemeConstantOverride("separation",0);
  var header=Ui.Add(layout,new PanelContainer());header.AddThemeStyleboxOverride("panel",GameUi.WindowHeaderBand());
  var heading=Ui.Row(header);heading.AddChild(LibreKO.UiIcons.Image("system/bag",new(14,14),GameUi.GoldBright));
  GameText(heading,"INVENTORY",13,GameUi.TextHi).SizeFlagsHorizontal=SizeFlags.ExpandFill;
  var bodyMargin=Ui.Add(layout,new MarginContainer());GameUi.Margins(bodyMargin,8,4,8,6);
  var body=Ui.Column(bodyMargin);body.AddThemeConstantOverride("separation",4);
  var board=Ui.Add(body,new InventoryDollBoard{Name="EquipmentDoll",CustomMinimumSize=new(BoardSpan,BoardSpan),SizeFlagsHorizontal=SizeFlags.ShrinkCenter});
  var center=new Vector2(BoardSpan/2,BoardSpan/2);
  foreach(var (slot,hint,angle) in OuterRing)AddDoll(slot,hint,OuterRadius,angle);
  foreach(var (slot,hint,angle) in InnerRing)AddDoll(slot,hint,InnerRadius,angle);
  AddTile(board,49,"Fairy",DollCell).Position=center+new Vector2(-25,0)-Vector2.One*(DollCell/2);
  AddTile(board,5,"Pet",DollCell).Position=center+new Vector2(25,0)-Vector2.One*(DollCell/2);
  void AddDoll(int slot,string hint,float radius,float angle){var radians=Mathf.DegToRad(angle);AddTile(board,slot,hint,DollCell).Position=center+new Vector2(radius*Mathf.Cos(radians),-radius*Mathf.Sin(radians))-Vector2.One*(DollCell/2);}

  var lower=Ui.Column(body);lower.CustomMinimumSize=new(304,0);lower.SizeFlagsHorizontal=SizeFlags.ShrinkCenter;lower.AddThemeConstantOverride("separation",4);
  var bagRow=Ui.Row(lower);
  for(int i=57;i<60;i++)AddTile(bagRow,i,"Bag",36);
  Ui.Add(bagRow,new Control{SizeFlagsHorizontal=SizeFlags.ExpandFill});
  bagRow.AddChild(LibreKO.UiIcons.Image("system/coins",new(15,15),GameUi.Gold));goldLabel=GameText(bagRow,"",13,GameUi.Gold);
  var searchRow=Ui.Row(lower);bagSearch=Ui.Add(searchRow,new LineEdit{PlaceholderText="Search the bag",ClearButtonEnabled=true,SizeFlagsHorizontal=SizeFlags.ExpandFill,CustomMinimumSize=new(0,28)});bagSearch.AddThemeFontSizeOverride("font_size",12);bagSearch.TextChanged+=_=>Refresh();
  var arrange=Ui.Add(searchRow,GameUi.IconButton(LibreKO.UiIcons.Get("system/arrange"),"Arrange main inventory by item ID · one undo step"));arrange.CustomMinimumSize=new(30,30);arrange.AddThemeConstantOverride("icon_max_width",16);arrange.Pressed+=()=>{if(!CanEdit)return;try{draft.ArrangeInventory(ItemSlot);Changed();}catch(Exception e){app.Status.Text=e.Message;}};arrange.Disabled=draft.Original.IsOnline;
  AddGrid(lower,Enumerable.Range(14,28),7,40);
  var footer=Ui.Row(lower);var meters=Ui.Column(footer);meters.AddThemeConstantOverride("separation",3);
  weightLabel=GameText(meters,"",12,GameUi.TextHi);weightLabel.HorizontalAlignment=HorizontalAlignment.Right;
  usageLabel=GameText(meters,"",12,GameUi.TextHi);usageLabel.HorizontalAlignment=HorizontalAlignment.Right;
  usageBar=Ui.Add(meters,new ProgressBar{ShowPercentage=false,CustomMinimumSize=new(0,5),MaxValue=28});usageBar.AddThemeStyleboxOverride("background",GameUi.MeterTrack());usageBar.AddThemeStyleboxOverride("fill",GameUi.MeterFill(GameUi.Gold));
  Ui.Add(footer,new InventoryTrash(this));

  var extras=Ui.Group(this,"Bag storage & reserved slots");extras.Visible=false;var toggle=extras.GetParent().GetChild<Button>(0);toggle.Text="▸  BAG STORAGE & RESERVED SLOTS";
  for(int bag=0;bag<3;bag++){GameText(extras,$"Bag {bag+1} storage",12,GameUi.Gold);AddGrid(extras,Enumerable.Range(60+bag*12,12),6,40);}
  GameText(extras,"Reserved costume positions · existing data is preserved",11,GameUi.TextLo);AddGrid(extras,new[]{48,52,53,54,55,56},6,40);
 }

 InventoryTile AddTile(Node parent,int index,string hint,float size){var tile=Ui.Add(parent,new InventoryTile());tile.Setup(this,index,hint,size);tiles.Add(index,tile);return tile;}
 void AddGrid(Node parent,IEnumerable<int> slots,int columns,float size){var grid=Ui.Add(parent,new GridContainer{Columns=columns,SizeFlagsHorizontal=SizeFlags.ShrinkCenter});grid.AddThemeConstantOverride("h_separation",4);grid.AddThemeConstantOverride("v_separation",4);foreach(var index in slots)AddTile(grid,index,"",size);}
 static Label GameText(Node parent,string text,int size,Color colour){var label=GameUi.Text(text,size,colour);label.AddThemeColorOverride("font_color",colour);label.MouseFilter=MouseFilterEnum.Ignore;parent.AddChild(label);return label;}
 internal bool CanEdit=>IsInstanceValid(this)&&IsInsideTree()&&app.InventoryView==this&&app.Operations.CanMutate&&app.Connected&&!draft.Original.IsOnline&&app.Draft==draft;
 internal bool IsSearchHit(InventorySlotDto slot){var text=bagSearch.Text.Trim();return text.Length==0||slot.ItemId!=0&&(slot.ItemId.ToString().Contains(text)||items.GetValueOrDefault(slot.ItemId)?.Label.Contains(text,StringComparison.OrdinalIgnoreCase)==true);}
 void RefreshFooter(){if(goldLabel==null)return;goldLabel.Text=(draft.Values["Money"]?.ToString() is {} money&&long.TryParse(money,out var gold)?gold:0).ToString("N0");var occupied=draft.Inventory!.Count(x=>x.Index is >=14 and <42&&x.ItemId>0);usageLabel.Text=$"{occupied}/28";usageBar.Value=occupied;decimal weight=0;foreach(var slot in draft.Inventory!.Where(x=>x.ItemId>0)){if(!items.TryGetValue(slot.ItemId,out var item)||!decimal.TryParse(item.Details?["Weight"]?.ToString(),out var single))continue;var charge=item.Details?["Kind"]?.ToString()=="255"&&item.Details?["Countable"]?.ToString()=="0";weight+=single*(charge?1:slot.Count);}weightLabel.Text=$"{weight/10:0.0} LT";}
 public override void _Process(double delta){footerElapsed+=delta;if(footerElapsed<0.5||draft.Inventory==null)return;footerElapsed=0;RefreshFooter();}
 internal Control CreateTooltip(int index)=>new InventoryTooltip(Slot(index),items.GetValueOrDefault(Slot(index).ItemId));
 internal void RequestClear(int target){if(!CanEdit)return;var slot=Slot(target);if(slot.ItemId==0)return;var name=items.GetValueOrDefault(slot.ItemId)?.Label??"Unknown item";var d=Ui.Add(app,new ConfirmationDialog{Title="Clear inventory position",DialogText=$"Remove {slot.ItemId} · {name} from {InventoryLayout.Name(target)}? This stays in the draft until saved."});d.Confirmed+=()=>{try{if(!CanEdit)return;if(Slot(target)!=slot){app.Status.Text="This inventory position changed. Select the item again before clearing it.";return;}var next=draft.Inventory!.ToArray();next[target]=InventoryLayout.Empty(target);InventoryLayout.Validate(next,ItemSlot);draft.SetInventory(next);Changed();}catch(Exception e){app.Status.Text=e.Message;}finally{d.QueueFree();}};d.Canceled+=()=>d.QueueFree();d.PopupCentered();}
}

public partial class InventoryDollBoard:Control {
 public override void _Draw(){var center=Size/2;var tint=new Color(GameUi.Gold,0.4f);DrawArc(center,146,0,Mathf.Tau,128,tint,1,true);DrawArc(center,77,0,Mathf.Tau,128,tint,1,true);}
}

public partial class InventoryTrash:PanelContainer {
 readonly InventoryPanel owner;
 public InventoryTrash(InventoryPanel owner){this.owner=owner;CustomMinimumSize=new(45,45);SizeFlagsVertical=SizeFlags.ShrinkEnd;TooltipText="Drop an item to remove it from the draft · confirmation required";var box=GameUi.Slot(GameUi.Bad);box.BgColor=new(0.24f,0.08f,0.08f,0.7f);AddThemeStyleboxOverride("panel",box);var center=Ui.Add(this,new CenterContainer{MouseFilter=MouseFilterEnum.Ignore});center.AddChild(LibreKO.UiIcons.Image("system/trash",new(20,20),GameUi.Bad));}
 public override bool _CanDropData(Vector2 atPosition,Variant data){if(!owner.CanEdit||data.VariantType!=Variant.Type.Dictionary)return false;var d=data.AsGodotDictionary();return d.ContainsKey("inventory")&&d["inventory"].AsUInt64()==owner.GetInstanceId()&&d.ContainsKey("slot")&&d["slot"].AsInt32() is >=0 and <96&&owner.Slot(d["slot"].AsInt32()).ItemId>0;}
 public override void _DropData(Vector2 atPosition,Variant data){if(_CanDropData(atPosition,data))owner.RequestClear(data.AsGodotDictionary()["slot"].AsInt32());}
}
