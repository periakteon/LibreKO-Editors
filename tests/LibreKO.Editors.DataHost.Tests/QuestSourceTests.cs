using System.Text;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;
using LibreKO.Editors.DataHost.Sources;
public class QuestSourceTests
{
    const string Valid="Bind Npc 9\nOn greeting\n    Say \"Welcome\"\n    Topic \"Close\" goto close\n";
    [Fact]
    public void QuestDraftCompilesWithoutDatabaseOrExecutingScript()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);
        f.Write(SourceRoot.ServerData,"Npcs.json","[{\"Id\":9,\"IsMonster\":false,\"Name\":\"Fixture guide\"}]"u8.ToArray());
        f.Write(SourceRoot.ServerQuests,"quest-manifest.json","[\"npc.quest\"]"u8.ToArray());
        f.Write(SourceRoot.ServerQuests,"npc.quest",Encoding.UTF8.GetBytes(Valid));
        var key=f.Key(SourceRoot.ServerQuests,"npc.quest");var before=store.Read(key);
        SourceValidationTicket Ticket(string path,string text)=>new(f.Key(SourceRoot.ServerQuests,path),store.Blobs.Put(Encoding.UTF8.GetBytes(text)));
        var validator=new QuestValidation(store);var validIssues=validator.Validate([Ticket("npc.quest",Valid)]);Assert.Empty(validIssues);
        var invalidIssues=validator.Validate([Ticket("npc.quest","Bind Npc 9\nOn greeting\n    gibberish()")]);Assert.NotEmpty(invalidIssues);Assert.Contains(invalidIssues,i=>i.Field.Contains("npc.quest:"));
        Assert.NotEmpty(validator.Validate([Ticket("npc.quest","include missing\n"+Valid)]));
        Assert.NotEmpty(validator.Validate([Ticket("npc.quest","include ../escape\n"+Valid)]));
        Assert.NotEmpty(validator.Validate([Ticket("npc.quest","include lib\n"+Valid),Ticket("lib.quest","include npc\n")]));
        Assert.NotEmpty(validator.Validate([Ticket("npc.quest","include lib\n"+Valid),Ticket("lib.quest","broken declaration ???")]));
        Assert.Empty(validator.Validate([Ticket("npc.quest","include lib\n"+Valid),Ticket("lib.quest","")]));
        f.Write(SourceRoot.ServerQuests,"lib.quest",Encoding.UTF8.GetBytes(""));var audit=validator.Audit([Ticket("npc.quest","include lib\n"+Valid)]);Assert.Empty(audit.Issues);Assert.Contains(audit.Dependencies,d=>d.Key.RelativePath=="lib.quest"&&d.Exists);
        var receipt=System.Text.Json.JsonSerializer.Deserialize<SourceValidationAudit>(System.Text.Json.JsonSerializer.Serialize(audit,JsonConfig.Options),JsonConfig.Options)!;f.Write(SourceRoot.ServerQuests,"lib.quest",Encoding.UTF8.GetBytes("\n"));Assert.ThrowsAny<InvalidOperationException>(()=>SourceReadScope.Verify(store,receipt.Dependencies));
        Assert.NotEmpty(validator.Validate([Ticket("quest-manifest.json","[\"../escape.quest\"]")]));
        Assert.NotEmpty(validator.Validate([Ticket("quest-manifest.json","[\"NPC.quest\",\"npc.quest\"]")]));
        Assert.NotEmpty(validator.Validate([], [key]));
        Assert.Empty(validator.Validate([Ticket("quest-manifest.json","[]")],[key]));
        Assert.Equal(before.Sha256,store.Read(before.Key).Sha256);
        Assert.Contains("LibreKO.Quests",typeof(QuestValidation).Assembly.GetReferencedAssemblies().Select(x=>x.Name));
    }
}
