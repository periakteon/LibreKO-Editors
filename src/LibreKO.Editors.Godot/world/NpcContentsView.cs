using Godot;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.Core.Sources;

namespace LibreKO.Editors.Desktop;
public partial class NpcContentsView : VBoxContainer
{
    readonly WorldWorkspace workspace;
    EditorSession Session=>workspace.Session;
    readonly OptionButton mode;
    readonly Label scope,notice,selectionLabel,shopLineLabel;
    readonly VBoxContainer destination,details;
    readonly GridContainer palette;
    readonly LineEdit paletteSearch;
    readonly SpinBox shopLine;
    readonly Dictionary<int,NamedReference> names=[];
    readonly List<ItemBoardTile> tiles=[];
    WorldKey? selected,owner,dropKey,poolKey;
    WorldDraft? boundDraft;
    WorldRowSnapshot? cachedDrop,cachedPool;
    SourceFileKey? shopKey;
    SourceDocument? shopDocument;
    int group,page,slot=1,epoch,serial,palettePage,poolSerial;
    int[] paletteIds=[];
    bool loading;
    int sourceGeneration,databaseGeneration;
    public Task ReadyTask {get;private set;}=Task.CompletedTask;
    internal int Mode=>mode.Selected;
    internal WorldKey? DropKey=>dropKey;
    internal SourceDocument? ShopDocument=>shopDocument;
    bool Current()=>IsInsideTree()&&workspace.Active&&workspace.Selected==selected&&ReferenceEquals(boundDraft,workspace.Draft)&&sourceGeneration==Session.Sources.Generation&&databaseGeneration==Session.Generation;
    bool CanEdit=>Current()&&!loading&&Session.Operations.CanMutate;
    public NpcContentsView(WorldWorkspace workspace)
    {
        this.workspace=workspace;Name="NpcContents";SizeFlagsVertical=SizeFlags.ExpandFill;
        var tools=Ui.Add(this,new HFlowContainer());
        mode=Ui.Add(tools,new OptionButton{FitToLongestItem=false,CustomMinimumSize=new(130,28)});foreach(var label in new[]{"Loot slots","Merchant shop","Ordered pool"})mode.AddItem(label);
        mode.ItemSelected+=i=>{page=0;slot=Mode==0?1:0;RefreshBoard();};
        shopLineLabel=Ui.Label(tools,"Shop line",11,Ui.Muted);shopLine=Ui.Add(tools,new SpinBox{MinValue=0,MaxValue=255,Step=1,CustomMinimumSize=new(70,28)});shopLine.ValueChanged+=v=>{page=0;RefreshBoard();};
        Ui.Button(tools,"‹",()=>{if(page>0){page--;RefreshBoard();}}).TooltipText="Previous 24 shop positions or pool entries";
        Ui.Button(tools,"›",()=>{var max=Mode==1?10:Math.Max(0,(PoolTokens().Count-1)/24);if(page<max){page++;RefreshBoard();}}).TooltipText="Next 24 shop positions or pool entries";
        Ui.Button(tools,"Shared users…",()=>_ = ShowUsersAsync()).TooltipText="List every typed prototype using this shop or drop group before editing shared data.";
        scope=Ui.Label(this,"Select an NPC, drop group or ordered pool",11,Ui.Accent);scope.AutowrapMode=TextServer.AutowrapMode.WordSmart;scope.MaxLinesVisible=2;
        Ui.Add(this,new ActivityIndicator(Session.Loading,"npc-contents"));
        var scroll=Ui.Add(this,new ScrollContainer{SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,CustomMinimumSize=new(0,155)});
        var body=Ui.Row(scroll);body.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        destination=Ui.Column(body);destination.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var catalog=Ui.Column(body);catalog.CustomMinimumSize=new(155,0);
        var find=Ui.Row(catalog);paletteSearch=Ui.Input(find,"Find item / ID");paletteSearch.CustomMinimumSize=new(85,0);paletteSearch.TextSubmitted+=text=>{palettePage=0;_ = LoadPaletteAsync();};
        Ui.Button(find,"Find",()=>{palettePage=0;_ = LoadPaletteAsync();});
        palette=Ui.Add(catalog,new GridContainer{Columns=3});
        var paging=Ui.Row(catalog);Ui.Button(paging,"‹",()=>{if(palettePage>0){palettePage--;_ = LoadPaletteAsync();}});Ui.Button(paging,"›",()=>{if(paletteIds.Length==6){palettePage++;_ = LoadPaletteAsync();}});
        Ui.Label(catalog,"Drag icons to a slot",10,Ui.Muted);
        selectionLabel=Ui.Label(this,"",11,Ui.Accent);selectionLabel.ClipText=true;
        details=Ui.Column(this);
        notice=Ui.Label(this,"Drag to swap; click a slot to choose a named item. Changes remain in the draft.",10,Ui.Muted);notice.AutowrapMode=TextServer.AutowrapMode.WordSmart;notice.MaxLinesVisible=2;
    }
    internal void Show(WorldKey? key)=>ReadyTask=LoadAsync(key);
    async Task LoadAsync(WorldKey? key)
    {
        var request=++serial;if(selected!=key){mode.Select(0);page=0;slot=1;}selected=key;boundDraft=workspace.Draft;sourceGeneration=Session.Sources.Generation;databaseGeneration=Session.Generation;loading=true;
        using var activity=Session.Loading.Begin("npc-contents","Loading shop and loot definitions…");
        owner=dropKey=poolKey=null;cachedDrop=cachedPool=null;shopDocument=null;shopKey=null;group=0;
        try
        {
            if(key==null||boundDraft==null||key.Table is not(WorldTable.Npcs or WorldTable.NpcItems or WorldTable.MakeItemGroups)){Ui.Clear(destination);return;}
            var values=boundDraft.Rows.Single(r=>r.Key==key).Values;
            if(key.Table==WorldTable.Npcs)
            {
                owner=key;group=FieldValueParser.ReadInt32(values["SellingGroup"]);var id=FieldValueParser.ReadInt32(values["ItemGroup"]);
                if(id>0)dropKey=new(WorldTable.NpcItems,id,IsMonster:key.IsMonster);
            }
            else if(key.Table==WorldTable.NpcItems){dropKey=key;mode.Select(0);}
            else {poolKey=key;mode.Select(2);}
            if(dropKey!=null&&boundDraft.Rows.All(r=>r.Key!=dropKey))
            {
                var row=await Session.Client.SendAsync<WorldRowSnapshot?>("world.get",dropKey);
                if(!Current()||request!=serial)return;
                if(row?.Key!=null)cachedDrop=row;else dropKey=null;
            }
            if(group>0&&Session.Sources.Store!=null)
            {
                Session.Sources.CaptureDrafts();shopKey=Session.Sources.Store.Key(SourceRoot.ServerData,"ItemSells.json");
                shopDocument=Session.Sources.Drafts!.Documents.SingleOrDefault(d=>d.Snapshot.Key==shopKey);
                if(shopDocument==null&&Session.Sources.Store.Read(shopKey).Exists)shopDocument=Session.Sources.Drafts.Open(shopKey);
            }
            if(!Current()||request!=serial)return;
            RefreshBoard();await LoadPaletteAsync();
        }
        catch(Exception e){if(request==serial&&IsInsideTree())notice.Text="Contents unavailable · "+e.Message;}
        finally{if(request==serial){loading=false;if(Current())RefreshBoard();}}
    }
    WorldRowSnapshot? Drop()=>dropKey==null?null:boundDraft?.Rows.SingleOrDefault(r=>r.Key==dropKey)??cachedDrop;
    WorldRowSnapshot? Pool()=>poolKey==null?null:boundDraft?.Rows.SingleOrDefault(r=>r.Key==poolKey)??cachedPool;
    List<string> PoolTokens()=>ItemBoardEdits.Tokens(Pool()?.Values["Items"]?.ToString()??"");
    void IncludeDrop(){if(Drop() is {} row)workspace.IncludeRow(row);else throw new InvalidOperationException("Drop group no longer available.");}
    void IncludePool(){if(Pool() is {} row)workspace.IncludeRow(row);else throw new InvalidOperationException("Pool no longer available.");}
    internal void ChooseMode(int selectedMode){mode.Select(selectedMode);page=0;slot=selectedMode==0?1:0;RefreshBoard();}
    internal void RefreshBoard()
    {
        if(!IsInsideTree()||boundDraft==null)return;epoch++;tiles.Clear();Ui.Clear(destination);Ui.Clear(details);shopLine.Visible=shopLineLabel.Visible=Mode==1;
        var grid=Ui.Add(destination,new GridContainer{Columns=3});
        if(Mode==0)
        {
            var drop=Drop();scope.Text=drop==null?"No assigned drop group. Choose Item Group in the inspector, or create a private group.":$"Drop group {drop.Key.Id} · {(drop.Key.IsMonster==true?"Monster":"NPC")} · shared database draft · chances are base rolls / 10000";
            if(drop==null){Ui.Button(destination,"Create private drop group",()=>_ = CreateDropAsync());return;}
            for(int i=1;i<=7;i++){int at=i;var value=FieldValueParser.ReadInt32(drop.Values["Item"+i]);AddTile(grid,"drop",at,value,$"{i} · {FieldValueParser.ReadInt32(drop.Values["Percent"+i])/100m:0.##}%");}
        }
        else if(Mode==1)
        {
            scope.Text=$"Shop group {group} · line {(int)shopLine.Value} · slots {page*24}–{Math.Min(255,page*24+23)} · shared ItemSells.json draft";
            if(group<=0||shopDocument==null){Ui.Label(destination,"Assign a Selling Group and configure ItemSells.json in the source profile.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;return;}
            var json=shopDocument.Json();for(int i=page*24;i<Math.Min(256,page*24+24);i++)
            {var rows=json.Rows.Where(r=>FieldValueParser.ReadInt32(r.Values["SellingGroup"])==group&&FieldValueParser.ReadInt32(r.Values["Line"])==(int)shopLine.Value&&FieldValueParser.ReadInt32(r.Values["Index"])==i).ToArray();AddTile(grid,"shop",i,rows.Length==1?FieldValueParser.ReadInt32(rows[0].Values["ItemId"]):0,rows.Length>1?"Duplicate":i.ToString());}
        }
        else
        {
            var tokens=PoolTokens();scope.Text=poolKey==null?"Select a pool-valued loot slot, then choose Open pool.":$"Pool {poolKey.Id} · {tokens.Count} ordered entries · duplicates retained · shared database draft";
            if(poolKey==null)return;
            for(int i=page*24;i<Math.Min(tokens.Count,page*24+24);i++){int.TryParse(tokens[i].Trim(),out var value);AddTile(grid,"pool",i,value,(i+1).ToString());}
            Ui.Button(destination,"Append named item…",()=>BrowseItem(true));
        }
        SelectSlot(slot);_ = NameTilesAsync();
    }
    void AddTile(Node parent,string kind,int index,int id,string label)
    {var tile=Ui.Add(parent,new ItemBoardTile(this,kind,index,id,label));if(kind!="palette")tiles.Add(tile);}
    async Task NameTilesAsync()
    {
        var version=epoch;var keys=tiles.Where(t=>t.ItemId>0&&(t.Kind!="drop"||t.ItemId>=100_000_000)).Select(t=>new ReferenceIdentity("items",t.ItemId)).Distinct().ToArray();
        try
        {
            var found=await Session.References.ResolveBatchAsync(keys);
            if(!Current()||version!=epoch)return;foreach(var item in found)names[item.Key.Id]=item;
            foreach(var tile in tiles)tile.SetMeaning(Tooltip(tile.ItemId,tile.Kind));SelectSlot(slot);
        }catch(Exception e){if(Current()&&version==epoch)notice.Text="Item names unavailable · "+e.Message;}
    }
    async Task LoadPaletteAsync()
    {
        if(!Current())return;var request=++paletteSerial;var text=paletteSearch.Text;var requestedPage=palettePage;
        using var activity=Session.Loading.Begin("npc-contents","Loading item palette…");
        try
        {
            var result=await Session.Client.SendAsync<Page<RecordSummary>>("items.list",new RecordQuery(text,requestedPage,6));
            if(!Current()||request!=paletteSerial)return;Ui.Clear(palette);paletteIds=result.Rows.Select(r=>r.Key.Id).ToArray();
            foreach(var row in result.Rows){names[row.Key.Id]=new(new("items",row.Key.Id),row.Label,"Database","resolved",Details:row.Details);AddTile(palette,"palette",row.Key.Id,row.Key.Id,"");}
        }catch(Exception e){if(Current()&&request==paletteSerial)notice.Text="Palette unavailable · "+e.Message;}
    }
    int paletteSerial;
    internal string Tooltip(int id,string kind)=>id==0?"Empty · drop an item here":ItemBoardEdits.IsPoolReference(kind,id)?$"Ordered pool {id} · select and Open pool":kind=="drop"&&id<100_000_000?$"{id} · ignored drop value in this server":$"{id} · {names.GetValueOrDefault(id)?.Label??"Loading item name…"}\n"+string.Join("\n",names.GetValueOrDefault(id)?.Details?.Where(v=>v.Key is "Damage" or "Ac" or "ReqLevel" or "Duration" or "Description").Select(v=>Ui.FieldName(v.Key)+": "+v.Value)??[]);
    internal void Changed(){workspace.ContentsChanged();RefreshBoard();}
    internal JsonObject CaptureState()=>new(){["mode"]=Mode,["page"]=page,["slot"]=slot,["line"]=(int)shopLine.Value,["pool"]=poolKey?.Id,["query"]=paletteSearch.Text,["palettePage"]=palettePage};
    internal async Task RestoreStateAsync(JsonObject state)
    {
        var draft=boundDraft;var view=serial;
        if(state["pool"] is {} p)
        {var id=p.GetValue<int>();await OpenPoolAsync(id,false);if(!Current()||draft!=boundDraft||view!=serial)return;if(poolKey?.Id!=id)throw new InvalidOperationException("The saved ordered pool is no longer available.");}
        mode.Select(state["mode"]!.GetValue<int>());shopLine.Value=state["line"]!.GetValue<int>();page=state["page"]!.GetValue<int>();slot=state["slot"]!.GetValue<int>();
        paletteSearch.Text=state["query"]!.GetValue<string>();palettePage=state["palettePage"]!.GetValue<int>();RefreshBoard();await LoadPaletteAsync();
    }
}
