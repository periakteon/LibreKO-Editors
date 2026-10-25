using System.Text.Json.Nodes;
namespace LibreKO.Editors.Contracts;
public enum EventFamily {Temple,CollectionRace,Lottery,CastleSiege,MapEvent,MapObject,NpcTrigger}
public enum EventTable {TempleEventSchedules,TempleEventRewards,ForgottenTempleWaves,CollectionRaces,CollectionRaceSchedules,CollectionRaceObjectives,CollectionRaceRewards,LotteryEvents,LotterySchedules,LotteryRewards,SiegeWarfare,GameEvents,MapObjects,EventTriggers}
public record EventRowKey(EventTable Table,int Id,int? Zone=null);
public record EventDocumentKey(EventFamily Family,int Id,int? Zone=null);
public record EventRowSnapshot(EventRowKey Key,JsonObject Values,string Version);
public record EventDocumentSnapshot(EventDocumentKey Key,IReadOnlyList<EventRowSnapshot> Rows,string Version);
public record EventDocumentSummary(EventDocumentKey Key,string Label,JsonObject Details);
public record EventRowChange(EventRowKey Key,EventRowSnapshot? Before,JsonObject? After);
public record EventChange(Guid OperationId,EventDocumentSnapshot Before,IReadOnlyList<EventRowChange> Rows);
public record EventSaveResult(SaveStatus Status,EventDocumentSnapshot? Document=null,IReadOnlyList<ValidationIssue>? Issues=null,string? Message=null);
public record EventFieldDescriptor(FieldDescriptor Field,string Label,bool Nullable,int? MaxLength=null);
public record EventTableDescriptor(EventTable Table,IReadOnlyList<string> KeyFields,IReadOnlyList<EventFieldDescriptor> Fields,JsonObject Defaults);
public record EventFamilyDescriptor(EventFamily Family,string Label,IReadOnlyList<EventTable> Tables);
public record EventCatalog(IReadOnlyList<EventFamilyDescriptor> Families,IReadOnlyList<EventTableDescriptor> Tables);
public record EventTableSupport(EventTable Table,bool CanBrowse,bool CanSave,string Reason,IReadOnlyList<CompatibilityIssue> Issues);
public record EventCompatibilityReport(string Database,string ServerVersion,IReadOnlyList<EventTableSupport> Tables);

public record EventHistoryEntry(EventChange Change,EventDocumentSnapshot? After);
public record EventHistorySummary(Guid OperationId,EventDocumentKey Document,string Label,bool Pending);
