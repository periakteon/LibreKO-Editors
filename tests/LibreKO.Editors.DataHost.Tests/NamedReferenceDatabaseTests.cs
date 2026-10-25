using System.Text;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.References;
using LibreKO.Editors.DataHost;
using LibreKO.Editors.DataHost.Sources;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Text.Json;

public class NamedReferenceDatabaseTests
{
    [Fact] public async Task AcceptedCustomIndexMetadataCannotDisableNumericModelNames()
    {
        using var f=SourceFixture.Create();f.Write(SourceRoot.ClientProject,"assets/npcs/index.json",Encoding.UTF8.GetBytes("{\"100\":\"shared\",\"custom\":{\"version\":1}}"));var source=new SourceContext();source.Configure(f.Profile,f.WorkspaceRoot);var value=Assert.Single(await source.ResolveNamedAsync(new([new("models",100)],f.Profile.Id)));Assert.Equal("shared",value.Label);Assert.Equal("resolved",value.State);
    }
    static async Task<EditorDatabase> Fixture()
    {
        await EventFixture.Ensure();
        await using var db = EventFixture.Open();
        await db.Npcs.Where(x => x.Id == 880009).ExecuteDeleteAsync();
        db.Npcs.AddRange(new NpcData { Id = 880009, IsMonster = false, Name = "Named fixture merchant", ItemGroup = 8809 },
            new NpcData { Id = 880009, IsMonster = true, Name = "Named fixture monster", ItemGroup = 8809 });
        if (!await db.NpcItems.AnyAsync(x => x.Index == 8809 && x.IsMonster)) db.NpcItems.Add(new() { Index = 8809, IsMonster = true, Item1 = 880140, Percent1 = 5000 });
        if (!await db.MakeItemGroups.AnyAsync(x => x.GroupNum == 880140)) db.MakeItemGroups.Add(new() { GroupNum = 880140, Items = "123450001,123450001,123450002" });
        if (!await db.Magic.AnyAsync(x => x.Id == 988010)) db.Magic.Add(new() { Id = 988010, EnName = "Named fixture magic" });
        await db.SaveChangesAsync();
        return new(EventFixture.Cs, Path.Combine(Path.GetTempPath(), "named-reference-db-" + Guid.NewGuid().ToString("N")));
    }

    [Fact]
    public async Task SameNpcNumberResolvesBothTypesAndSpawnOffset()
    {
        var database = await Fixture();
        var candidates = await database.ResolveNamedAsync(new([new("npcs", 880009)]));
        Assert.True(ReferenceRules.ForSpawn(880009, 0).IsMonster);
        Assert.False(ReferenceRules.ForSpawn(880009, 100).IsMonster);
        Assert.Equal(2, candidates.Count(r => r.Key.Id == 880009));
        Assert.All(candidates, r => Assert.NotNull(r.Key.IsMonster));
        Assert.All(candidates, r => Assert.Equal("ambiguous", r.State));
        var typed = await database.ResolveNamedAsync(new([ReferenceRules.ForSpawn(880009, 0)]));
        Assert.Equal("Named fixture monster", Assert.Single(typed).Label);
        Assert.Equal("resolved", typed[0].State);
        var paged = await database.LookupNamedAsync(new("npcs", "Named fixture", Page: 1, Size: 1));
        Assert.Equal(2, paged.Total);
        Assert.Single(paged.Rows);
        Assert.Equal(880009, paged.Rows[0].Key.Id);
        Assert.Equal(2, (await database.LookupAsync(new("npcs", "880009"))).Total);
    }

