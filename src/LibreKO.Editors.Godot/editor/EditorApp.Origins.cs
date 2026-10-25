using Godot;
namespace LibreKO.Editors.Desktop;
public partial class EditorApp
{
    VBoxContainer originsHost=null!;
    internal ItemOriginsView? Origins;
    void RefreshOrigins()
    {
        Ui.Clear(originsHost);Origins=null;
        Inspector.SetTabHidden(2,Module!=0);
        if(Module==0&&SelectedId>0&&Connected)Origins=Ui.Add(originsHost,new ItemOriginsView(Session,SelectedId){SizeFlagsVertical=SizeFlags.ExpandFill});
    }
}
