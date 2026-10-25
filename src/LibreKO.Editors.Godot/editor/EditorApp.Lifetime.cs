using Godot;
namespace LibreKO.Editors.Desktop;
public partial class EditorApp {
 bool closing=>Session?.IsClosing==true;
 internal bool IsClosing=>closing;
 internal Task QuitEditor(int exitCode=0)=>Session.Shell.QuitAsync(exitCode);
}
