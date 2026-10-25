using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.Core.Sources;
public class ItemBoardEditTests
{
    [Theory]
    [InlineData("drop",500,true)]
    [InlineData("shop",500,false)]
    [InlineData("pool",500,false)]
    [InlineData("palette",500,false)]
    [InlineData("drop",1,false)]
    public void PoolInterpretationDependsOnTheConsumingSlot(string kind,int id,bool expected)
    {Assert.Equal(expected,ItemBoardEdits.IsPoolReference(kind,id));}
    [Fact] public void DropSlotSwapMovesChanceInOneUndoStep()
    {
        var key=new WorldKey(WorldTable.NpcItems,7,IsMonster:true);
        var values=new JsonObject{["Index"]=7,["IsMonster"]=true,["Item1"]=123450001,["Percent1"]=5000,["Item2"]=881400,["Percent2"]=10000};
        var draft=new WorldDraft([new(key,values,"version")]);
        ItemBoardEdits.SwapDropSlots(draft,key,1,2);
        Assert.Equal(881400,FieldValueParser.ReadInt32(draft.Rows[0].Values["Item1"]));Assert.Equal(10000,FieldValueParser.ReadInt32(draft.Rows[0].Values["Percent1"]));
        draft.Undo(false);Assert.False(draft.IsDirty);
        Assert.Throws<InvalidOperationException>(()=>ItemBoardEdits.SwapDropSlots(draft,key,0,2));Assert.False(draft.IsDirty);
    }
    [Fact] public void ShopSwapPreservesUnknownSourceDataAndClearRemovesOnlySelectedSlot()
    {
        var key=new SourceFileKey("p",SourceRoot.ServerData,"root","ItemSells.json");
        var bytes="[{\"SellingGroup\":12,\"Line\":0,\"Index\":0,\"ItemId\":123450001,\"custom\":9007199254740993},{\"SellingGroup\":12,\"Line\":0,\"Index\":1,\"ItemId\":123450002}]"u8.ToArray();
        var document=new SourceDocument(new(key,false,"",null),bytes);var json=document.Json();
        ItemBoardEdits.SwapShopSlots(json,12,0,0,0,1);
        Assert.Equal(123450002,FieldValueParser.ReadInt32(json.Rows.Single(r=>FieldValueParser.ReadInt32(r.Values["Index"])==0).Values["ItemId"]));Assert.Equal("9007199254740993",json.RowAt(0).Values["custom"]!.ToString());
        ItemBoardEdits.SetShopSlot(json,12,0,1,0);Assert.Equal(1,json.RowCount);
        ItemBoardEdits.SetShopSlot(json,12,1,2,123450001);Assert.Equal(2,json.RowCount);Assert.Equal(1,FieldValueParser.ReadInt32(json.RowAt(1).Values["Line"]));
        Assert.Throws<InvalidOperationException>(()=>ItemBoardEdits.SetShopSlot(json,12,256,0,123450001));
    }
    [Fact] public void AmbiguousShopSlotCannotBeSilentlyReplaced()
    {
        var key=new SourceFileKey("p",SourceRoot.ServerData,"root","ItemSells.json");
        var json=new SourceDocument(new(key,false,"",null),"[{\"SellingGroup\":12,\"Line\":0,\"Index\":0,\"ItemId\":123450001},{\"SellingGroup\":12,\"Line\":0,\"Index\":0,\"ItemId\":123450002}]"u8.ToArray()).Json();
        Assert.Throws<InvalidOperationException>(()=>ItemBoardEdits.SetShopSlot(json,12,0,0,123450003));Assert.Equal(2,json.RowCount);
    }
    [Fact] public void PoolMoveRetainsDuplicateAndOpaqueTokens()
    {
        Assert.Equal(" 1,,1,bad",ItemBoardEdits.MovePoolEntry(" 1,1,,bad",1,2));
        Assert.Equal("1,1",ItemBoardEdits.RemovePoolEntry("1,2,1",1));
    }
    [Fact] public void ShopMoveIntoEmptyPositionKeepsTheWholeOpaqueRow()
    {
        var key=new SourceFileKey("p",SourceRoot.ServerData,"root","ItemSells.json");
        var json=new SourceDocument(new(key,false,"",null),"[{\"SellingGroup\":12,\"Line\":0,\"Index\":0,\"ItemId\":123450001,\"custom\":9007199254740993}]"u8.ToArray()).Json();
        ItemBoardEdits.SwapShopSlots(json,12,0,0,1,3);
        Assert.Single(json.Rows);Assert.Equal("9007199254740993",json.RowAt(0).Values["custom"]?.ToString());Assert.Equal(1,FieldValueParser.ReadInt32(json.RowAt(0).Values["Line"]));Assert.Equal(3,FieldValueParser.ReadInt32(json.RowAt(0).Values["Index"]));
    }
}
