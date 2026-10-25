using Godot;
using LibreKO.Editors.Core;

namespace LibreKO.Editors.Desktop;
public partial class ActivityIndicator : HBoxContainer
{
    readonly ActivityTracker tracker;
    readonly string? channel;
    readonly Label caption;
    public ActivityIndicator(ActivityTracker tracker, string? channel = null, bool compact = false)
    {
        this.tracker = tracker; this.channel = channel; MouseFilter = MouseFilterEnum.Ignore;
        AddChild(new LoadingSpinner());
        caption = Ui.Label(this, compact ? "Loading" : "Loading…", 11, Ui.Accent);
        caption.ClipText = true; caption.CustomMinimumSize = new(compact ? 46 : 140, 0);
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
    }
    public override void _Process(double delta)
    {
        Visible = tracker.IsBusy(channel);
        if (!Visible) return;
        var messages = tracker.Messages(channel); TooltipText = string.Join("\n", messages);
        if (channel != null) caption.Text = messages.FirstOrDefault() ?? "Loading…";
    }
}
public partial class LoadingSpinner : Control
{
    float phase;
    public LoadingSpinner() { CustomMinimumSize = new(18, 18); MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Process(double delta) { if (IsVisibleInTree()) { phase += (float)delta * 5; QueueRedraw(); } }
    public override void _Draw() => DrawArc(Size / 2, 6, phase, phase + Mathf.Pi * 1.4f, 24, Ui.Accent, 2, true);
}
