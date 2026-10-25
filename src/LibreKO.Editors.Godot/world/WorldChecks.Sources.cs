using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Sources;
namespace LibreKO.Editors.Desktop;
public static partial class WorldChecks
{
    static async Task Sources(EditorShell shell,string mode)
    {
        _=SourceChecks.Marker(shell);var session=shell.Session;var profile=session.Sources.Profile!;var store=session.Sources.Store!;
        SourceFileKey Key(SourceRoot root,string name)=>new(profile.Id,root,SourceIdentity.Root(profile.Roots[root]),name);
        async Task Frame()=>await shell.ToSignal(shell.GetTree(),SceneTree.SignalName.ProcessFrame);
        async Task Wait(Func<bool> ready){var deadline=DateTime.UtcNow.AddSeconds(25);while(!ready()){if(DateTime.UtcNow>deadline)throw new Exception("Source integration check timed out: "+mode);await Frame();}}
        await shell.RequestWorkspaceAsync("world");var app=(WorldWorkspace)shell.Active!;app.ShowSources();var sources=app.SourceView!;
        if(mode=="world-json")
        {
            var formats=Enum.GetValues<WorldTable>().Select(t=>t+".json").Concat(new[]{"ItemSells.json","ItemExchangeExps.json","MiningExchanges.json","MiningFishingItems.json"}).ToArray();
            foreach(var name in formats)
            {
                await sources.OpenAsync(Key(SourceRoot.ServerData,name));await Frame();
                var format=ServerJsonCatalog.Format(name)!;var expected=ServerJsonCatalog.Fields(format,session.Sources.WorldCatalog).Select(f=>"SourceField_"+f.Field.Name).Order();
                var actual=sources.FindChildren("SourceField_*","",true,false).OfType<Control>().Where(c=>c.IsVisibleInTree()).Select(c=>c.Name.ToString()).Order();
                if(!expected.SequenceEqual(actual))throw new Exception("Source form field coverage differs: "+name);
                if(!sources.FindChildren("*","CodeEdit",true,false).Any())throw new Exception("Raw source view missing.");
            }
            var key=Key(SourceRoot.ServerData,"ItemSells.json");await sources.OpenAsync(key);var first=sources.SelectedDocument!;var hash=store.Read(key).Sha256;
            await session.References.ResolveAsync(new("items",int.Parse(first.Json().Root[0]!["ItemId"]!.ToString())),profile.Id,default);
            sources.Undo(false);await Frame();
            var input=sources.FindChildren("SourceField_ItemId","LineEdit",true,false).OfType<LineEdit>().Single(c=>c.IsVisibleInTree());
            await Wait(()=>sources.FindChildren("*","Label",true,false).OfType<Label>().Any(l=>l.IsVisibleInTree()&&l.Text.Contains(input.Text)&&!l.Text.Contains("resolving")));
            input.Text=input.Text=="123450002"?"123450001":"123450002";input.GrabFocus();
            var capture=session.Sources.CaptureDrafts()!;if(capture.Files.Count!=1||store.Read(key).Sha256!=hash)throw new Exception("Focused source capture wrote or lost draft.");
            foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,800)}){shell.GetWindow().Size=size;await EventGalleries.Capture(shell,"world-server-sources",size);}
            app.Discard();await Frame();
            var poolKey=Key(SourceRoot.ServerData,"MakeItemGroups.json");await sources.OpenAsync(poolKey);var poolDocument=sources.SelectedDocument!;var originalPool=poolDocument.Json().Root[0]!["Items"]!.GetValue<string>();
            var pool=sources.FindChildren("*","",true,false).OfType<OrderedItemPoolView>().Single(c=>c.IsVisibleInTree());
            var move=pool.FindChildren("*","Button",true,false).OfType<Button>().Where(b=>b.Text=="↑").Last();move.EmitSignal(BaseButton.SignalName.Pressed);
            var changedPool=poolDocument.Json().Root[0]!["Items"]!.GetValue<string>();if(changedPool==originalPool||changedPool.Split(',').Count(x=>x=="123450001")!=2)throw new Exception("Ordered pool controls lost order or duplicate members.");sources.Undo(false);if(poolDocument.IsDirty)throw new Exception("Pool reorder was not one source undo.");
            var pageKey=Key(SourceRoot.ServerData,"NpcPositions.zone999.json");var rows=new JsonArray();for(int i=0;i<105;i++)rows.Add(new JsonObject{["Index"]=i,["NpcId"]=880009,["ZoneId"]=210,["ActType"]=1});
            sources.OpenCandidate(new(new(pageKey,false,"",null),store.Blobs.Put(Encoding.UTF8.GetBytes(rows.ToJsonString()))));sources.SelectSourceRow(104);var ordinal=sources.FindChildren("SourceField_Index","LineEdit",true,false).OfType<LineEdit>().Single(c=>c.IsVisibleInTree());if(ordinal.Text!="104")throw new Exception("Paged source ordinal navigation selected the wrong row.");app.Discard();await Frame();
            await shell.OpenTargetAsync(new("world","source",JsonSerializer.SerializeToNode(key,JsonConfig.Options)!.AsObject(),profile.Id));if(app.SourceView!.SelectedDocument?.Snapshot.Key!=key)throw new Exception("Named source target did not open its actual file.");return;
        }
        if(mode=="world-seed-compare")
        {
            var key=Key(SourceRoot.ServerData,"NpcPositions.json");await sources.OpenAsync(key);var doc=sources.SelectedDocument!.Json();
            var values=new JsonObject{["Index"]=888888,["NpcId"]=880009,["ZoneId"]=210,["ActType"]=1};var row=new WorldRowSnapshot(new(WorldTable.NpcPositions,888888),values,"fixture");
            var match=SeedRowMatcher.Match(row,[doc]);if(match.State!=SeedMatchState.Unique)throw new Exception("Generated index was treated as seed identity.");
            var original=store.Read(key);var changed=row with{Values=(JsonObject)values.DeepClone()};changed.Values["NpcId"]=880001;
            var pending=SeedComparison.PrepareSeedUpdate(changed,doc,match.Candidates[0],store);sources.OpenCandidate(pending.Files.Single());
            var inverse=SeedComparison.PrepareDatabaseUpdate(doc,match.Candidates[0],row,row.Key,session.Sources.WorldCatalog);
            if(inverse.Rows.Single().After!["Index"]!.GetValue<int>()!=888888||store.Read(key).Sha256!=original.Sha256)throw new Exception("Comparison renumbered DB key or wrote source.");
            var review=SeedComparisonView.Show(session,row,[doc]);await Frame();var dialog=shell.GetChildren().OfType<AcceptDialog>().Single(d=>d.Title=="Database / server source comparison"||d.Title=="Database / server source comparison");
            dialog.EmitSignal(AcceptDialog.SignalName.Canceled);await review;app.Discard();return;
        }
        await sources.OpenAsync(Key(SourceRoot.ServerData,"ItemSells.json"));var document=sources.SelectedDocument!;var beforeFileHash=store.Read(document.Snapshot.Key).Sha256;
        if(mode=="world-source-cancel")
        {
            document.SetText("[{\"SellingGroup\":880009,\"Line\":0,\"Index\":0,\"ItemId\":123450004}]");
            var review=SourceChangeReview.Show(session,null,session.Sources.CaptureDrafts());await Frame();var d=shell.GetChildren().OfType<AcceptDialog>().Single(x=>x.Name=="SourceChangeReview");d.EmitSignal(AcceptDialog.SignalName.Canceled);if(await review)throw new Exception("Cancelled review reported completion.");
            if(!app.HasUnsavedChanges||store.Read(document.Snapshot.Key).Sha256!=beforeFileHash)throw new Exception("Cancelled review lost/wrote source.");
            var leave=shell.CanLeaveAsync();await Frame();var dirty=shell.GetChildren().OfType<ConfirmationDialog>().Single(x=>x.Title=="Unsaved changes");dirty.EmitSignal(ConfirmationDialog.SignalName.Canceled);if(await leave)throw new Exception("Dirty source navigation was unguarded.");
            var stale=document.Capture(store);app.Discard();await session.Sources.SelectAsync(session.Profile.Sources![1]);app.ShowSources();
            var rejected=await session.Sources.ApplyAsync(new(Guid.NewGuid(),[stale]));if(rejected.State!=FileApplyState.Invalid)throw new Exception("Old source callback crossed profile scope.");
            await session.Sources.SelectAsync(profile);app.ShowSources();return;
        }
        var npcKey=new WorldKey(WorldTable.Npcs,Random.Shared.Next(700000000,750000000),IsMonster:true);var catalog=session.Sources.WorldCatalog!;var npc=(JsonObject)catalog.Tables.Single(t=>t.Table==WorldTable.Npcs).Defaults.DeepClone();npc["Id"]=npcKey.Id;npc["IsMonster"]=true;npc["Name"]="Original database fixture";
        var created=await session.Client.SendAsync<WorldSaveResult>("world.save",new WorldChange(Guid.NewGuid(),[new(npcKey,null,npc)],[]));if(created.Status!=SaveStatus.Saved)throw new Exception("Cannot create mixed-save fixture.");
        app.ShowDatabase();await app.SelectAsync(npcKey);app.SetValue(npcKey,"Name",JsonValue.Create("Saved database fixture"));app.ShowSources();var nextItem=document.Json().Root[0]!["ItemId"]!.ToString()=="123450002"?123450003:123450002;document.SetText("[{\"SellingGroup\":880009,\"Line\":0,\"Index\":0,\"ItemId\":"+nextItem+"}]");
        var clientKey=store.Key(SourceRoot.ClientProject,"assets/terrain/mixed-save/heightmap.png");sources.OpenCandidate(new(store.Read(clientKey),store.Blobs.Put(LibreKO.Editors.Core.Client.ClientHeightmapCodec.Encode(new(3,4,new float[9])))));await sources.PreviewReady;await sources.OpenAsync(document.Snapshot.Key);
        session.Sources.ApplyCheckpoint=(step,_)=>{if(step=="prepared")throw new IOException("Native interruption before file replacement");};
        var save=app.SaveAsync();await Wait(()=>shell.GetChildren().OfType<AcceptDialog>().Any(d=>d.Name=="SourceChangeReview"));var reviewDialog=shell.GetChildren().OfType<AcceptDialog>().Single(d=>d.Name=="SourceChangeReview");
        Button Button(string text)=>reviewDialog.FindChildren("*","Button",true,false).OfType<Button>().Single(b=>b.Text==text);
        Button("Save database changes").EmitSignal(BaseButton.SignalName.Pressed);await Wait(()=>session.Operations.CanMutate&&Button("Save database changes").Disabled);
        var historyCount=(await session.Client.SendAsync<OperationLink[]>("world.history",new{})).Length;
        Button("Apply file changes").EmitSignal(BaseButton.SignalName.Pressed);await Wait(()=>session.Operations.CanMutate&&session.Sources.History!.List().Any(x=>x.Pending));
        session.Sources.ApplyCheckpoint=null;var current=await session.Client.SendAsync<WorldRowSnapshot>("world.get",npcKey);var dbName=current.Values["Name"]!.GetValue<string>();var fileHash=store.Read(document.Snapshot.Key).Sha256;
        if(dbName!="Saved database fixture"||fileHash!=beforeFileHash)throw new Exception("Mixed-source save boundary lost");
        if(store.Read(clientKey).Exists||!session.Sources.Drafts!.Documents.Single(d=>d.Snapshot.Key==clientKey).IsDirty)throw new Exception("Hidden client draft crossed a failed mixed-save boundary.");
        Button("Save database changes").EmitSignal(BaseButton.SignalName.Pressed);await Frame();if((await session.Client.SendAsync<OperationLink[]>("world.history",new{})).Length!=historyCount)throw new Exception("Database part applied twice.");
        foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,800)}){shell.GetWindow().Size=size;reviewDialog.PopupCentered();await EventGalleries.Capture(shell,"world-source-partial",size);}
        reviewDialog.EmitSignal(AcceptDialog.SignalName.Confirmed);if(await save||!app.HasUnsavedChanges)throw new Exception("Partial save discarded remaining source draft.");await Frame();
        foreach(var pending in session.Sources.History!.List().Where(x=>x.Pending))session.Sources.History.Reconcile(pending.OperationId);
        var retry=app.SaveAsync();await Frame();reviewDialog=shell.GetChildren().OfType<AcceptDialog>().Single(d=>d.Name=="SourceChangeReview");
        if(!Button("Save database changes").Disabled)throw new Exception("Retry includes already saved database mutation.");Button("Apply file changes").EmitSignal(BaseButton.SignalName.Pressed);if(!await retry)throw new Exception("Reviewed source retry failed.");
        if(app.HasUnsavedChanges||store.Read(document.Snapshot.Key).Sha256==beforeFileHash)throw new Exception("Applied source subset stayed dirty or was not persisted.");
        current=await session.Client.SendAsync<WorldRowSnapshot>("world.get",npcKey);await session.Client.SendAsync<WorldSaveResult>("world.save",new WorldChange(Guid.NewGuid(),[new(npcKey,current,null)],[]));
    }
}
