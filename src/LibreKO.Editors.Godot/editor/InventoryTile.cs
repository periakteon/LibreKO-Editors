using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using GameUi=LibreKO.UiTheme;
namespace LibreKO.Editors.Desktop;

// Database metadata stays in InventorySlotDto. The client ItemSlot is a visual
// projection only; its narrower count/expiry types are never written back.
public partial class InventoryTile:Control {
 InventoryPanel owner=null!;LibreKO.ItemSlotView view=null!;TextureRect emptyIcon=null!;Label hint=null!;
 public int Index {get;private set;}
 public void Setup(InventoryPanel panel,int index,string emptyHint,float size){
  owner=panel;Index=index;Name="InventorySlot_"+index;CustomMinimumSize=Size=new(size,size);MouseFilter=MouseFilterEnum.Stop;MouseDefaultCursorShape=CursorShape.PointingHand;
  view=Ui.Add(this,new LibreKO.ItemSlotView(size){Index=index,Veiled=true,MouseFilter=MouseFilterEnum.Ignore});view.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
  var empty=Ui.Add(view,new Control{MouseFilter=MouseFilterEnum.Ignore});
  emptyIcon=Ui.Add(empty,new TextureRect{Texture=LibreKO.UiIcons.Equipment(index),ExpandMode=TextureRect.ExpandModeEnum.IgnoreSize,StretchMode=TextureRect.StretchModeEnum.KeepAspectCentered,MouseFilter=MouseFilterEnum.Ignore,SelfModulate=new Color(GameUi.TextLo,0.34f)});emptyIcon.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);emptyIcon.OffsetLeft=emptyIcon.OffsetTop=8;emptyIcon.OffsetRight=emptyIcon.OffsetBottom=-8;
  hint=Ui.Add(empty,GameUi.Text(emptyHint,9,new Color(GameUi.TextLo,0.42f),HorizontalAlignment.Center));hint.VerticalAlignment=VerticalAlignment.Center;hint.MouseFilter=MouseFilterEnum.Ignore;hint.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);hint.AddThemeColorOverride("font_color",new Color(GameUi.TextLo,0.42f));
  MouseEntered+=()=>view.EmitSignal(Control.SignalName.MouseEntered);MouseExited+=()=>view.EmitSignal(Control.SignalName.MouseExited);
  view.Clicked+=_=>{owner.Select(Index);if(owner.Slot(Index).ItemId==0&&!GetViewport().GuiIsDragging())owner.BrowseSlot(Index);};view.RightClicked+=_=>{owner.Select(Index);owner.BrowseSlot(Index);};view.DoubleClicked+=_=>{owner.Select(Index);owner.EditSelected();};
 }
 public void Display(InventorySlotDto slot,string tooltip,bool selected){
  view.Set(new LibreKO.Domain.ItemSlot{ItemId=slot.ItemId,Durability=slot.Durability,Count=(short)Math.Min(slot.Count,(ushort)short.MaxValue),Flag=slot.Flag,UniqueId=slot.UniqueId,ExpiresAt=slot.ExpiresAt is >=int.MinValue and <=int.MaxValue?(int)slot.ExpiresAt:0},slot.Count);
  view.Look=selected?LibreKO.SlotLook.Selected:LibreKO.SlotLook.Normal;emptyIcon.Visible=slot.ItemId==0&&emptyIcon.Texture!=null;hint.Visible=slot.ItemId==0&&emptyIcon.Texture==null;TooltipText=tooltip;
  Modulate=Index>=14&&Index<42||Index>=60?owner.IsSearchHit(slot)?Colors.White:new Color(1,1,1,0.28f):Colors.White;
 }
 public override void _GuiInput(InputEvent e)=>view._GuiInput(e);
 public override Variant _GetDragData(Vector2 atPosition){if(!owner.CanEdit||owner.Slot(Index).ItemId==0)return default;LibreKO.DragLayer.ShowItem(this,owner.Slot(Index).ItemId,atPosition);return new Godot.Collections.Dictionary{{"inventory",owner.GetInstanceId()},{"slot",Index}};}
 public override bool _CanDropData(Vector2 atPosition,Variant data){if(data.VariantType!=Variant.Type.Dictionary)return false;var d=data.AsGodotDictionary();return d.ContainsKey("inventory")&&d["inventory"].AsUInt64()==owner.GetInstanceId()&&d.ContainsKey("slot")&&owner.CanMove(d["slot"].AsInt32(),Index);}
 public override void _DropData(Vector2 atPosition,Variant data){if(_CanDropData(atPosition,data))owner.Move(data.AsGodotDictionary()["slot"].AsInt32(),Index);}
 public override GodotObject _MakeCustomTooltip(string forText)=>owner.CreateTooltip(Index);
}
