using LibreKO.Editors.Contracts;
using LibreKO.Editors.DataHost.World;
using LibreKO.Editors.Core.Sources;
using System.Text;
using LibreKO.Common.Domain.Entities.GameData;
using Microsoft.EntityFrameworkCore;

[Collection("World database")]
public class ItemOriginTests
{
    [Fact] public async Task PoolConsumersIncludeAllTypedGroupsAndUnassignedDefinitionsWithPaging()
    {
        await WorldFixture.Ensure();await using var db=EventFixture.Open();short unused=30000;
        while(await db.NpcItems.AnyAsync(g=>g.Index==unused))unused++;
        var orphan=new NpcItemData{Index=unused,IsMonster=false,Item1=WorldFixture.PoolId};db.NpcItems.Add(orphan);await db.SaveChangesAsync();
        try
        {
            var service=new WorldDatabase(WorldFixture.Cs,WorldFixture.Scratch());
            var all=await service.PoolConsumersAsync(new(WorldFixture.PoolId,Size:100));
            foreach(var monster in new[]{false,true})Assert.Contains(all.Rows,r=>r.DropGroup==new WorldKey(WorldTable.NpcItems,WorldFixture.DropId,IsMonster:monster)&&r.Owner==new WorldKey(WorldTable.Npcs,WorldFixture.NpcId,IsMonster:monster));
            Assert.Contains(all.Rows,r=>r.DropGroup.Id==unused&&r.Owner==null);
            var page=await service.PoolConsumersAsync(new(WorldFixture.PoolId,Page:1,Size:1));Assert.Equal(all.Total,page.Total);Assert.Equal(all.Rows[1],Assert.Single(page.Rows));
        }
        finally{db.NpcItems.Remove(orphan);await db.SaveChangesAsync();}
    }
    [Fact] public async Task DirectAndPooledDropsRetainTypedOwnersAndDuplicateWeights()
    {
        await WorldFixture.Ensure();var service=new WorldDatabase(WorldFixture.Cs,WorldFixture.Scratch());
        var result=await service.ItemOriginsAsync(new(123450001,Size:100),null);
        foreach(var monster in new[]{false,true})
        {
            var direct=Assert.Single(result.Rows.Where(r=>r.Owner==new WorldKey(WorldTable.Npcs,WorldFixture.NpcId,IsMonster:monster)&&r.Slot==1));
            Assert.Equal("drop",direct.Kind);Assert.Equal(5000,direct.BasisPoints);Assert.Null(direct.PoolId);
            var pool=Assert.Single(result.Rows.Where(r=>r.Owner==direct.Owner&&r.Slot==2));
            Assert.Equal(WorldFixture.PoolId,pool.PoolId);Assert.Equal(2,pool.PoolMatches);Assert.Equal(2,pool.PoolSize);
        }
        Assert.Contains(result.Diagnostics,d=>d.Contains("shop",StringComparison.OrdinalIgnoreCase));
        var page=await service.ItemOriginsAsync(new(123450001,Page:1,Size:1),null);
        Assert.Equal(result.Total,page.Total);Assert.Single(page.Rows);Assert.Equal(result.Rows[1],page.Rows[0]);
        Assert.Empty((await service.ItemOriginsAsync(new(123450001,Page:100,Size:100),null)).Rows);
    }
    [Fact] public async Task MerchantSourceRowsJoinTypedDatabaseOwnersAndReportMissingSource()
    {
        await WorldFixture.Ensure();var root=WorldFixture.Scratch();var data=Path.Combine(root,"data");Directory.CreateDirectory(data);
        var profile=new SourceProfile(Guid.NewGuid().ToString(),"Origins",new Dictionary<SourceRoot,string>{{SourceRoot.ServerData,data}});
        var store=new SourceStore(profile,Path.Combine(root,"workspace"));
        var service=new WorldDatabase(WorldFixture.Cs,root);
        Assert.Contains((await service.ItemOriginsAsync(new(123450001),store)).Diagnostics,d=>d.Contains("ItemSells"));
        File.WriteAllText(Path.Combine(data,"ItemSells.json"),"[{\"SellingGroup\":0,\"Line\":0,\"Index\":0,\"ItemId\":123450001}]",new UTF8Encoding(false));
        var found=await service.ItemOriginsAsync(new(123450001,Size:100),store);
        // A zero selling group cannot open a shop; context must not claim that an NPC sells this item.
        Assert.DoesNotContain(found.Rows,r=>r.Kind=="shop");
        Assert.DoesNotContain(found.Diagnostics,d=>d.Contains("unavailable",StringComparison.OrdinalIgnoreCase));
        await using var db=EventFixture.Open();
        var seller=new NpcData{Id=Random.Shared.Next(700_000_000,800_000_000),IsMonster=false,Name="Origin merchant",SellingGroup=560001,NpcType=NpcData.TypeTradeMerchant};
        var inactive=new NpcData{Id=seller.Id,IsMonster=true,Name="Same number, non-shop role",SellingGroup=560001};
        db.Npcs.AddRange(seller,inactive);await db.SaveChangesAsync();
        try
        {
            File.WriteAllText(Path.Combine(data,"ItemSells.json"),"[{\"SellingGroup\":560001,\"Line\":2,\"Index\":3,\"ItemId\":123450001},{\"SellingGroup\":560001,\"Line\":2,\"Index\":4,\"ItemId\":123450001}]",new UTF8Encoding(false));
            var sellers=(await service.ItemOriginsAsync(new(123450001,Size:100),store)).Rows.Where(r=>r.Kind=="shop").ToArray();
            Assert.Equal(2,sellers.Length);Assert.All(sellers,r=>{Assert.Equal(new WorldKey(WorldTable.Npcs,seller.Id,IsMonster:false),r.Owner);Assert.Equal("Origin merchant",r.OwnerName);Assert.Equal(2,r.ShopLine);Assert.NotNull(r.Source);});
            Assert.Equal(new[]{0,1},sellers.Select(r=>r.SourceRow).ToArray());
            var users=await service.ListAsync(new(WorldTable.Npcs,ShopGroup:560001));Assert.Equal(2,users.Total);Assert.All(users.Rows,r=>Assert.Equal(560001,LibreKO.Editors.Core.FieldValueParser.ReadInt32(r.Values["SellingGroup"])));
        }
        finally{db.Npcs.RemoveRange(seller,inactive);await db.SaveChangesAsync();}
    }
}
