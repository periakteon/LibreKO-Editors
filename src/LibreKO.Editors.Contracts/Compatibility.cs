namespace LibreKO.Editors.Contracts;
public enum CompatibilitySeverity { Information, Warning, Blocking }
public record CompatibilityIssue(string Code,CompatibilitySeverity Severity,string Scope,string Target,string Message,RecordKey? Record=null);
public record CompatibilityColumn(string Table,string Name,string StoreType,bool Nullable,bool PrimaryKey=false,bool HasDefault=false,bool Generated=false);
public record ModuleCompatibility(RecordKind Kind,bool CanBrowse,bool CanSave,string Reason);
public record DatabaseCompatibility(string Database,string ServerVersion,IReadOnlyList<ModuleCompatibility> Modules,IReadOnlyList<CompatibilityIssue> Issues);
public record CompatibilityItem(int Id,string Name,int Kind,int Slot,int Extension,int Damage,int Defense,int Duration);
public record CompatibilityItemBatch(IReadOnlyList<CompatibilityItem> Rows,int? NextId);
public record CompatibilityInventoryBatch(int Scanned,int? NextId,IReadOnlyList<CompatibilityIssue> Issues);
