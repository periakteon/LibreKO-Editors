using System.Text.Json;
using System.Text.Json.Nodes;
namespace LibreKO.Editors.Contracts;
public enum RecordKind { Item, Character, Account }
public record RecordKey(RecordKind Kind, int Id);
public record RecordSummary(RecordKey Key, string Label, JsonObject Details);
public record Page<T>(IReadOnlyList<T> Rows, int Total, int Number, int Size);
public record RecordQuery(string Text = "", int Page = 0, int Size = 50, JsonObject? Filters = null);
public record InventorySlotDto(int Index, int ItemId, short Durability, ushort Count, byte Flag, long ExpiresAt, int UniqueId);
public record RecordSnapshot(RecordKey Key, JsonObject Values, string Version, byte? Nation = null, bool IsOnline = false, IReadOnlyList<InventorySlotDto>? Inventory = null);
public enum FieldKind { Text, Integer, Decimal, Boolean, Enum, ReadOnly, DateTime, DateOnly }
public record FieldDescriptor(string Name, string Group, FieldKind Kind, bool Writable, decimal? Minimum = null, decimal? Maximum = null, IReadOnlyDictionary<string,string>? Choices = null, string? Reference = null);
public record ReferenceEntry(int Id,string Label,JsonObject? Details=null);
public record InventoryItemFilter(int Slot,int OtherHandItemId=0);
public record ReferenceQuery(string Kind,string Text="",int Page=0,int Size=50,int? Context=null,InventoryItemFilter? Inventory=null);
public record ReferenceResolve(string Kind,IReadOnlyList<int> Ids,int? Context=null);
public record ConnectionState(string HistoryRoot,string Database,DatabaseCompatibility? Compatibility=null);
public record ValidationIssue(string Field, string Message);
public record RecordChange(Guid OperationId, RecordSnapshot Before, JsonObject ChangedValues, IReadOnlyList<InventorySlotDto>? Inventory = null, bool Create = false);
public enum SaveStatus { Saved, Conflict, Invalid, Online, UnknownCommit }
public record SaveResult(SaveStatus Status, RecordSnapshot? Record = null, IReadOnlyList<ValidationIssue>? Issues = null, string? Message = null);
public record RpcRequest(int Version, Guid Id, string Method, JsonElement Payload);
public record RpcError(string Code, string Message);
public record RpcResponse(int Version, Guid Id, JsonElement? Result, RpcError? Error);
public interface IEditorDataClient { Task<T> SendAsync<T>(string method, object payload, CancellationToken cancellationToken = default); }
public static class JsonConfig { public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = false, PropertyNameCaseInsensitive = true }; }

