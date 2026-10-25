using Godot;
using LibreKO.Editors.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LibreKO.Editors.Desktop;
public partial class ItemOriginsView : VBoxContainer
{
    readonly EditorSession session;readonly int itemId;
    readonly VBoxContainer rows;readonly Label caption,diagnostics;
    int page,serial;bool more,started;
    public Task ReadyTask { get; private set; }=Task.CompletedTask;
    public ItemOriginsView(EditorSession session,int itemId)
    {
        this.session=session;this.itemId=itemId;
        Ui.Heading(this,"WHERE THIS ITEM COMES FROM",11).AddThemeColorOverride("font_color",Ui.Accent);
        Ui.Label(this,"Named sellers and direct / pooled drop definitions. Open a prototype to inspect every spawn location.",12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
        Ui.Add(this,new ActivityIndicator(session.Loading,"origins"));
        diagnostics=Ui.Label(this,"",11,Ui.Accent);diagnostics.AutowrapMode=TextServer.AutowrapMode.WordSmart;
        var scroll=Ui.Add(this,new ScrollContainer{SizeFlagsVertical=SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,CustomMinimumSize=new(0,190)});
        rows=Ui.Column(scroll);rows.SizeFlagsHorizontal=SizeFlags.ExpandFill;
        var tools=Ui.Add(this,new HFlowContainer());Ui.Button(tools,"Previous",()=>{if(page>0){page--;ReadyTask=LoadAsync();}});
        caption=Ui.Label(tools,"",11,Ui.Muted);Ui.Button(tools,"Next",()=>{if(more){page++;ReadyTask=LoadAsync();}});
        Ui.Button(tools,"Refresh origins",()=>ReadyTask=LoadAsync());
        VisibilityChanged+=()=>{if(IsVisibleInTree()&&!started){started=true;ReadyTask=LoadAsync();}};
    }
    public override void _Ready(){if(IsVisibleInTree()){started=true;ReadyTask=LoadAsync();}}
    async Task LoadAsync()
    {
        var request=++serial;var generation=session.Generation;var source=session.Sources.Generation;
        using var activity=session.Loading.Begin("origins","Finding item sellers and drops…");
        Ui.Clear(rows);caption.Text="Loading…";
        try
        {
            await session.Sources.EnsureRegisteredAsync();
            var result=await session.Client.SendAsync<ItemOriginPage>("world.item-origins",new ItemOriginQuery(itemId,page,20));
            if(!IsInsideTree()||request!=serial||generation!=session.Generation||source!=session.Sources.Generation)return;
            caption.Text=$"{result.Total:N0} origins · page {result.Page+1}";more=(result.Page+1)*result.Size<result.Total;
            diagnostics.Text=string.Join("\n",result.Diagnostics);
            if(result.Rows.Count==0)Ui.Label(rows,"No configured origins on this page.",12,Ui.Muted);
            foreach(var origin in result.Rows)
            {
                var panel=Ui.Add(rows,new PanelContainer());panel.AddThemeStyleboxOverride("panel",Ui.Surface(8));var body=Ui.Column(panel);
                Ui.Label(body,$"{origin.Owner.Id} · {origin.OwnerName} · {(origin.Owner.IsMonster==true?"Monster":"NPC")}",13,Ui.Accent).AutowrapMode=TextServer.AutowrapMode.WordSmart;
                var text=origin.Kind=="shop"?$"Shop {origin.ShopGroup} · page {origin.ShopLine} · slot {origin.Slot} · {origin.Source!.RelativePath} row {origin.SourceRow+1}":$"Drop group {origin.Definition!.Id} · slot {origin.Slot} · base roll {origin.BasisPoints/100m:0.##}%"+(origin.PoolId is int pool?$" · pool {pool} · {origin.PoolMatches}/{origin.PoolSize} entries":" · direct item");
                Ui.Label(body,text,12,Ui.Muted).AutowrapMode=TextServer.AutowrapMode.WordSmart;
                var links=Ui.Add(body,new HFlowContainer());
                Ui.Button(links,"NPC / monster & spawns",()=>_ = session.Shell.OpenTargetAsync(Target(origin.Owner))).TooltipText="Open this typed prototype and browse its placements across all zones.";
                if(origin.Definition!=null)Ui.Button(links,"Drop definition",()=>_ = session.Shell.OpenTargetAsync(Target(origin.Definition)));
                if(origin.PoolId is int poolId)Ui.Button(links,"Ordered pool",()=>_ = session.Shell.OpenTargetAsync(Target(new(WorldTable.MakeItemGroups,poolId))));
                if(origin.Source!=null)Ui.Button(links,"Shop source row",()=>
                {
                    var key=JsonSerializer.SerializeToNode(origin.Source,JsonConfig.Options)!.AsObject();key["row"]=origin.SourceRow;
                    _=session.Shell.OpenTargetAsync(new("world","source",key,origin.Source.ProfileId));
                });
            }
        }
        catch(Exception e){if(IsInsideTree()&&request==serial){caption.Text="Origins unavailable";diagnostics.Text=e.Message;more=false;}}
    }
    NavigationTarget Target(WorldKey key)=>new("world",key.Table.ToString(),new JsonObject{["id"]=key.Id,["zone"]=key.Zone,["isMonster"]=key.IsMonster},session.HistoryRoot);
}
