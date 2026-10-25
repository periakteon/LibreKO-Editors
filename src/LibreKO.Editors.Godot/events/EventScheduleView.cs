using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.Events;

namespace LibreKO.Editors.Desktop;
public partial class EventScheduleView : VBoxContainer
{
    public void Show(EventDocumentSnapshot document, string? serverTimezone)
    {
        var entries = EventScheduleProjection.Project(document, serverTimezone);
        if (document.Key.Family is EventFamily.MapEvent or EventFamily.MapObject or EventFamily.NpcTrigger)
        {
            Visible = false;
            return;
        }

        Ui.Label(this, entries.FirstOrDefault()?.Clock ?? (document.Key.Family is EventFamily.CollectionRace or EventFamily.Lottery ? "UTC" : "Server schedule time (" + (serverTimezone ?? "timezone unspecified") + ")"), 11, Ui.Accent);
        var week = Ui.Row(this);
        foreach (var day in new[]
        {
            DayOfWeek.Monday,
            DayOfWeek.Tuesday,
            DayOfWeek.Wednesday,
            DayOfWeek.Thursday,
            DayOfWeek.Friday,
            DayOfWeek.Saturday,
            DayOfWeek.Sunday
        }

        )
        {
            var panel = Ui.Add(week, new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill });
            panel.AddThemeStyleboxOverride("panel", Ui.Surface(6, true));
            var column = Ui.Column(panel);
            Ui.Label(column, day.ToString()[..3].ToUpperInvariant(), 10, Ui.Muted);
            var matching = entries.Where(e => e.CalendarRule == null && (e.Day == null || e.Day == day)).OrderBy(e => e.Hour).ThenBy(e => e.Minute).ToArray();
            var text = matching.Length == 0 ? "—" : string.Join("\n", matching.Take(2).Select(e => $"{e.Hour:00}:{e.Minute:00}" + (e.Countdown ? " *" : ""))) + (matching.Length > 2 ? $"\n+{matching.Length - 2} more" : "");
            var label = Ui.Label(column, text, 12, matching.Length == 0 ? Ui.Muted : Ui.Accent);
            label.TooltipText = string.Join("\n", matching.Select(e => $"{e.Hour:00}:{e.Minute:00} · {e.Label}"));
        }

        foreach (var entry in entries.Where(e => e.CalendarRule != null))
            Ui.Label(this, $"{entry.CalendarRule} · {entry.Hour:00}:{entry.Minute:00} · {entry.Clock}", 11, Ui.Accent).AutowrapMode = TextServer.AutowrapMode.WordSmart;
        Ui.Label(this, "Stored schedule rules · runtime clock offset is controlled by the server", 10, Ui.Muted);
    }
}
