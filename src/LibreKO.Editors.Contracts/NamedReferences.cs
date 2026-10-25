namespace LibreKO.Editors.Contracts;

public record ReferenceIdentity(string Kind, int Id, int? Zone = null, bool? IsMonster = null);
public record NamedReference(ReferenceIdentity Key, string Label, string Source, string State,
    NavigationTarget? Target = null, System.Text.Json.Nodes.JsonObject? Details = null);
public record NamedReferenceQuery(string Kind, string Text = "", int Page = 0, int Size = 50,
    int? Zone = null, bool? IsMonster = null, string? SourceProfileId = null);
public record NamedReferenceResolve(IReadOnlyList<ReferenceIdentity> Keys, string? SourceProfileId = null);
