using System.Text;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Sources;

public class ServerJsonTests
{
    [Fact] public async Task NewlyDuplicatedPersistedSourceKeysAreRejectedButLegacyDuplicatesSurvive()
    {
        using var f=SourceFixture.Create();var store=new SourceStore(f.Profile,f.WorkspaceRoot);var key=f.Key(SourceRoot.ServerData,"ItemSells.json");const string row="{\"SellingGroup\":1,\"Line\":0,\"Index\":0,\"ItemId\":123450001}";f.Write(key.Root,key.RelativePath,Encoding.UTF8.GetBytes("["+row+"]"));var registry=new SourceValidationRegistry(store.Blobs,f.Profile);ServerJsonCatalog.Register(registry,store.Blobs);ServerJsonCatalog.RegisterIdentityValidation(registry,store);
        var bad=await new FileApply(f.Profile,f.WorkspaceRoot,registry).ApplyAsync(new(Guid.NewGuid(),[store.Stage(key,Encoding.UTF8.GetBytes("["+row+","+row+"]"))]));Assert.Equal(FileApplyState.Invalid,bad.State);
        f.Write(key.Root,key.RelativePath,Encoding.UTF8.GetBytes("["+row+","+row+"]"));var edited=(JsonArray)JsonNode.Parse("["+row+","+row+"]")!;edited[1]!["ItemId"]=123450002;var good=await new FileApply(f.Profile,f.WorkspaceRoot,registry).ApplyAsync(new(Guid.NewGuid(),[store.Stage(key,Encoding.UTF8.GetBytes(edited.ToJsonString()))]));Assert.Equal(FileApplyState.Applied,good.State);
    }
    static JsonSourceDocument Open(SourceFixture f, string path, string text)
    {
        f.Write(SourceRoot.ServerData, path, Encoding.UTF8.GetBytes(text));
        var store = new SourceStore(f.Profile, f.WorkspaceRoot);
        var snapshot = store.Read(f.Key(SourceRoot.ServerData, path));
        return JsonSourceDocument.Open(snapshot, store.Blobs.Read(snapshot.Content!));
    }

    [Fact]
    public void JsonEditPreservesUnrelatedTokensAndNoOpBytes()
    {
        using var f = SourceFixture.Create();
        const string raw = "[\r\n  {\"Name\" : \"before\", \"Exact\":9007199254740993,\"Unknown\": { \"escaped\":\"\\u0061\" },\"Members\":[1,1,2]},\r\n  {\"Name\":\"untouched\"}\r\n]";
        var doc = Open(f, "Npcs.json", raw);
        var input = Encoding.UTF8.GetBytes(raw);
        Assert.Equal(input, doc.Render());
        var token = doc.Rows[0].Token;
        var otherFileToken = token with { File = token.File with { RelativePath = "Homes.json" } };
        Assert.Throws<InvalidOperationException>(() => doc.Set(otherFileToken,"Name",JsonValue.Create("wrong")));
        Assert.Throws<InvalidOperationException>(() => doc.Set(token with { FileHash = "wrong" },"Name",JsonValue.Create("wrong")));
        doc.Set(token, "Name", JsonValue.Create("after"));
        Assert.Equal(raw.Replace("\"before\"", "\"after\""), Encoding.UTF8.GetString(doc.Render()));
        Assert.Equal(9007199254740993L, doc.Root[0]!["Exact"]!.GetValue<long>());
        var precision = Open(f,"Homes.json","{\"Exact\":9007199254740993}");
        Assert.Equal(9007199254740993L, precision.Root["Exact"]!.GetValue<long>());
        // Root and row values are detached; caller mutation cannot bypass dirty tracking.
        doc.Rows[0].Values["Name"] = "bypass";
        Assert.Equal("after", doc.Rows[0].Values["Name"]!.GetValue<string>());
        doc.Set(token,"Name",JsonValue.Create("before"));
        Assert.False(doc.IsDirty);
        Assert.Equal(input,doc.Render());
    }

