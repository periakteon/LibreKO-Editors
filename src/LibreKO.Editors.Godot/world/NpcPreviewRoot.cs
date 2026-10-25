using Godot;
namespace LibreKO.Editors.Desktop;
public partial class NpcPreviewRoot : Node3D
{
    internal Action? Released;
    internal AnimationPlayer? PrimaryAnimation;
    public override void _Notification(int what)
    {if(what==NotificationPredelete){Released?.Invoke();Released=null;}}
}
