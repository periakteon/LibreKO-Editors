using System.Globalization;
using System.Text.Json.Nodes;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Events;
public static class EventRules
{
    public static bool Belongs(EventDocumentKey doc, EventTable table, JsonObject row)
    {
        int N(string key) => FieldValueParser.ReadInt32(row[key]);
        return doc.Family switch
        {
            EventFamily.Temple => table is EventTable.TempleEventSchedules or EventTable.TempleEventRewards ? N("Event") == doc.Id : table == EventTable.ForgottenTempleWaves && doc.Id == 5,
            EventFamily.CollectionRace => table == EventTable.CollectionRaces ? N("Id") == doc.Id : table is EventTable.CollectionRaceSchedules or EventTable.CollectionRaceObjectives or EventTable.CollectionRaceRewards && N("RaceId") == doc.Id,
            EventFamily.Lottery => table == EventTable.LotteryEvents ? N("Id") == doc.Id : table is EventTable.LotterySchedules or EventTable.LotteryRewards && N("LotteryId") == doc.Id,
            EventFamily.CastleSiege => table == EventTable.SiegeWarfare && N("CastleIndex") == doc.Id,
            EventFamily.MapEvent => table == EventTable.GameEvents && N("EventNum") == doc.Id && N("ZoneNum") == doc.Zone,
            EventFamily.MapObject => table == EventTable.MapObjects && N("Id") == doc.Id,
            EventFamily.NpcTrigger => table == EventTable.EventTriggers && N("Index") == doc.Id,
            _ => false
        };
    }

    public static string? ReferenceFor(EventTable table, string field, JsonObject values) => field switch
    {
        "RaceId" => "event-races",
        "LotteryId" => "event-lotteries",
        "ZoneId" or "ZoneNum" => "zones",
        "MasterKnights" => "clans",
        "ItemId" or "ReqItemId" => "items",
        "NpcId" when table == EventTable.ForgottenTempleWaves => "npcs",
        "TargetId" when table == EventTable.CollectionRaceObjectives => FieldValueParser.ReadInt32(values["Kind"]) switch
        {
            0 => "npcs",
            2 => "items",
            _ => null
        },
        "Exec1" when table == EventTable.GameEvents && FieldValueParser.ReadInt32(values["Type"]) == 1 => "zones",
        _ => null
    };
    public static IReadOnlyList<ValidationIssue> Validate(EventChange change, EventCatalog catalog)
    {
        var issues = new List<ValidationIssue>();
        void Error(string field, string message) => issues.Add(new(field, message));
        if (change.Rows.Select(r => r.Key).Distinct().Count() != change.Rows.Count)
            Error("Rows", "Duplicate row mutations.");
        if (change.Before.Key.Family != EventFamily.MapEvent && change.Before.Key.Zone != null || change.Before.Key.Family == EventFamily.MapEvent && change.Before.Key.Zone == null)
            Error("Identity", "Invalid event document key.");
        foreach (var row in change.Rows)
        {
            var prefix = row.Key.Table + " " + row.Key.Id;
            var schema = catalog.Tables.SingleOrDefault(t => t.Table == row.Key.Table);
            if (schema == null)
            {
                Error(prefix, "Unsupported event table.");
                continue;
            }

            if (row.Before == null && row.After == null)
            {
                Error(prefix, "Empty mutation.");
                continue;
            }

            foreach (var values in new[]
            {
                row.Before?.Values,
                row.After
            }.Where(v => v != null))
            {
                try
                {
                    if (EventKeys.From(row.Key.Table, values!) != row.Key || !Belongs(change.Before.Key, row.Key.Table, values!))
                        Error(prefix, "Row does not belong to this document/key.");
                }
                catch
                {
                    Error(prefix, "Invalid row identity.");
                }
            }

            if (row.Before != null && row.Before.Key != row.Key)
                Error(prefix, "Original row key differs.");
            if (row.Key.Table == EventTable.SiegeWarfare && (row.Before == null || row.After == null))
                Error(prefix, "Existing siege configuration can only be updated.");
            if (row.After == null)
                continue;
            foreach (var pair in row.After)
            {
                var field = schema.Fields.SingleOrDefault(f => f.Field.Name == pair.Key);
                if (field == null)
                {
                    Error(prefix + "." + pair.Key, "Unknown field.");
                    continue;
                }

                if (row.Before != null && JsonNode.DeepEquals(row.Before.Values[pair.Key], pair.Value))
                    continue;
                var key = schema.KeyFields.Contains(pair.Key);
                if (!field.Field.Writable && !(row.Before == null && key))
                {
                    Error(prefix + "." + pair.Key, "This field is read-only.");
                    continue;
                }

                try
                {
                    if (pair.Value == null)
                    {
                        if (!field.Nullable)
                            throw new FormatException();
                        continue;
                    }

                    var text = pair.Value.ToString();
                    if (field.Field.Kind == FieldKind.Text)
                    {
                        if (field.MaxLength is { } max && text.Length > max)
                            throw new FormatException();
                        continue;
                    }

                    if (field.Field.Kind == FieldKind.Boolean)
                    {
                        _ = bool.Parse(text);
                        continue;
                    }

                    if (field.Field.Kind == FieldKind.DateOnly)
                    {
                        _ = FieldValueParser.Parse(field.Field, text);
                        continue;
                    }

                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var finite) || !double.IsFinite(finite))
                        throw new FormatException();
                    var number = decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
                    if (field.Field.Kind != FieldKind.Decimal && number != decimal.Truncate(number))
                        throw new FormatException();
                    if (field.Field.Minimum is { } lo && number < lo || field.Field.Maximum is { } hi && number > hi)
                        throw new FormatException();
                    if (field.Field.Choices != null && !field.Field.Choices.ContainsKey(text))
                        throw new FormatException();
                }
                catch
                {
                    Error(prefix + "." + pair.Key, "Invalid value or supported range.");
                }
            }

