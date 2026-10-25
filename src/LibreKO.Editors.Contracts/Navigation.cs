using System.Text.Json.Nodes;

namespace LibreKO.Editors.Contracts;

public record NavigationTarget(string EditorId, string Kind, JsonObject Key, string ScopeId);
public record OperationLink(string Origin, Guid OperationId, string ScopeId, string Label,
    bool Pending, IReadOnlyList<NavigationTarget> Targets);
