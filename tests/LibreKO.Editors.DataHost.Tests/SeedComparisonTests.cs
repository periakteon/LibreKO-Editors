using System.Text;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.DataHost.World;

public class SeedComparisonTests
{
    [Fact]
    public void SourceComparisonCreatesCompleteTypedDraftWithoutWriting()
    {
        var bytes=Encoding.UTF8.GetBytes("[{\"Level\":3,\"Exp\":\"9007199254740993\",\"Custom\":true}]");
        var key=new SourceFileKey("fixture",SourceRoot.ServerData,"fixture","PetLevels.json");
        var doc=JsonSourceDocument.Open(new(key,false,"",null),bytes);
        var target=new WorldKey(WorldTable.PetLevels,88);
        Assert.Throws<InvalidOperationException>(()=>SeedComparison.PrepareDatabaseUpdate(doc,doc.Rows[0].Token,null,target));
        var change=SeedComparison.PrepareDatabaseUpdate(doc,doc.Rows[0].Token,null,target,WorldSchema.Catalog());
        Assert.Equal(9007199254740993L,change.Rows[0].After!["Exp"]!.GetValue<long>());
        Assert.Equal(88,change.Rows[0].After!["Level"]!.GetValue<int>());
        Assert.DoesNotContain("Custom",change.Rows[0].After!.Select(x=>x.Key));
        Assert.Empty(WorldRules.Validate(change,WorldSchema.Catalog())); Assert.False(doc.IsDirty);
    }
}
