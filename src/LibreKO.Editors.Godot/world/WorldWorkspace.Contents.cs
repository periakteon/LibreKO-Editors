using Godot;
using LibreKO.Editors.Contracts;
namespace LibreKO.Editors.Desktop;
public partial class WorldWorkspace
{
    internal NpcContentsView Contents=null!;
    TabContainer contextTabs=null!;
    internal void ContentsChanged()
    {
        UpdateNotice();RowEditor?.RefreshReferences();
        if(Selected!=null&&Draft!=null&&Catalog!=null)
        {
            PendingInputs.Clear();Ui.Clear(Form);RowEditor=Ui.Add(Form,new WorldRowEditor(this));
            RowEditor.Configure(Draft,Catalog.Tables.Single(t=>t.Table==Selected.Table),Selected);
        }
    }
    internal void ShowContents(){contextTabs.CurrentTab=1;}
    internal bool UndoContentsSource(bool redo)
    {if(contextTabs.CurrentTab!=1||Contents.Mode!=1||Contents.ShopDocument==null)return false;Contents.UndoShop(redo);return true;}
}
