using LibreKO.Editors.Contracts;
using Microsoft.EntityFrameworkCore;
namespace LibreKO.Editors.DataHost.World;
public sealed partial class WorldDatabase
{
    public async Task<Page<PoolConsumer>> PoolConsumersAsync(PoolConsumerQuery query)
    {
        if(query.PoolId is <100 or >=100_000_000)throw new InvalidOperationException("Choose a runtime ordered-pool ID.");
        Require(await CheckCompatibilityAsync(),[WorldTable.NpcItems,WorldTable.Npcs]);await using var db=Open();var id=query.PoolId;
        var groups=await db.NpcItems.AsNoTracking().Where(d=>d.Index>0&&(d.Item1==id||d.Item2==id||d.Item3==id||d.Item4==id||d.Item5==id||d.Item6==id||d.Item7==id)).ToArrayAsync();
        var ids=groups.Select(g=>g.Index).Distinct().ToArray();var owners=await db.Npcs.AsNoTracking().Where(n=>ids.Contains(n.ItemGroup)).ToArrayAsync();
        var rows=new List<PoolConsumer>();
        foreach(var group in groups.OrderBy(g=>g.Index).ThenBy(g=>g.IsMonster))
        {
            var key=new WorldKey(WorldTable.NpcItems,group.Index,IsMonster:group.IsMonster);
            var users=owners.Where(n=>n.ItemGroup==group.Index&&n.IsMonster==group.IsMonster).OrderBy(n=>n.Id).ToArray();
            if(users.Length==0)rows.Add(new(key,null,"No prototype assigned"));
            foreach(var npc in users)rows.Add(new(key,new(WorldTable.Npcs,npc.Id,IsMonster:npc.IsMonster),npc.Name));
            if(rows.Count>1_000_000)throw new InvalidOperationException("Pool consumer lookup exceeds one million references.");
        }
        var page=Math.Clamp(query.Page,0,1_000_000);var size=Math.Clamp(query.Size,1,100);
        return new(rows.Skip(page*size).Take(size).ToArray(),rows.Count,page,size);
    }
}
