using System.Text;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
public class QuestManifestTests
{
    [Fact]
    public async Task InvalidBatchGraphPreventsAnyPrepareOrReplacement()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var registry=f.Validators();bool called=false;
        registry.RegisterBatch("fixture-graph",c=>true,async(c,ct)=>{await Task.Yield();called=true;return new ValidationIssue[]{new("include","Invalid graph")};});
        var mutation=store.Stage(f.Key(SourceRoot.ServerData,"first.json"),"[]"u8.ToArray());
        var outcome=await new FileApply(f.Profile,f.WorkspaceRoot,registry).ApplyAsync(new(Guid.NewGuid(),[mutation]));
        Assert.True(called);Assert.Equal(FileApplyState.Invalid,outcome.State);Assert.False(store.Read(mutation.Before.Key).Exists);
    }
    [Fact]
    public void ManifestMembershipIsExplicitAndPreservesOtherEntries()
    {
        var key=new SourceFileKey("fixture",SourceRoot.ServerQuests,"fixture","quest-manifest.json");
        var doc=JsonSourceDocument.Open(new(key,false,"",null),Encoding.UTF8.GetBytes("[\r\n  \"first.quest\",\r\n  \"\\u0073econd.quest\"\r\n]"));
        var original=doc.Render();QuestManifest.Prepare(doc,"first.quest",true);Assert.Equal(original,doc.Render());
        QuestManifest.Prepare(doc,"folder/new.quest",true);Assert.Contains("\\u0073econd.quest",Encoding.UTF8.GetString(doc.Render()));
        QuestManifest.Prepare(doc,"first.quest",false);Assert.Equal(new[]{"second.quest","folder/new.quest"},QuestManifest.Entries(doc));
        Assert.Throws<InvalidDataException>(()=>QuestManifest.Prepare(doc,"../escape.quest",true));
        Assert.Throws<InvalidDataException>(()=>QuestManifest.Prepare(doc,"other.cs",true));
    }
}