    [Fact]
    public void OrderedRowsRetainDuplicatesAndTokenIdentityAfterMove()
    {
        using var f = SourceFixture.Create();
        var doc = Open(f,"MakeItemGroups.json","[ {\"GroupNum\":1,\"Items\":\"10,10,20\"}, {\"GroupNum\":2} ]");
        var first = doc.Rows[0].Token;
        doc.Move(first,1);
        doc.Set(first,"Items",JsonValue.Create("10,10,30"));
        Assert.Equal(1,doc.Root[1]!["GroupNum"]!.GetValue<int>());
        doc.Insert(new JsonObject { ["GroupNum"] = 3 },1);
        doc.Remove(first);
        Assert.Equal(new[]{2,3},doc.Rows.Select(r=>r.Values["GroupNum"]!.GetValue<int>()));
        Assert.Throws<InvalidOperationException>(()=>doc.Set(first,"Items",null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonEditsPreserveBomAndUnequalWhitespace(bool utf16)
    {
        using var f=SourceFixture.Create();
        Encoding encoding=utf16?new UnicodeEncoding(false,true,true):new UTF8Encoding(true,true);
        const string raw="[\r\n {\"Name\":\"first\"},\n\t{\"Name\":\"second\"}, {\"Name\":\"third\"}\r\n]";
        var bytes=encoding.GetPreamble().Concat(encoding.GetBytes(raw)).ToArray();
        f.Write(SourceRoot.ServerData,"Npcs.json",bytes); var store=new SourceStore(f.Profile,f.WorkspaceRoot);
        var doc=JsonSourceDocument.Open(store.Read(f.Key(SourceRoot.ServerData,"Npcs.json")),bytes);
        Assert.Equal(bytes,doc.Render()); doc.Set(doc.Rows[1].Token,"Name",JsonValue.Create("changed"));
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(raw.Replace("second","changed"))).ToArray(),doc.Render());
        Assert.Throws<InvalidDataException>(()=>JsonSourceDocument.Open(doc.Snapshot,Encoding.UTF8.GetBytes("[not valid")));
    }

    [Fact]
    public void CodecAuthorizesOnlySupportedTopLevelServerData()
    {
        using var f=SourceFixture.Create(); var blobs=new BlobStore(f.WorkspaceRoot);
        var registry=new SourceValidationRegistry(blobs,f.Profile);
        ServerJsonCatalog.Register(registry,blobs);
        foreach(var table in Enum.GetValues<WorldTable>()) Assert.NotNull(ServerJsonCatalog.Format(table+".json"));
        Assert.NotNull(ServerJsonCatalog.Format("NpcPositions.zone21.json"));
        Assert.Null(ServerJsonCatalog.Format("NpcPositions.evil.json"));
        Assert.Equal(new[]{"SellingGroup","Line","Index"},ServerJsonCatalog.Format("ItemSells.json")!.KeyFields);
        Assert.Throws<InvalidOperationException>(()=>registry.Codec(f.Key(SourceRoot.ClientProject,"Npcs.json")));
        Assert.Throws<InvalidOperationException>(()=>registry.Codec(f.Key(SourceRoot.ServerData,"nested/Npcs.json")));
        Assert.NotEmpty(registry.Validate(new(f.Key(SourceRoot.ServerData,"Npcs.json"),blobs.Put(Encoding.UTF8.GetBytes("[{broken]")))));
        Assert.NotEmpty(registry.Validate(new(f.Key(SourceRoot.ServerData,"ItemSells.json"),blobs.Put(Encoding.UTF8.GetBytes("[{\"SellingGroup\":1,\"Line\":999,\"Index\":0,\"ItemId\":10}]")))));
        Assert.Empty(registry.Validate(new(f.Key(SourceRoot.ServerData,"ItemSells.json"),blobs.Put(Encoding.UTF8.GetBytes("[{\"SellingGroup\":1,\"Line\":2,\"Index\":0,\"ItemId\":10,\"Custom\":true}]")))));
    }
}
