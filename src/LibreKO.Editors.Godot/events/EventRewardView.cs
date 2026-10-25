using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Events;
using LibreKO.Editors.Core.References;

namespace LibreKO.Editors.Desktop;
public static class EventRewardView
{
    public static IReadOnlyList<(string Field, ReferenceIdentity Key)> References(EventRowSnapshot row)
    {
        var result = new List<(string, ReferenceIdentity)>();
        foreach (var value in row.Values)
        {
            var kind = EventRules.ReferenceFor(row.Key.Table, value.Key, row.Values);
            if (kind != null && int.TryParse(value.Value?.ToString(), out var id)) result.Add((value.Key, new(kind, id)));
        }
        return result;
    }

    public static async Task NameRowsAsync(EventsWorkspace app, ItemList list, EventRowSnapshot[] rows)
    {
        var generation = app.Session.Generation; var view = app.ViewGeneration;
        var scope = app.Session.References.Scope;
        var keys = rows.SelectMany(References).Select(x => x.Key).Distinct().ToArray();
        IReadOnlyList<NamedReference> names;
        try { names = await app.Session.References.ResolveBatchAsync(keys); }
        catch { names = keys.Select(k => new NamedReference(k, "", "Database", "unavailable")).ToArray(); }
        if (!GodotObject.IsInstanceValid(list) || !list.IsInsideTree() || !app.Active || app.Session.Generation != generation ||
            app.ViewGeneration != view || app.Session.References.Scope != scope) return;
        for (var i = 0; i < rows.Length; i++)
        {
            var row = app.Draft?.Rows.FirstOrDefault(r => r.Key == rows[i].Key) ?? rows[i]; var fields = References(row);
            var lines = fields.Select(x => x.Field + ": " + Describe(x.Key, names)).ToArray();
            var summary = row.Values.ContainsKey("Name") ? row.Values["Name"]!.ToString() :
                row.Values.ContainsKey("Hour") ? EventScheduleProjection.DescribeSchedule(row) :
                row.Key.Table == EventTable.ForgottenTempleWaves ? $"Tier {row.Values["Tier"]} · wave {row.Values["Wave"]}" :
                row.Key.Table == EventTable.CollectionRaceObjectives ? $"Order {row.Values["Ordinal"]}" : row.Key.Table.ToString();
            var text = row.Key.Id + " · " + summary + (lines.Length > 0 ? " · " + string.Join(" · ", lines) : "");
            if ((row.Values["ItemCount"] ?? row.Values["Count"]) is { } count) text += " × " + count;
            list.SetItemText(i, text);
            list.SetItemTooltip(i, text + "\n" + string.Join("\n", names.Where(n => fields.Any(x => x.Key.Kind == n.Key.Kind && x.Key.Id == n.Key.Id)).Select(n => n.Source).Distinct()));
        }
    }

    public static string Describe(ReferenceIdentity key, IReadOnlyList<NamedReference> names)
    {
        var matching = names.Where(x => x.Key.Kind == key.Kind && x.Key.Id == key.Id && (key.IsMonster == null || key.IsMonster == x.Key.IsMonster)).ToArray();
        return ReferenceLabels.Display(ReferencePresenter.Combine(key, matching));
    }
    public static async Task NameDocumentsAsync(EventsWorkspace app, ItemList list, EventDocumentSummary[] documents)
    {
        var generation = app.Session.Generation; var family = app.Family; var scope = app.Session.References.Scope;
        int? Zone(EventDocumentSummary row) => row.Key.Zone ?? (int.TryParse(row.Details["ZoneId"]?.ToString(), out var zone) ? zone : null);
        var keys = documents.Select(Zone).Where(x => x != null).Select(x => new ReferenceIdentity("zones", x!.Value)).Distinct().ToArray();
        if (keys.Length == 0) return;
        var names = await app.Session.References.ResolveBatchAsync(keys);
        if (!GodotObject.IsInstanceValid(list) || !list.IsInsideTree() || app.Session.Generation != generation || app.Family != family ||
            app.Session.References.Scope != scope || !app.Documents.Select(x => x.Key).SequenceEqual(documents.Select(x => x.Key))) return;
        for (var i = 0; i < documents.Length; i++)
        {
            var row = documents[i];
            var text = row.Key.Id + " · " + row.Label;
            if (Zone(row) is int zone) text += " · " + Describe(new("zones", zone), names);
            list.SetItemText(i, text); list.SetItemTooltip(i, text);
        }
    }
    public static async Task<string> DescribeChangesAsync(EditorSession session, IReadOnlyList<EventRowChange> changes)
    {
        var rows = changes.SelectMany(c => new[] { c.Before, c.After == null ? null : new EventRowSnapshot(c.Key, c.After, "review") })
            .Where(r => r != null).Cast<EventRowSnapshot>().ToArray();
        var keys = rows.SelectMany(References).Select(x => x.Key).Distinct().ToArray();
        var names = await session.References.ResolveBatchAsync(keys);
        return string.Join("\n", rows.Select(r => r.Key.Table + " " + r.Key.Id + " · " + string.Join(" · ", References(r).Select(x => x.Field + ": " + Describe(x.Key, names)))));
    }
}
