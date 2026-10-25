using System.Text.Json;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.DataHost;
using LibreKO.Editors.DataHost.Events;
using LibreKO.Editors.DataHost.Sources;
using LibreKO.Editors.DataHost.World;
EditorDatabase? database=null;
EventDatabase? events=null;
WorldDatabase? world=null;
var sources=new SourceContext();
DockerRelay? relay=null;
string? dockerConnection=null,dockerContainerId=null;
var input=new BufferedStream(Console.OpenStandardInput());
while(await Protocol.ReadFrameAsync(input) is {} line){
 RpcRequest? request=null;RpcResponse response;
 try{request=JsonSerializer.Deserialize<RpcRequest>(line,JsonConfig.Options)!;if(request.Version!=1)throw new InvalidDataException("Unsupported protocol version");
 object result;
 if(request.Method=="ping")result=new{ready=true};
 else if(request.Method=="docker.discover")result=await DockerDiscovery.DiscoverAsync();
 else if(request.Method=="docker.prepare"){var id=request.Payload.GetProperty("containerId").GetString();var selected=(await DockerDiscovery.DiscoverAsync()).SingleOrDefault(c=>c.ContainerId==id)??throw new InvalidOperationException("The database container is no longer running.");relay?.Dispose();relay=null;if(selected.NeedsRelay){relay=new DockerRelay(selected.ContainerId);selected=selected with{ConnectionString=relay.ConnectionString(selected)};}dockerConnection=selected.ConnectionString;dockerContainerId=selected.ContainerId;result=selected;}
 else if(request.Method=="connect"){var cs=request.Payload.GetProperty("connectionString").GetString()!;var workspace=request.Payload.GetProperty("workspaceRoot").GetString()!;var builder=new MySqlConnector.MySqlConnectionStringBuilder(cs);
 var identity=ConnectionIdentity.For(cs,dockerConnection,dockerContainerId);
 var root=DatabaseHistory.DirectoryFor(workspace,identity);var next=new EditorDatabase(cs,root);var compatibility=await next.ConnectAsync();result=new ConnectionState(root,builder.Database,compatibility);database=next;events=new(cs,root);world=new(cs,root);}
 else if(request.Method=="schema")result=FieldSchema.For(request.Payload.GetProperty("kind").Deserialize<RecordKind>());
 else if(request.Method=="world.catalog")result=WorldSchema.Catalog();
 else if(request.Method=="sources.configure"){sources.Configure(request.Payload.GetProperty("profile").Deserialize<SourceProfile>(JsonConfig.Options)!,request.Payload.GetProperty("workspaceRoot").GetString()!);result=new{ready=true};}
 else if(request.Method=="sources.clear"){sources.Clear();result=new{ready=true};}
 else if(request.Method=="sources.quest.validate")result=new QuestValidation(sources.Store??throw new InvalidOperationException("Select source roots first.")).Validate(request.Payload.GetProperty("tickets").Deserialize<SourceValidationTicket[]>(JsonConfig.Options)!,request.Payload.TryGetProperty("deleted",out var deletedQuests)?deletedQuests.Deserialize<SourceFileKey[]>(JsonConfig.Options):null);
 else if(request.Method=="sources.quest.audit")result=new QuestValidation(sources.Store??throw new InvalidOperationException("Select source roots first.")).Audit(request.Payload.GetProperty("tickets").Deserialize<SourceValidationTicket[]>(JsonConfig.Options)!,request.Payload.TryGetProperty("deleted",out var auditDeleted)?auditDeleted.Deserialize<SourceFileKey[]>(JsonConfig.Options):null);
 else if(request.Method=="references.named.list")result=await new NamedReferenceService(database,sources).LookupNamedAsync(request.Payload.Deserialize<NamedReferenceQuery>(JsonConfig.Options)!);
 else if(request.Method=="references.named.resolve")result=await new NamedReferenceService(database,sources).ResolveNamedAsync(request.Payload.Deserialize<NamedReferenceResolve>(JsonConfig.Options)!);
 else if(database==null)throw new InvalidOperationException("Connect to a database first.");
 else result=request.Method switch{
 "world.compatibility"=>await world!.CheckCompatibilityAsync(),
 "world.list"=>await world!.ListAsync(request.Payload.Deserialize<WorldQuery>(JsonConfig.Options)!),
 "world.item-origins"=>await world!.ItemOriginsAsync(request.Payload.Deserialize<ItemOriginQuery>(JsonConfig.Options)!,sources.Store),
 "world.pool-consumers"=>await world!.PoolConsumersAsync(request.Payload.Deserialize<PoolConsumerQuery>(JsonConfig.Options)!),
 "world.get"=>(object?)await world!.GetAsync(request.Payload.Deserialize<WorldKey>(JsonConfig.Options)!)??new{},
 "world.get-batch"=>await world!.GetBatchAsync(request.Payload.Deserialize<WorldKey[]>(JsonConfig.Options)!),
 "world.next-id"=>await world!.NextIdAsync(request.Payload.GetProperty("table").Deserialize<WorldTable>(),NullableInt(request.Payload,"zone"),request.Payload.TryGetProperty("isMonster",out var monsterScope)&&monsterScope.ValueKind!=JsonValueKind.Null?monsterScope.GetBoolean():null,request.Payload.TryGetProperty("reserved",out var worldReserved)?worldReserved.Deserialize<int[]>():null),
 "world.membership"=>await world!.MembershipAsync(request.Payload.GetProperty("owner").Deserialize<WorldKey>(JsonConfig.Options)!,request.Payload.GetProperty("relation").GetString()!),
 "world.save"=>await world!.SaveAsync(request.Payload.Deserialize<WorldChange>(JsonConfig.Options)!),
 "world.history"=>await world!.HistoryAsync(),
 "world.map-context"=>await world!.MapContextAsync(request.Payload.GetProperty("zone").GetInt32(),sources.Store),
 "world.history.get"=>await world!.HistoryEntryAsync(request.Payload.GetProperty("id").GetGuid()),
 "world.reconcile"=>await world!.ReconcileAsync(request.Payload.GetProperty("id").GetGuid()),
 "world.replay"=>await world!.ReplayAsync(request.Payload.GetProperty("id").GetGuid(),request.Payload.GetProperty("reverse").GetBoolean()),
 "events.diagnostics"=>await events!.DiagnosticsAsync(request.Payload.Deserialize<EventDocumentKey>(JsonConfig.Options)!),
 "events.history"=>await events!.HistoryAsync(),
 "events.history.get"=>await events!.HistoryEntryAsync(request.Payload.GetProperty("id").GetGuid()),
 "events.reconcile"=>await events!.ReconcileAsync(request.Payload.GetProperty("id").GetGuid()),
 "events.replay"=>await events!.ReplayAsync(request.Payload.GetProperty("id").GetGuid(),request.Payload.GetProperty("reverse").GetBoolean()),
 "events.save"=>await events!.SaveAsync(request.Payload.Deserialize<EventChange>(JsonConfig.Options)!),
 "events.duplicate"=>await events!.PlanDuplicateAsync(request.Payload.GetProperty("source").Deserialize<EventDocumentKey>(JsonConfig.Options)!,request.Payload.GetProperty("newId").GetInt32(),request.Payload.TryGetProperty("zone",out var duplicateZone)&&duplicateZone.ValueKind!=JsonValueKind.Null?duplicateZone.GetInt32():null),
 "events.catalog"=>events!.Catalog(),
 "events.compatibility"=>await events!.CheckCompatibilityAsync(),
 "events.list"=>await events!.ListAsync(request.Payload.GetProperty("family").Deserialize<EventFamily>(),request.Payload.GetProperty("query").Deserialize<RecordQuery>(JsonConfig.Options)!),
 "events.get"=>(object?)await events!.GetAsync(request.Payload.Deserialize<EventDocumentKey>(JsonConfig.Options)!)??new{},
 "events.next-id"=>await events!.NextIdAsync(request.Payload.GetProperty("table").Deserialize<EventTable>(),request.Payload.TryGetProperty("zone",out var zone)&&zone.ValueKind!=JsonValueKind.Null?zone.GetInt32():null,request.Payload.TryGetProperty("reserved",out var reserved)?reserved.Deserialize<int[]>():null),
 "compatibility.schema"=>await database.CheckCompatibilityAsync(),
 "compatibility.items"=>await database.ScanItemsAsync(Cursor(request.Payload)),
 "compatibility.inventories"=>await database.ScanInventoriesAsync(Cursor(request.Payload)),
 "references.list"=>await database.LookupAsync(request.Payload.Deserialize<ReferenceQuery>(JsonConfig.Options)!),
 "references.resolve"=>await ResolveReferences(database,request.Payload.Deserialize<ReferenceResolve>(JsonConfig.Options)!),
 "items.list"=>await database.ListAsync(RecordKind.Item,request.Payload.Deserialize<RecordQuery>(JsonConfig.Options)!),
 "characters.list"=>await database.ListAsync(RecordKind.Character,request.Payload.Deserialize<RecordQuery>(JsonConfig.Options)!),
 "accounts.list"=>await database.ListAsync(RecordKind.Account,request.Payload.Deserialize<RecordQuery>(JsonConfig.Options)!),
 "items.get"=>(object?)await database.GetAsync(new(RecordKind.Item,request.Payload.GetProperty("id").GetInt32()))??new{},
 "characters.get"=>(object?)await database.GetAsync(new(RecordKind.Character,request.Payload.GetProperty("id").GetInt32()))??new{},
 "accounts.get"=>(object?)await database.GetAsync(new(RecordKind.Account,request.Payload.GetProperty("id").GetInt32()))??new{},
 "items.save" or "characters.save" or "accounts.save"=>await SaveRecord(database,request.Method,request.Payload.Deserialize<RecordChange>(JsonConfig.Options)!),
 _=>throw new InvalidOperationException("Unsupported request.")};
 response=new(1,request.Id,JsonSerializer.SerializeToElement(result,JsonConfig.Options),null);
 }catch(Exception e){var message=e is MySqlConnector.MySqlException my?$"Database operation failed ({my.Number}). Check the connection and schema.":e is JsonException?"Invalid request value.":e.Message;response=new(1,request?.Id??Guid.Empty,null,new("request",message));}
 Console.WriteLine(JsonSerializer.Serialize(response,JsonConfig.Options));
}
relay?.Dispose();

static int? Cursor(JsonElement payload)=>payload.TryGetProperty("afterId",out var value)&&value.ValueKind!=JsonValueKind.Null?value.GetInt32():null;
static int? NullableInt(JsonElement payload,string name)=>payload.TryGetProperty(name,out var value)&&value.ValueKind!=JsonValueKind.Null?value.GetInt32():null;

static async Task<IReadOnlyList<ReferenceEntry>> ResolveReferences(EditorDatabase database,ReferenceResolve request)=>(await database.LookupAsync(new(request.Kind,Size:100,Context:request.Context),request.Ids)).Rows;
static Task<SaveResult> SaveRecord(EditorDatabase database,string method,RecordChange change){var expected=method switch{"items.save"=>RecordKind.Item,"accounts.save"=>RecordKind.Account,_=>RecordKind.Character};if(change.Before.Key.Kind!=expected)throw new InvalidOperationException("Record type does not match the save request.");return database.SaveAsync(change);}
