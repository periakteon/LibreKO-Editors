using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Sources;
using System.Text.Json.Nodes;
namespace LibreKO.Editors.Desktop;
public static class NpcContentsChecks
{
    public static async Task Run(EditorShell shell)
    {
        _=SourceChecks.Marker(shell);
        var shopKey=shell.Session.Sources.Store!.Key(SourceRoot.ServerData,"ItemSells.json");
        var shopPath=SourcePaths.Resolve(shell.Session.Sources.Profile!,shopKey,true);
        var original="[{\"SellingGroup\":560001,\"Line\":0,\"Index\":0,\"ItemId\":123450001,\"custom\":9007199254740993},{\"SellingGroup\":560001,\"Line\":0,\"Index\":1,\"ItemId\":123450001}]"u8.ToArray();
        File.WriteAllBytes(shopPath,original);
        await shell.RequestWorkspaceAsync("world");var app=(WorldWorkspace)shell.Active!;
        await app.SelectAsync(new(WorldTable.Npcs,880019,IsMonster:true));await app.PreviewReady;
        var board=app.FindChild("NpcContents",true,false);
        if(board==null)throw new Exception("NPC visual shop and loot board is missing.");
        if(!app.MapView.Visible)throw new Exception("NPC board lost its 3D prototype context.");
        var contents=app.Contents;await contents.ReadyTask;app.ShowContents();contents.ChooseMode(0);
        var dropKey=contents.DropKey!;var clean=await shell.Session.Client.SendAsync<WorldRowSnapshot>("world.get",dropKey);
        if(app.Draft!.Rows.Count!=1)throw new Exception("Viewing contents adopted unrelated records into the editable draft.");
        var from=FieldValueParser.ReadInt32(clean.Values["Item1"]);var chance=FieldValueParser.ReadInt32(clean.Values["Percent1"]);
        var target=contents.FindChild("BoardTile_drop_2",true,false) as ItemBoardTile??throw new Exception("Loot slot icons missing.");
        var payload=contents.DragPayload("drop",1,from);
        if(!target._CanDropData(Vector2.Zero,payload))throw new Exception("Valid loot drag refused.");
        target._DropData(Vector2.Zero,payload);
        var changed=app.Draft.Rows.Single(r=>r.Key==dropKey);
        if(FieldValueParser.ReadInt32(changed.Values["Item2"])!=from||FieldValueParser.ReadInt32(changed.Values["Percent2"])!=chance)throw new Exception("Loot drag lost its item or probability.");
        if(contents.CanDrop("drop",1,payload))throw new Exception("Stale drag payload remained valid after editing.");
        app.Undo(false);await contents.ReadyTask;contents.ChooseMode(0);contents.SelectSlot(3);
        if(!await contents.SetItemAsync(123450001)||FieldValueParser.ReadInt32(app.Draft.Rows.Single(r=>r.Key==dropKey).Values["Item3"])!=123450001)throw new Exception("Palette assignment did not change the database draft.");
        var draftFingerprint=RecordFingerprint.Compute(app.Draft.Rows.Single(r=>r.Key==dropKey).Values);
        if(await contents.SetItemAsync(1999999999)||draftFingerprint!=RecordFingerprint.Compute(app.Draft.Rows.Single(r=>r.Key==dropKey).Values))throw new Exception("Missing item was assigned or changed the draft.");
        await contents.OpenPoolAsync(881400);if(contents.FindChildren("BoardTile_pool_*","",true,false).OfType<ItemBoardTile>().Count()!=2)throw new Exception("Ordered duplicate pool icons missing.");
        app.SetValue(app.Selected!,"SellingGroup",JsonValue.Create(560001));await contents.ReadyTask;contents.ChooseMode(1);
        var shop=contents.ShopDocument??throw new Exception("Shop source draft not loaded.");
        // Open the same source in its structured form first: board edits must refresh that existing input owner.
        app.ShowSources();await app.SourceView!.OpenAsync(shopKey);app.ShowDatabase();app.ShowContents();
        payload=contents.DragPayload("shop",0,123450001);await contents.DropAsync("shop",2,payload);
        var moved=shop.Json().Rows.Single(r=>FieldValueParser.ReadInt32(r.Values["Index"])==2);
        if(!shop.IsDirty||moved.Values["custom"]?.ToString()!="9007199254740993"||!File.ReadAllBytes(shopPath).AsSpan().SequenceEqual(original))throw new Exception("Shop drag lost custom data or edited the original file directly.");
        shell.Session.Sources.CaptureDrafts();
        if(!shop.Json().Rows.Any(r=>FieldValueParser.ReadInt32(r.Values["Index"])==2))throw new Exception("Structured source owner overwrote visual board changes.");
        var shopEdited=shop.Render();app.Undo(false);if(shop.IsDirty)throw new Exception("Shop Undo did not restore its source draft.");app.Undo(true);if(!shop.Render().AsSpan().SequenceEqual(shopEdited))throw new Exception("Shop Redo did not restore the visual move.");
        foreach(var size in new[]{new Vector2I(1500,950),new Vector2I(1280,800),new Vector2I(1280,760)}){shell.GetWindow().Size=size;contents.ChooseMode(0);await EventGalleries.Capture(shell,"npc-loot-board",size);contents.ChooseMode(1);await EventGalleries.Capture(shell,"npc-shop-board",size);}
        var live=await shell.Session.Client.SendAsync<WorldRowSnapshot>("world.get",dropKey);
        if(live.Version!=clean.Version)throw new Exception("Visual board modified the database without Save.");
        app.Discard();GD.Print("3D NPC shop/loot boards, drag guards, pool icons, item validation, drafts and original preservation verified");
    }
}