    [Fact]
    public async Task ResolveDoesNotTruncateSameNumberCandidatesAtThePageBoundary()
    {
        await EventFixture.Ensure();
        var ids = Enumerable.Range(881100, 51).ToArray();
        await using (var context = EventFixture.Open())
        {
            await context.Npcs.Where(x => ids.Contains(x.Id)).ExecuteDeleteAsync();
            foreach (var id in ids)
                context.Npcs.AddRange(new NpcData { Id = id, IsMonster = false, Name = "Batch NPC" },
                    new NpcData { Id = id, IsMonster = true, Name = "Batch monster" });
            await context.SaveChangesAsync();
        }
        var service = new EditorDatabase(EventFixture.Cs, Path.GetTempPath());
        var result = await service.ResolveNamedAsync(new(ids.Select(x => new ReferenceIdentity("npcs", x)).ToArray()));
        Assert.Equal(102, result.Count);
        Assert.All(result, x => Assert.Equal("ambiguous", x.State));
    }

    [Fact]
    public async Task HelperResolvesSourceNamesBeforeAnyDatabaseConnection()
    {
        using var f = SourceFixture.Create();
        f.Write(SourceRoot.ServerData, "ZoneInfos.json", Encoding.UTF8.GetBytes("[{\"ZoneNo\":211,\"MapName\":\"Offline fixture\"}]"));
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "scripts/build.ps1"))) root = root.Parent;
        Assert.NotNull(root);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(Path.Combine(root!.FullName, "src/LibreKO.Editors.DataHost/bin/Debug/net10.0/LibreKO.Editors.DataHost.dll"));
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        async Task<JsonElement> Send(string method, object payload)
        {
            var id = Guid.NewGuid();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new RpcRequest(1, id, method, JsonSerializer.SerializeToElement(payload, JsonConfig.Options)), JsonConfig.Options));
            var text = await process.StandardOutput.ReadLineAsync(timeout.Token);
            var response = JsonSerializer.Deserialize<RpcResponse>(text!, JsonConfig.Options)!;
            Assert.Equal(id, response.Id); Assert.Null(response.Error);
            return response.Result!.Value;
        }
        try
        {
            await Send("sources.configure", new { profile = f.Profile, workspaceRoot = f.WorkspaceRoot });
            var response = await Send("references.named.resolve", new NamedReferenceResolve([new("zones", 211)], f.Profile.Id));
            Assert.Equal("Offline fixture", response.Deserialize<NamedReference[]>(JsonConfig.Options)![0].Label);
        }
        finally { process.StandardInput.Close(); await process.WaitForExitAsync(timeout.Token); }
    }

    [Fact]
    public async Task TypedGroupsDescribeUsersAndMissingValuesKeepTheRequestedKey()
    {
        var database = await Fixture();
        var groups = await database.ResolveNamedAsync(new([new("drop-groups", 8809, IsMonster: true), new("drop-pools", 880140), new("zones", 210), new("items", 123450001), new("magic", 2147483000), new("magic", 988010)]));
        var drop = groups.Single(x => x.Key.Kind == "drop-groups");
        Assert.True(drop.Key.IsMonster);
        Assert.Contains("Named fixture monster", drop.Label);
        Assert.DoesNotContain("merchant", drop.Label);
        Assert.Contains("3 entries", groups.Single(x => x.Key.Kind == "drop-pools").Label);
        Assert.Equal("Event test zone", groups.Single(x => x.Key.Kind == "zones").Label);
        Assert.NotEmpty(groups.Single(x => x.Key.Kind == "items").Label);
        Assert.Equal("Named fixture magic", groups.Single(x => x.Key.Id == 988010).Label);
        var missing = groups.Single(x => x.Key.Id == 2147483000);
        Assert.Equal(2147483000, missing.Key.Id);
        Assert.Equal("missing", missing.State);
        Assert.All(groups, x => Assert.Equal("Database", x.Source));
    }

    [Fact]
    public async Task MissingClientRootDoesNotBlockOtherServerSourceNames()
    {
        using var f = SourceFixture.Create();
        f.Write(SourceRoot.ServerData, "Npcs.json", Encoding.UTF8.GetBytes("[{\"Id\":880009,\"IsMonster\":true,\"Name\":\"Available server creature\"}]"));
        var client = f.Profile.Roots[SourceRoot.ClientProject]; Directory.Move(client, client + "-offline");
        var sources = new SourceContext(); sources.Configure(f.Profile, f.WorkspaceRoot);
        Assert.Equal("Available server creature", Assert.Single(await sources.ResolveNamedAsync(new([new("npcs", 880009, IsMonster: true)], f.Profile.Id))).Label);
        Assert.Equal("unavailable", Assert.Single(await sources.ResolveNamedAsync(new([new("models", 17)], f.Profile.Id))).State);
    }

    [Fact]
    public async Task SourceNamesFollowSeedCaseRulesAndRejectAnInvalidType()
    {
        using var f = SourceFixture.Create();
        f.Write(SourceRoot.ServerData, "Npcs.json", Encoding.UTF8.GetBytes("[{\"id\":880009,\"ismonster\":1,\"name\":\"Flexible seed creature\"}]"));
        var source = new SourceContext(); source.Configure(f.Profile, f.WorkspaceRoot);
        var valid = Assert.Single(await source.ResolveNamedAsync(new([new("npcs", 880009, IsMonster: true)], f.Profile.Id)));
        Assert.Equal("Flexible seed creature", valid.Label);
        Assert.Equal("resolved", valid.State);
        f.Write(SourceRoot.ServerData, "Npcs.json", Encoding.UTF8.GetBytes("[{\"Id\":880009,\"IsMonster\":\"unknown\",\"Name\":\"Invalid type\"}]"));
        source.Configure(f.Profile, f.WorkspaceRoot);
        var invalid = Assert.Single(await source.ResolveNamedAsync(new([new("npcs", 880009, IsMonster: false)], f.Profile.Id)));
        Assert.Equal("unavailable", invalid.State);
    }

    [Fact]
    public async Task FileNamesStaySeparateFromDatabaseAndWorkOffline()
    {
        using var f = SourceFixture.Create();
        f.Write(SourceRoot.ServerData, "Npcs.json", Encoding.UTF8.GetBytes("[{\"Id\":880009,\"IsMonster\":true,\"Name\":\"Source creature\"},{\"Id\":880009,\"IsMonster\":false,\"Name\":\"Source merchant\"}]"));
        f.Write(SourceRoot.ServerData, "ZoneInfos.json", Encoding.UTF8.GetBytes("[{\"ZoneNo\":210,\"MapName\":\"Source zone\"}]"));
        f.Write(SourceRoot.ServerData, "ItemSells.json", Encoding.UTF8.GetBytes("[{\"SellingGroup\":700,\"Line\":0,\"Index\":0,\"ItemId\":123450001}]"));
        f.Write(SourceRoot.ClientProject, "assets/npcs/index.json", Encoding.UTF8.GetBytes("{\"17\":\"custom_fixture_model\"}"));
        var sources = new SourceContext();
        sources.Configure(f.Profile, f.WorkspaceRoot);
        var offline = new NamedReferenceService(null, sources);
        var file = Assert.Single(await offline.ResolveNamedAsync(new([new("npcs", 880009, IsMonster: true)], f.Profile.Id)));
        Assert.Equal("Source creature", file.Label);
        Assert.Contains("Server file", file.Source);
        var groups = await offline.LookupNamedAsync(new("shops", "700", SourceProfileId: f.Profile.Id));
        Assert.Contains("1 entries", Assert.Single(groups.Rows).Label);
        var models = await offline.ResolveNamedAsync(new([new("models", 17)], f.Profile.Id));
        Assert.Equal("custom_fixture_model", Assert.Single(models).Label);
        var database = await Fixture();
        var connected = new NamedReferenceService(database, sources);
        Assert.Equal("Named fixture monster", Assert.Single(await connected.ResolveNamedAsync(new([new("npcs", 880009, IsMonster: true)]))).Label);
        Assert.Equal("Source creature", Assert.Single(await connected.ResolveNamedAsync(new([new("npcs", 880009, IsMonster: true)], f.Profile.Id))).Label);
        var unavailable = Assert.Single(await new NamedReferenceService(null, new()).ResolveNamedAsync(new([new("npcs", 880009)])));
        Assert.Equal("unavailable", unavailable.State);
    }
}
