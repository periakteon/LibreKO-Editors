using Godot;
using System.Text;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
namespace LibreKO.Editors.Desktop;

public partial class InventoryPanel:VBoxContainer {
 EditorApp app=null!;RecordDraft draft=null!;
 readonly Dictionary<int,ReferenceEntry> items=new();
 readonly Dictionary<int,InventoryTile> tiles=new();
 VBoxContainer detail=null!;int selected=14;
 public void Configure(EditorApp owner,RecordDraft record){
  app=owner;draft=record;SizeFlagsHorizontal=SizeFlags.ExpandFill;
  if(draft.Inventory==null){Ui.Label(this,"Unknown inventory format · preserved read-only",12,Ui.Muted);return;}
  BuildGameLayout();
  Refresh();
 }
 public async Task LoadItems(){if(app.IsClosing||draft.Inventory==null)return;var ids=draft.Inventory.Where(x=>x.ItemId>0).Select(x=>x.ItemId).Distinct().ToArray();if(ids.Length>0){var found=await app.Session.References.ResolveBatchAsync(ids.Select(id=>new ReferenceIdentity("items",id)).ToArray());if(app.IsClosing||!IsInstanceValid(this)||!IsInsideTree())return;foreach(var row in found)items[row.Key.Id]=new(row.Key.Id,string.IsNullOrWhiteSpace(row.Label)?row.State=="unavailable"?"Lookup unavailable":"Missing reference":row.Label,row.Details);}Refresh();ShowSelected();}
 internal InventorySlotDto Slot(int index)=>draft.Inventory!.Single(s=>s.Index==index);
 byte? ItemSlot(int id)=>items.TryGetValue(id,out var item)&&byte.TryParse(item.Details?["Slot"]?.ToString(),out var code)?code:null;
 internal bool CanMove(int source,int target){if(!CanEdit)return false;try{var probe=new RecordDraft(draft.Original with{Inventory=draft.Inventory});probe.MoveInventory(source,target,ItemSlot);return true;}catch{return false;}}
 public void Move(int source,int target){try{if(!CanEdit)return;draft.MoveInventory(source,target,ItemSlot);Changed();Select(target);}catch(Exception e){app.Status.Text=e.Message;}}
 void Changed(){app.Inventory=draft.Inventory!.ToList();Refresh();ShowSelected();app.RefreshPreview();app.Status.Text="Inventory draft changed · Save to database to apply";}
 internal void Select(int index){selected=index;Refresh();ShowSelected();}
 void Refresh(){foreach(var pair in tiles){var slot=Slot(pair.Key);pair.Value.Display(slot,Tooltip(slot),pair.Key==selected);}RefreshFooter();}
 public string Tooltip(InventorySlotDto slot){
  if(slot.ItemId==0)return InventoryLayout.Name(slot.Index)+$" · slot {slot.Index}\nEmpty · drop a compatible item here";
  var item=items.GetValueOrDefault(slot.ItemId);var v=item?.Details;var text=new StringBuilder();text.AppendLine(item?.Label??$"Unknown item {slot.ItemId}");text.AppendLine($"ID {slot.ItemId} · {InventoryLayout.Name(slot.Index)} ({slot.Index})");
  if(v!=null){text.AppendLine(CodeLabels.Describe(CodeLabels.Slots,v["Slot"]?.ToString()??""));if(v["Description"]?.ToString() is {Length:>0} description)text.AppendLine(description);
   foreach(var name in new[]{"Damage","Ac","ReqLevel","ReqStr","ReqSta","ReqDex","ReqIntel","ReqCha","MaxHpB","MaxMpB","StrB","StaB","DexB","IntelB","ChaB","FireDamage","IceDamage","LightningDamage","PoisonDamage","FireR","ColdR","LightningR","MagicR","PoisonR","CurseR"})if(v[name]?.ToString() is {} value&&value!="0")text.AppendLine(Ui.FieldName(name)+": "+value);
  }
  text.AppendLine($"Count {slot.Count} · Durability {slot.Durability}");text.AppendLine($"Flag {slot.Flag} · {(Enum.IsDefined((LibreKO.Domain.ItemFlag)slot.Flag)?((LibreKO.Domain.ItemFlag)slot.Flag).ToString():"Unknown flag")} · Unique ID {slot.UniqueId}");if(slot.ExpiresAt!=0)text.AppendLine("Expires (Unix): "+slot.ExpiresAt);return text.ToString().TrimEnd();
 }
 void ShowSelected(){if(detail==null)return;Ui.Clear(detail);var target=selected;var slot=Slot(target);var item=items.GetValueOrDefault(slot.ItemId);
  var actions=Ui.Row(detail);var label=Ui.Label(actions,InventoryLayout.Name(selected),11,Ui.Accent);label.SizeFlagsHorizontal=SizeFlags.ExpandFill;label.ClipText=true;label.CustomMinimumSize=new(70,0);label.TooltipText=$"Slot {selected} · {slot.ItemId} · {item?.Label??"Empty"}";
  var replace=Ui.Button(actions,slot.ItemId==0?"Add item…":"Replace item…",()=>BrowseSlot(target));replace.TooltipText="Browse all items compatible with this position";replace.Disabled=draft.Original.IsOnline;
  var edit=Ui.Button(actions,"Details…",EditSelected);edit.Disabled=slot.ItemId==0||draft.Original.IsOnline;
  var remove=Ui.Button(actions,"Clear…",()=>RequestClear(target));remove.Disabled=slot.ItemId==0||draft.Original.IsOnline;
 }
 internal void EditSelected(){if(CanEdit&&Slot(selected).ItemId>0)EditMetadata();}
 internal void BrowseSlot(int target){
  if(!CanEdit)return;if(!Enumerable.Range(0,256).Any(code=>InventoryLayout.FitsSlot(target,(byte)code))){app.Status.Text="This reserved position cannot accept new items.";return;}
  if(target>=60&&Slot(57+(target-60)/12).ItemId==0){app.Status.Text="Equip a bag before adding items to its storage.";return;}
  var original=Slot(target);var other=target==6?Slot(8).ItemId:target==8?Slot(6).ItemId:0;
  app.ShowReferencePicker("items",null,entry=>{try{if(!CanEdit)return;if(Slot(target)!=original)throw new InvalidOperationException("This slot changed while choosing. Select the position again.");items[entry.Id]=entry;var list=draft.Inventory!.ToArray();list[target]=new(target,entry.Id,checked((short)FieldValueParser.ReadInt32(entry.Details?["Duration"])),1,0,0,0);InventoryLayout.Validate(list,ItemSlot);draft.SetInventory(list);Changed();}catch(Exception e){app.Status.Text=e.Message;throw;}},inventory:new(target,other));
 }
 void EditMetadata(){var target=selected;var slot=Slot(target);var d=Ui.Add(app,new ConfirmationDialog{Title="Inventory item details",MinSize=new(620,440),DialogHideOnOk=false,OkButtonText="Apply to draft"});var content=Ui.Column(d);content.Position=new(14,40);content.CustomMinimumSize=new(580,0);Ui.Label(content,$"{slot.ItemId} · {items.GetValueOrDefault(slot.ItemId)?.Label}",14,Ui.Accent);var inputs=new Dictionary<string,LineEdit>();foreach(var (name,value) in new[]{("Count",(long)slot.Count),("Durability",(long)slot.Durability),("Flag",(long)slot.Flag),("Expires (Unix)",slot.ExpiresAt),("Unique ID",(long)slot.UniqueId)})inputs[name]=Ui.Field(content,name,"",value.ToString());var error=Ui.Label(content,"",12,new Color("#ffadad"));d.Confirmed+=()=>{try{if(!CanEdit||Slot(target)!=slot)throw new InvalidOperationException("This inventory position changed. Close the dialog and select it again.");long N(string name)=>long.Parse(inputs[name].Text,System.Globalization.CultureInfo.InvariantCulture);var changed=slot with{Count=checked((ushort)N("Count")),Durability=checked((short)N("Durability")),Flag=checked((byte)N("Flag")),ExpiresAt=N("Expires (Unix)"),UniqueId=checked((int)N("Unique ID"))};var next=draft.Inventory!.ToArray();next[target]=changed;InventoryLayout.Validate(next,ItemSlot);draft.SetInventory(next);Changed();d.QueueFree();}catch(Exception e){error.Text=e.Message;}};d.Canceled+=()=>d.QueueFree();d.PopupCentered();}
}
