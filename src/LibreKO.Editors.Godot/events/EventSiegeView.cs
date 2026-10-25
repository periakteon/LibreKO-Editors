using Godot;
using LibreKO.Editors.Core.Events;

namespace LibreKO.Editors.Desktop;
public partial class EventSiegeView : VBoxContainer
{
    public void Show(EventDraft draft, string? serverTimezone)
    {
        var row = draft.Rows.FirstOrDefault();
        if (row == null)
            return;
        Ui.Label(this, "OWNER CLAN  " + row.Values["MasterKnights"] + "     ·     MORADON TAX  " + row.Values["MoradonTax"] + "     ·     DELOS TAX  " + row.Values["DellosTax"], 11, Ui.Accent);
        Ui.Label(this, "Owner, tax totals, requests and challenges are live information. Request / guerrilla rules are stored configuration; runtime behavior depends on the server version.", 11, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Ui.Label(this, "0 disables a siege rule; 1 is Sunday through 7 Saturday. * marks the war countdown, ten minutes before war time.", 11, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }
}
