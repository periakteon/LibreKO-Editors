using Godot;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.Core.Sources;

namespace LibreKO.Editors.Desktop;
public partial class NpcContentsView
{
    internal void SelectSlot(int index)
    {
        if(index!=slot)poolSerial++;
        slot=index;Ui.Clear(details);foreach(var tile in tiles)if(IsInstanceValid(tile))tile.Highlight(tile.Index==index);
        var value=SelectedValue();selectionLabel.Text=Tooltip(value,Mode==0?"drop":Mode==1?"shop":"pool").Split('\n')[0];
        var actions=Ui.Add(details,new HFlowContainer());
        Ui.Button(actions,"Choose item…",()=>BrowseItem()).Disabled=Mode==0&&dropKey==null||Mode==1&&shopDocument==null||Mode==2&&poolKey==null;
        if(Mode==0)
        {
            Ui.Button(actions,"Choose pool…",()=>ReferencePicker.Show(Session.Shell,Session,new("drop-pools"),entry=>SetDrop(entry.Key.Id),()=>CanEdit));
            var chance=Ui.Add(actions,new SpinBox{MinValue=-327.68,MaxValue=327.67,Step=.01,Suffix="%",Value=(Drop()?.Values["Percent"+slot] is {} v?FieldValueParser.ReadInt32(v):0)/100.0,CustomMinimumSize=new(100,28),TooltipText="Base drop chance: stored signed 16-bit chance / 10000. Zero or negative disables the slot; server bonuses alter the live roll."});
            chance.ValueChanged+=value=>{if(CanEdit&&Drop() is {} row){IncludeDrop();boundDraft!.Set(row.Key,"Percent"+slot,JsonValue.Create((int)Math.Round(value*100)));workspace.ContentsChanged();foreach(var tile in tiles.Where(t=>t.Kind=="drop"&&t.Index==slot))tile.SetCaption($"{slot} · {value:0.##}%");}};
            if(value is >=100 and <100_000_000)Ui.Button(actions,"Open pool",()=>_ = OpenPoolAsync(value));
        }
        Ui.Button(actions,"Clear",ClearSlot).Disabled=value==0;
        if(value>=100_000_000||Mode!=0&&value>0)Ui.Button(actions,"Item & origins",()=>_ = Session.Shell.OpenTargetAsync(new("content","items",new JsonObject{["id"]=value,["tab"]=2},Session.HistoryRoot)));
    }
    int SelectedValue()
    {
        if(Mode==0)return slot is >=1 and <=7?FieldValueParser.ReadInt32(Drop()?.Values["Item"+slot]):0;
        if(Mode==2){var tokens=PoolTokens();return slot>=0&&slot<tokens.Count&&int.TryParse(tokens[slot].Trim(),out var id)?id:0;}
        var rows=shopDocument?.Json().Rows.Where(r=>FieldValueParser.ReadInt32(r.Values["SellingGroup"])==group&&FieldValueParser.ReadInt32(r.Values["Line"])==(int)shopLine.Value&&FieldValueParser.ReadInt32(r.Values["Index"])==slot).ToArray();
        return rows?.Length==1?FieldValueParser.ReadInt32(rows[0].Values["ItemId"]):0;
    }
    void BrowseItem(bool append=false)
    {
        var expected=CaptureState();ReferencePicker.Show(Session.Shell,Session,new("items"),entry=>
        {if(!CanEdit||!JsonNode.DeepEquals(expected,CaptureState()))throw new InvalidOperationException("The selected position changed; choose it again.");ApplyItem(entry.Key.Id,append);},()=>Current());
    }
    internal async Task<bool> SetItemAsync(int itemId)
    {
        if(!CanEdit)return false;var expected=CaptureState();bool applied=false;
        await Session.RunAsync(async()=>
        {
            var item=await Session.Client.SendAsync<RecordSnapshot?>("items.get",new{id=itemId});
            if(item?.Values==null)throw new InvalidOperationException("This item no longer exists in the database.");
            if(!Current()||!JsonNode.DeepEquals(expected,CaptureState()))throw new InvalidOperationException("The selected position changed.");
            ApplyItem(itemId);applied=true;
        });return applied;
    }
    void ApplyItem(int itemId,bool append=false)
    {
        if(!Current()||boundDraft==null||itemId<=0)throw new InvalidOperationException("Choose an existing positive item ID in the active draft.");
        if(Mode==0){if(itemId<100_000_000)throw new InvalidOperationException("This server treats drop values below 100000000 as pool IDs. Put this custom item in an ordered pool.");SetDrop(itemId);return;}
        if(Mode==1)EditShop(json=>ItemBoardEdits.SetShopSlot(json,group,(int)shopLine.Value,slot,itemId));
        else
        {
            if(poolKey==null)throw new InvalidOperationException("Open a pool first.");var tokens=PoolTokens();
            if(append){tokens.Add(itemId.ToString());slot=tokens.Count-1;page=slot/24;}
            else{if(slot<0||slot>=tokens.Count)throw new InvalidOperationException("Choose an existing pool entry, or Append.");tokens[slot]=itemId.ToString();}
            IncludePool();boundDraft.Set(poolKey,"Items",JsonValue.Create(string.Join(",",tokens)));
        }
        Changed();
    }
    void SetDrop(int value)
    {
        if(!Current()||dropKey==null||slot is <1 or >7)throw new InvalidOperationException("Choose a drop slot first.");
        if(value is >0 and <100)throw new InvalidOperationException("Drop values 1–99 are not item/pool references in this server.");
        IncludeDrop();boundDraft!.Set(dropKey,"Item"+slot,JsonValue.Create(value));Changed();
    }
    void ClearSlot()
    {
        if(!CanEdit)return;
        try
        {
            if(Mode==0)SetDrop(0);
            else if(Mode==1){EditShop(json=>ItemBoardEdits.SetShopSlot(json,group,(int)shopLine.Value,slot,0));Changed();}
            else if(poolKey!=null){IncludePool();boundDraft!.Set(poolKey,"Items",JsonValue.Create(ItemBoardEdits.RemovePoolEntry(Pool()!.Values["Items"]!.ToString(),slot)));Changed();}
        }catch(Exception e){notice.Text=e.Message;}
    }
    void EditShop(Action<JsonSourceDocument> edit)
    {
        if(shopKey==null||shopDocument==null)throw new InvalidOperationException("Configure an ItemSells source first.");
        Session.Sources.CaptureDrafts();
        void Apply(SourceDocument document){var json=document.Json();edit(json);document.EditBytes(json.Render());}
        if(workspace.SourceView!=null)workspace.SourceView.EditExternal(shopKey,Apply);else Apply(shopDocument);
    }
    internal void UndoShop(bool redo)
    {
        if(!CanEdit||shopKey==null||shopDocument==null)return;
        Session.Sources.CaptureDrafts();
        if(workspace.SourceView!=null)workspace.SourceView.EditExternal(shopKey,document=>document.Undo(redo));else shopDocument.Undo(redo);
        workspace.ContentsChanged();RefreshBoard();
    }
    internal async Task OpenPoolAsync(int id,bool activate=true)
    {
        if(!Current()||boundDraft==null)return;var key=new WorldKey(WorldTable.MakeItemGroups,id);
        var request=++poolSerial;var view=serial;var version=epoch;var draft=boundDraft;
        bool Valid()=>Current()&&request==poolSerial&&view==serial&&version==epoch&&ReferenceEquals(draft,boundDraft);
        try
        {
        if(boundDraft.Rows.All(r=>r.Key!=key))
        {
            using var activity=Session.Loading.Begin("npc-contents","Loading ordered pool…");
            var row=await Session.Client.SendAsync<WorldRowSnapshot?>("world.get",key);
            if(!Valid())return;if(row?.Key==null){notice.Text="The referenced pool is missing.";return;}cachedPool=row;
        }
        if(!Valid())return;poolKey=key;if(activate){mode.Select(2);page=0;slot=0;}RefreshBoard();
        }
        catch(Exception e){if(Valid())notice.Text="Pool unavailable · "+e.Message;}
    }
    internal Godot.Collections.Dictionary DragPayload(string kind,int index,int itemId)=>new()
    {{"board",GetInstanceId()},{"epoch",epoch},{"kind",kind},{"index",index},{"item",itemId},{"line",(int)shopLine.Value},{"mode",Mode},{"scope",Session.HistoryRoot}};
    internal bool CanDrop(string targetKind,int target,Variant data)
    {
        if(!CanEdit||targetKind=="palette"||data.VariantType!=Variant.Type.Dictionary)return false;
        var d=data.AsGodotDictionary();
        if(!d.ContainsKey("board")||!d.ContainsKey("epoch")||!d.ContainsKey("kind")||!d.ContainsKey("index")||!d.ContainsKey("item")||!d.ContainsKey("mode")||!d.ContainsKey("scope")||!d.ContainsKey("line"))return false;
        if(d["board"].AsUInt64()!=GetInstanceId()||d["epoch"].AsInt32()!=epoch||d["mode"].AsInt32()!=Mode||d["scope"].AsString()!=Session.HistoryRoot||d["item"].AsInt32()<=0)return false;
        var source=d["kind"].AsString();return source=="palette"||source==targetKind&&d["index"].AsInt32()!=target;
    }
    internal async Task DropAsync(string targetKind,int target,Variant data)
    {
        if(!CanDrop(targetKind,target,data))return;var d=data.AsGodotDictionary();
        try
        {
            var from=d["index"].AsInt32();var source=d["kind"].AsString();slot=target;
            if(source=="palette"){await SetItemAsync(d["item"].AsInt32());return;}
            if(targetKind=="drop"&&dropKey!=null){IncludeDrop();ItemBoardEdits.SwapDropSlots(boundDraft!,dropKey,from,target);}
            else if(targetKind=="shop")EditShop(json=>ItemBoardEdits.SwapShopSlots(json,group,d["line"].AsInt32(),from,(int)shopLine.Value,target));
            else if(targetKind=="pool"&&poolKey!=null){IncludePool();boundDraft!.Set(poolKey,"Items",JsonValue.Create(ItemBoardEdits.MovePoolEntry(Pool()!.Values["Items"]!.ToString(),from,target)));}
            Changed();
        }catch(Exception e){notice.Text=e.Message;}
    }
    async Task CreateDropAsync()
    {
        if(!CanEdit||owner==null)return;
        await Session.RunAsync(async()=>
        {
            var id=await Session.Client.SendAsync<int>("world.next-id",new{table=WorldTable.NpcItems,isMonster=owner.IsMonster,reserved=boundDraft!.Rows.Where(r=>r.Key.Table==WorldTable.NpcItems).Select(r=>r.Key.Id).ToArray()});
            if(!Current())return;var key=new WorldKey(WorldTable.NpcItems,id,IsMonster:owner.IsMonster);
            var values=(JsonObject)workspace.Catalog!.Tables.Single(t=>t.Table==WorldTable.NpcItems).Defaults.DeepClone();values["Index"]=id;values["IsMonster"]=owner.IsMonster;
            var npc=boundDraft.Rows.Single(r=>r.Key==owner);var next=(JsonObject)npc.Values.DeepClone();next["ItemGroup"]=id;
            boundDraft.Apply([new(key,null,values),new(owner,npc,next)]);dropKey=key;workspace.ContentsChanged();
        });RefreshBoard();
    }
    async Task ShowUsersAsync()
    {
        if(!Current())return;
        var query=Mode==1?new WorldQuery(WorldTable.Npcs,ShopGroup:group):dropKey==null?null:new WorldQuery(WorldTable.Npcs,IsMonster:dropKey.IsMonster,ParentId:dropKey.Id);
        var pool=Mode==2?poolKey:null;var databaseScope=Session.HistoryRoot;var generation=Session.Generation;
        if(pool==null&&(query==null||Mode==1&&group<=0)){notice.Text="Select an assigned group or ordered pool first.";return;}
        var dialog=Ui.Add(Session.Shell,new AcceptDialog{Title=pool==null?"Prototypes sharing this group":"Drop groups and prototypes sharing this pool",MinSize=new(720,500)});var body=Ui.Column(dialog);body.Position=new(18,40);body.CustomMinimumSize=new(675,390);
        Ui.Label(body,pool==null?"Saving this group affects every prototype using it. Open a prototype to inspect its assignments.":"Every typed drop group referencing this pool is shown with its prototype owners, including groups with no assigned prototype.",12,Ui.Accent).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var list=Ui.Add(body,new ItemList{CustomMinimumSize=new(670,290)});var caption=Ui.Label(body,"Loading users…",11,Ui.Muted);var bar=Ui.Row(body);int usersPage=0,requestSerial=0,total=0;WorldKey[] targets=[];
        bool Valid()=>IsInstanceValid(dialog)&&dialog.IsInsideTree()&&databaseScope==Session.HistoryRoot&&generation==Session.Generation;
        async Task Load()
        {
            var request=++requestSerial;var requestedPage=usersPage;using var activity=Session.Loading.Begin("npc-contents","Loading shared users…");
            try
            {
                string[] labels;WorldKey[] keys;
                if(pool!=null)
                {
                    var rows=await Session.Client.SendAsync<Page<PoolConsumer>>("world.pool-consumers",new PoolConsumerQuery(pool.Id,requestedPage));
                    total=rows.Total;keys=rows.Rows.Select(r=>r.Owner??r.DropGroup).ToArray();
                    labels=rows.Rows.Select(r=>$"Drop group {r.DropGroup.Id} · {(r.DropGroup.IsMonster==true?"Monster":"NPC")} → "+(r.Owner==null?r.OwnerName:$"{r.Owner.Id} · {r.OwnerName}")).ToArray();
                }
                else
                {
                    var rows=await Session.Client.SendAsync<Page<WorldRowSnapshot>>("world.list",query! with{Page=requestedPage,Size=50});
                    total=rows.Total;keys=rows.Rows.Select(r=>r.Key).ToArray();labels=rows.Rows.Select(r=>$"{r.Key.Id} · {r.Values["Name"]} · {(r.Key.IsMonster==true?"Monster":"NPC")}").ToArray();
                }
                if(!Valid()||request!=requestSerial)return;targets=keys;list.Clear();foreach(var label in labels)list.AddItem(label);caption.Text=$"{total} references · page {requestedPage+1}";
            }
            catch(Exception e){if(IsInstanceValid(caption)&&request==requestSerial)caption.Text="Shared users unavailable · "+e.Message;}
        }
        Ui.Button(bar,"Previous",()=>{if(usersPage>0){usersPage--;_ = Load();}});Ui.Button(bar,"Next",()=>{if((usersPage+1)*50<total){usersPage++;_ = Load();}});
        Ui.Button(bar,"Open user / group",()=>{var i=list.GetSelectedItems().FirstOrDefault(-1);if(Valid()&&i>=0&&i<targets.Length){var key=targets[i];dialog.Hide();dialog.QueueFree();_ = Session.Shell.OpenTargetAsync(new("world",key.Table.ToString(),new JsonObject{["id"]=key.Id,["isMonster"]=key.IsMonster},databaseScope));}});
        dialog.Confirmed+=()=>dialog.QueueFree();dialog.Canceled+=()=>dialog.QueueFree();dialog.PopupCentered();await Load();
    }
}
