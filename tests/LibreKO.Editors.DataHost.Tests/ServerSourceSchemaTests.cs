using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
using LibreKO.Editors.DataHost.Sources;
using LibreKO.Editors.DataHost.World;

public class ServerSourceSchemaTests
{
    [Fact]
    public void SourceSchemaMatchesEveryMappedAndFileOnlyPropertyOffline()
    {
        foreach(var table in Enum.GetValues<WorldTable>())
            Assert.Equal(WorldSchema.Catalog().Tables.Single(x=>x.Table==table).Fields,ServerSourceSchema.Fields(table+".json"));
        var expected=new Dictionary<string,Type>
        {
            ["ItemSells"]=typeof(LibreKO.Common.Domain.Entities.GameData.ItemSellData),
            ["ItemExchangeExps"]=typeof(LibreKO.Common.Domain.Entities.GameData.ItemExchangeExpData),
            ["MiningExchanges"]=typeof(LibreKO.Common.Domain.Entities.GameData.MiningExchangeData),
            ["MiningFishingItems"]=typeof(LibreKO.Common.Domain.Entities.GameData.MiningFishingItemData)
        };
        foreach(var pair in expected)
            Assert.Equal(pair.Value.GetProperties().Where(x=>x.CanWrite).Select(x=>x.Name).Order(),ServerSourceSchema.Fields(pair.Key+".json").Select(x=>x.Field.Name).Order());
    }
}
