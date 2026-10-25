using System.Text.Json.Nodes;

namespace LibreKO.Editors.Core;

public sealed record WorkspaceLocation(string? EditorId, JsonObject State)
{
    public WorkspaceLocation Copy() => new(EditorId, (JsonObject)State.DeepClone());
    public bool Matches(WorkspaceLocation other) => EditorId == other.EditorId && JsonNode.DeepEquals(State, other.State);
}

/// <summary>Only commits a back/forward move after the destination has been restored successfully.</summary>
public sealed class BrowserHistory(int capacity = 200)
{
    readonly List<WorkspaceLocation> entries = [];
    int cursor = -1;
    public string Scope { get; private set; } = "";
    public WorkspaceLocation? Current => Get(cursor);
    public WorkspaceLocation? Back => Get(cursor - 1);
    public WorkspaceLocation? Forward => Get(cursor + 1);
    WorkspaceLocation? Get(int index) => index >= 0 && index < entries.Count ? entries[index].Copy() : null;
    public void Reset(string scope) { Scope = scope; entries.Clear(); cursor = -1; }
    public void UpdateCurrent(WorkspaceLocation location) { if (cursor >= 0) entries[cursor] = location.Copy(); }
    public void Visit(WorkspaceLocation location)
    {
        if (Current?.Matches(location) == true) return;
        if (cursor + 1 < entries.Count) entries.RemoveRange(cursor + 1, entries.Count - cursor - 1);
        entries.Add(location.Copy()); cursor++;
        if (entries.Count > Math.Max(2, capacity)) { entries.RemoveAt(0); cursor--; }
    }
    public bool CommitMove(WorkspaceLocation destination,bool forward=false)
    {
        var target=cursor+(forward?1:-1);
        if(Get(target)?.Matches(destination)!=true)return false;
        cursor=target;return true;
    }
}