            if (row.Before == null && row.Key.Id <= 0)
                Error(prefix, "New event row IDs must be positive.");
            foreach (var field in schema.Fields)
                if (!row.After.ContainsKey(field.Field.Name))
                    Error(prefix + "." + field.Field.Name, "A complete proposed row is required.");
            int N(string name) => FieldValueParser.ReadInt32(row.After[name]);
            try
            {
                if (row.After.ContainsKey("Repeat") && (row.Before == null || new[] { "Repeat", "Day", "Date", "DayOfMonth" }.Any(f => !JsonNode.DeepEquals(row.Before.Values[f], row.After[f]))))
                {
                    switch (N("Repeat"))
                    {
                        case 1 when row.After["Day"] == null:
                            Error(prefix + ".Day", "Choose a weekday for a weekly schedule."); break;
                        case 2 when row.After["Date"] == null:
                            Error(prefix + ".Date", "Choose a calendar date for a one-time schedule."); break;
                        case 3 when N("DayOfMonth") is < 1 or > 31:
                            Error(prefix + ".DayOfMonth", "Monthly schedules require a day from 1 to 31; shorter months use their last day."); break;
                    }
                }
                if (row.After.ContainsKey("MinLevel") && row.After.ContainsKey("MaxLevel") && N("MinLevel") > 0 && N("MaxLevel") > 0 && N("MinLevel") > N("MaxLevel"))
                    Error(prefix, "Minimum level exceeds maximum level.");
                if (row.Key.Table is EventTable.CollectionRaceObjectives or EventTable.ForgottenTempleWaves && N("Count") <= 0)
                    Error(prefix, "Count must be positive.");
                if (row.After.ContainsKey("ItemId") && N("ItemId") > 0 && row.After.ContainsKey("ItemCount") && N("ItemCount") <= 0)
                    Error(prefix, "Item count must be positive for an item reward.");
                if (row.Key.Table == EventTable.LotteryRewards && N("Count") <= 0)
                    Error(prefix, "Prize quantity must be positive.");
            }
            catch
            {
                Error(prefix, "Invalid numeric values.");
            }
        }

        return issues;
    }
}
