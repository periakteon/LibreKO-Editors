using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Events;

namespace LibreKO.Editors.Desktop;
public partial class EventWorldViews : VBoxContainer
{
    public void Show(EventDraft draft, EventCatalog catalog)
    {
        var text = draft.Original.Key.Family switch
        {
            EventFamily.MapEvent => "Map event identity includes both Zone and Event number. Type 1 warps using Exec1 (zone), Exec2 (X), Exec3 (Z). Type 3 applies server-defined damage. Database edits do not paint the SMD event grid.",
            EventFamily.MapObject => "Object placement and stored control values. Control target can denote a warp group; its meaning depends on the object type. A database row does not create SMD geometry or scripts.",
            EventFamily.NpcTrigger => "NPC type + trap number match NpcInstance.TrapNumber. This is not a prototype ID. Trigger number refers to existing server script behavior; duplicate matches use the first matching rule.",
            _ => ""
        };
        if (text == "")
        {
            Visible = false;
            return;
        }

        var panel = Ui.Group(this, "World configuration");
        Ui.Label(panel, text, 11, Ui.Muted).AutowrapMode = TextServer.AutowrapMode.WordSmart;
    }
}
