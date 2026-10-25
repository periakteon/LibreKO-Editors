using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Events;
public record EventScheduleEntry(EventRowKey Key, DayOfWeek? Day, int Hour, int Minute, string Clock, string Label, bool Countdown = false, string? CalendarRule = null);
public static class EventScheduleProjection
{
    public static string DescribeSchedule(EventRowSnapshot row)
    {
        var entry = Schedule(row, "");
        var rule = entry.CalendarRule ?? entry.Day?.ToString() ?? "Daily";
        return $"{rule} · {entry.Hour:00}:{entry.Minute:00}";
    }

    static EventScheduleEntry Schedule(EventRowSnapshot row, string clock)
    {
        int N(string field) => FieldValueParser.ReadInt32(row.Values[field]);
        DayOfWeek? day = row.Values["Day"] == null ? null : (DayOfWeek)N("Day");
        string? calendar = null;
        if (row.Values.ContainsKey("Repeat"))
        {
            var repeat = N("Repeat");
            if (repeat != 1) day = null;
            calendar = repeat switch
            {
                0 => null,
                1 => day == null ? "Weekly · weekday missing" : null,
                2 => "Once · " + (row.Values["Date"]?.ToString() ?? "date missing"),
                3 => $"Monthly · day {N("DayOfMonth")} (last day in shorter months)",
                _ => $"Unknown repeat {repeat}"
            };
        }
        return new(row.Key, day, N("Hour"), N("Minute"), clock, "Scheduled start", CalendarRule: calendar);
    }

    public static IReadOnlyList<EventScheduleEntry> Project(EventDocumentSnapshot document, string? serverTimezone)
    {
        var entries = new List<EventScheduleEntry>();
        var clock = document.Key.Family is EventFamily.CollectionRace or EventFamily.Lottery ? "UTC" : "Server schedule time (" + (string.IsNullOrWhiteSpace(serverTimezone) ? "timezone unspecified" : serverTimezone) + ")";
        foreach (var row in document.Rows)
        {
            int N(string field) => FieldValueParser.ReadInt32(row.Values[field]);
            if (row.Key.Table is EventTable.TempleEventSchedules or EventTable.CollectionRaceSchedules or EventTable.LotterySchedules)
            {
                entries.Add(Schedule(row, clock));
            }

            if (row.Key.Table == EventTable.SiegeWarfare)
                foreach (var(day, hour, minute, label)in new[]
                {
                    ("WarDay", "WarTime", "WarMinute", "War"),
                    ("WarRequestDay", "WarRequestTime", "WarRequestMinute", "Request"),
                    ("GuerrillaWarDay", "GuerrillaWarTime", "GuerrillaWarMinute", "Guerrilla")
                }

                )
                {
                    var d = N(day);
                    if (d is < 1 or > 7)
                        continue;
                    entries.Add(new(row.Key, (DayOfWeek)(d - 1), N(hour), N(minute), clock, label));
                    if (day == "WarDay")
                    {
                        var total = (d - 1) * 1440 + N(hour) * 60 + N(minute) - 10;
                        total = (total + 10080) % 10080;
                        entries.Add(new(row.Key, (DayOfWeek)(total / 1440), total % 1440 / 60, total % 60, clock, "War countdown", true));
                    }
                }
        }

        return entries;
    }
}
