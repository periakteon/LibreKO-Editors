using Godot;

namespace LibreKO.Editors.Desktop;
public interface IEditorWorkspace
{
    string Id { get; }

    Control View { get; }
    LibreKO.Editors.Core.WorkspaceLocation CaptureLocation();
    Task RestoreLocationAsync(LibreKO.Editors.Core.WorkspaceLocation location);

    bool HasUnsavedChanges { get; }

    bool CanSave { get; }
    string SaveLabel => "Save to database";
    IReadOnlyList<string> PendingDestinations => HasUnsavedChanges ? ["Database"] : [];

    Task InitializeAsync(EditorSession session);
    Task RefreshAsync();
    Task<bool> SaveAsync();
    void Discard();
    void Undo(bool redo);
    void FocusSearch();
    void SetActive(bool active);
    void InvalidateDatabaseState();
    void ShowHistory();
    void ShowCompatibility();
}
