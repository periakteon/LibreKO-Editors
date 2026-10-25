namespace LibreKO.Editors.Desktop;
public record WorkspaceDefinition(string Id, string Title, string Subtitle, string Description, string Icon, Func<IEditorWorkspace> Create);
public static class WorkspaceRegistry
{
    public static IReadOnlyList<WorkspaceDefinition> All { get; } = [
        new("content", "Content Editor", "Items, player characters and accounts", "Visual inventory, model previews, named references and database history.", "game/main-hand", () => new EditorApp()),
        new("events", "Events Editor", "Schedules, objectives and rewards", "Manage linked activities, world events and recoverable database operations.", "system/combat-attack", () => new EventsWorkspace()),
        new("world", "Map & NPC Editor", "Maps, NPCs, monsters and drops", "Manage world definitions, spawns, drops, exchanges, summons and pets.", "system/location", () => new WorldWorkspace()) ];
    public static IEditorWorkspace Create(string id) => All.Single(x => x.Id == id).Create();
}
