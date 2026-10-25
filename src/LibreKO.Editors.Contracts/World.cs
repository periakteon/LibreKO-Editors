using System.Text.Json.Nodes;

namespace LibreKO.Editors.Contracts;

public enum WorldTable { ZoneInfos, StartPositions, Homes, Npcs, NpcPositions, NpcItems,
    MakeItemGroups, MonsterSummons, ItemExchanges, PetLevels, PetTransforms, GameEvents, MapObjects, EventTriggers, Warps }
public record WorldKey(WorldTable Table, int Id, int? Zone = null, bool? IsMonster = null);
public record WorldRowSnapshot(WorldKey Key, JsonObject Values, string Version);
public record WorldRowChange(WorldKey Key, WorldRowSnapshot? Before, JsonObject? After);
public record WorldMembership(WorldKey Owner, string Relation, IReadOnlyList<WorldKey> BeforeKeys);
public record WorldChange(Guid OperationId, IReadOnlyList<WorldRowChange> Rows, IReadOnlyList<WorldMembership> Memberships);
public record WorldHistoryEntry(WorldChange Change, IReadOnlyList<WorldRowSnapshot> After, string ScopeId);
public record WorldQuery(WorldTable Table, string Text = "", int Page = 0, int Size = 50,
    int? Zone = null, bool? IsMonster = null, int? ParentId = null,int? ShopGroup=null);
public record WorldFieldDescriptor(FieldDescriptor Field, string Label, bool Nullable, int? MaxLength);
public record WorldTableDescriptor(WorldTable Table, IReadOnlyList<string> KeyFields, IReadOnlyList<WorldFieldDescriptor> Fields, JsonObject Defaults);
public record WorldCatalog(IReadOnlyList<WorldTableDescriptor> Tables);
public record WorldSaveResult(SaveStatus Status, IReadOnlyList<WorldRowSnapshot> Rows,
    IReadOnlyList<ValidationIssue>? Issues = null, string? Message = null);
public record WorldTableSupport(WorldTable Table, bool CanBrowse, bool CanSave, string Reason, IReadOnlyList<CompatibilityIssue> Issues);
public record WorldCompatibilityReport(string Database, string ServerVersion, IReadOnlyList<WorldTableSupport> Tables);
