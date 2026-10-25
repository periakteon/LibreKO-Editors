using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Help;

namespace LibreKO.Editors.Core.Tests;

public class HelpCatalogTests
{
    [Theory]
    [InlineData("shops")]
    [InlineData("client")]
    public void MerchantReleaseRequiresTheMatchingClientStockCatalogue(string topic)
    {
        var article=HelpCatalog.All.Single(g=>g.Id==topic).Markdown;
        Assert.Contains("_sell",article);
        Assert.Contains("[ItemId, Line, Index]",article);
        Assert.Contains("rebuild and distribute",article,StringComparison.OrdinalIgnoreCase);
    }
    [Fact] public void GuidesLoadOfflineFromEmbeddedResources()
    {
        Assert.Equal(10,HelpCatalog.All.Count);
        Assert.Equal(HelpCatalog.All.Count,HelpCatalog.All.Select(g=>g.Id).Distinct().Count());
        Assert.All(HelpCatalog.All,g=>{Assert.StartsWith("# ",g.Markdown);Assert.Contains("1. ",g.Markdown);});
    }
    [Fact] public void SearchMatchesAllWordsAcrossTitleAndArticle()
    {
        Assert.Contains(HelpCatalog.Search("ROW COUNT"),g=>g.Id=="database");
        Assert.Contains(HelpCatalog.Search("client verified"),g=>g.Id=="client");
        Assert.Empty(HelpCatalog.Search("no-such-help-topic-93828"));
        Assert.Equal(HelpCatalog.All,HelpCatalog.Search("  "));
    }
    [Theory]
    [InlineData(SourceRoot.ServerData,"ItemSells.json","shops")]
    [InlineData(SourceRoot.ServerData,"MiningExchanges.json","shops")]
    [InlineData(SourceRoot.ServerData,"custom\\NPCPOSITIONS.JSON","seeds")]
    [InlineData(SourceRoot.ServerMaps,"Moradon.smd","maps")]
    [InlineData(SourceRoot.ServerQuests,"quest-manifest.json","quests")]
    [InlineData(SourceRoot.ClientProject,"assets/terrain/custom/height.png","client")]
    [InlineData(SourceRoot.BuildOutput,"output/build-manifest.json","client")]
    [InlineData(SourceRoot.Pipeline,"scripts/export.py","client")]
    public void SourceLinksChooseTheDeploymentDestination(SourceRoot root,string path,string expected)
        =>Assert.Equal(expected,HelpCatalog.ForSource(root,path));
    [Fact] public void MixedChangesLeadToCoordinatedReleaseInsteadOfOneDestination()
    {
        Assert.Equal("release",HelpCatalog.ForChanges([new("a",SourceRoot.ServerData,"b","ItemSells.json"),new("a",SourceRoot.ClientProject,"c","assets/items/icons/1.png")],false));
        Assert.Equal("shops",HelpCatalog.ForChanges([new("a",SourceRoot.ServerData,"b","ItemSells.json")],false));
        Assert.Equal("release",HelpCatalog.ForChanges([new("a",SourceRoot.ServerMaps,"b","map.smd")],true));
        Assert.Equal("database",HelpCatalog.ForChanges([],true));
        Assert.Equal("overview",HelpCatalog.ForChanges([],false));
    }
}
