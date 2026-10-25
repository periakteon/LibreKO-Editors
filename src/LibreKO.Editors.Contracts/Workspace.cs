namespace LibreKO.Editors.Contracts;
public record WorkspaceProfile(string AssetRoot,string WorkspaceRoot,string ConnectionEnvironmentVariable="LIBREKO_EDITOR_DB",string? ServerScheduleTimeZone=null,IReadOnlyList<SourceProfile>? Sources=null,string? ActiveSourceId=null);
public record DockerDatabase(string ContainerId,string Label,string ConnectionString,bool NeedsRelay,bool NeedsPassword);
