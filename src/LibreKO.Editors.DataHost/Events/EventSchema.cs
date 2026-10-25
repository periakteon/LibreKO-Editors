using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.Events;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Editors.DataHost.Events;
public static class EventSchema
{
    static readonly Type[] types = [typeof(TempleEventScheduleData), typeof(TempleEventRewardData), typeof(ForgottenTempleWaveData), typeof(CollectionRaceData), typeof(CollectionRaceScheduleData), typeof(CollectionRaceObjectiveData), typeof(CollectionRaceRewardData), typeof(LotteryEventData), typeof(LotteryScheduleData), typeof(LotteryRewardData), typeof(SiegeWarfareData), typeof(GameEventData), typeof(MapObjectData), typeof(EventTriggerData)];
    static readonly Lazy<EventCatalog> catalog = new(Build);
    public static Type EntityType(EventTable table) => (int)table >= 0 && (int)table < types.Length ? types[(int)table] : throw new InvalidOperationException("Unsupported event table.");
    public static PropertyInfo[] Properties(EventTable table) => EntityType(table).GetProperties().Where(p => p.CanRead && p.SetMethod?.IsPublic == true).ToArray();
    public static EventCatalog Catalog() => catalog.Value;
    public static IReadOnlyList<EventTable> TablesFor(EventDocumentKey key) => key.Family switch
    {
        EventFamily.Temple => key.Id == 5 ? [EventTable.TempleEventSchedules, EventTable.TempleEventRewards, EventTable.ForgottenTempleWaves] : [EventTable.TempleEventSchedules, EventTable.TempleEventRewards],
        EventFamily.CollectionRace => [EventTable.CollectionRaces, EventTable.CollectionRaceSchedules, EventTable.CollectionRaceObjectives, EventTable.CollectionRaceRewards],
        EventFamily.Lottery => [EventTable.LotteryEvents, EventTable.LotterySchedules, EventTable.LotteryRewards],
        EventFamily.CastleSiege => [EventTable.SiegeWarfare],
        EventFamily.MapEvent => [EventTable.GameEvents],
        EventFamily.MapObject => [EventTable.MapObjects],
        EventFamily.NpcTrigger => [EventTable.EventTriggers],
        _ => throw new InvalidOperationException("Unsupported event family.")};
    public static JsonObject VersionValues(EventTable table, JsonObject values)
    {
        var names = Catalog().Tables.Single(t => t.Table == table);
        return new(values.Where(v => names.KeyFields.Contains(v.Key) || names.Fields.Any(f => f.Field.Name == v.Key && f.Field.Writable)).Select(v => new KeyValuePair<string, JsonNode?>(v.Key, v.Value?.DeepClone())));
    }

    public static JsonObject Values(EventTable table, object entity)
    {
        var values = new JsonObject();
        foreach (var property in Properties(table))
        {
            var value = property.GetValue(entity);
            values[property.Name] = value is Enum ? JsonValue.Create(Convert.ToInt32(value)) : JsonSerializer.SerializeToNode(value, property.PropertyType);
        }

        return values;
    }

    public static EventRowSnapshot Snapshot(EventTable table, object entity)
    {
        var values = Values(table, entity);
        return new(EventKeys.From(table, values), values, RecordFingerprint.Compute(VersionValues(table, values)));
    }

    static string Label(string name) => Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])", " ");
    static EventCatalog Build()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseMySql("Server=127.0.0.1;Database=metadata;User=unused", new MySqlServerVersion(new Version(8, 2, 0))).Options);
        var tables = new List<EventTableDescriptor>();
        foreach (var table in Enum.GetValues<EventTable>())
        {
            var type = EntityType(table);
            var entity = db.Model.FindEntityType(type)!;
            var defaults = Values(table, Activator.CreateInstance(type)!);
            var keys = EventKeys.Fields(table);
            var fields = new List<EventFieldDescriptor>();
            foreach (var property in Properties(table))
            {
                var mapped = entity.FindProperty(property.Name)!;
                var t = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var numeric = t.IsEnum ? Enum.GetUnderlyingType(t) : t;
                decimal? min = numeric == typeof(byte) ? 0 : numeric == typeof(short) ? short.MinValue : numeric == typeof(int) ? int.MinValue : numeric == typeof(long) ? long.MinValue : null;
                decimal? max = numeric == typeof(byte) ? byte.MaxValue : numeric == typeof(short) ? short.MaxValue : numeric == typeof(int) ? int.MaxValue : numeric == typeof(long) ? long.MaxValue : null;
                var name = property.Name;
                if (name is "Hour" or "WarTime" or "WarRequestTime" or "GuerrillaWarTime")
                {
                    min = 0;
                    max = 23;
                }

                if (name is "Minute" or "WarMinute" or "WarRequestMinute" or "GuerrillaWarMinute")
                {
                    min = 0;
                    max = 59;
                }

                if (name.EndsWith("Day") && table == EventTable.SiegeWarfare)
                {
                    min = 0;
                    max = 7;
                }

                if (name is "Rate" or "ExpPercent")
                {
                    min = 0;
                    max = 100;
                }

                if (name.EndsWith("Tariff"))
                {
                    min = 0;
                    max = 20;
                }

                if (name is "DurationMinutes" or "UserLimit" or "MaxWinners")
                {
                    min = 1;
                    max = int.MaxValue;
                }

                if (name is "Count" or "ItemCount" or "ReqItemCount" or "StartSecond" or "LoyaltyPoints" or "DungeonCharge" or "DungeonEntranceFee")
                    min = 0;
                var writable = !keys.Contains(name);
                if (table == EventTable.SiegeWarfare)
                    writable = new[]
                    {
                        "WarDay",
                        "WarTime",
                        "WarMinute",
                        "WarRequestDay",
                        "WarRequestTime",
                        "WarRequestMinute",
                        "GuerrillaWarDay",
                        "GuerrillaWarTime",
                        "GuerrillaWarMinute",
                        "MoradonTariff",
                        "DellosTariff",
                        "DungeonCharge",
                        "DungeonEntranceFee"
                    }.Contains(name);
                IReadOnlyDictionary<string, string>? choices = t.IsEnum ? Enum.GetValues(t).Cast<object>().ToDictionary(v => Convert.ToInt32(v).ToString(), v => Label(v.ToString()!)) : null;
                if (table == EventTable.EventTriggers && name == "NpcType") choices = World.WorldSchema.NpcRoleChoices();
                if (name == "Event" && choices != null)
                    choices = choices.Where(p => p.Key != "0").ToDictionary();
                if (table == EventTable.SiegeWarfare && name.EndsWith("Day"))
                    choices = new Dictionary<string, string>
                    {
                        {
                            "0",
                            "Disabled"
                        },
                        {
                            "1",
                            "Sunday"
                        },
                        {
                            "2",
                            "Monday"
                        },
                        {
                            "3",
                            "Tuesday"
                        },
                        {
                            "4",
                            "Wednesday"
                        },
                        {
                            "5",
                            "Thursday"
                        },
                        {
                            "6",
                            "Friday"
                        },
                        {
                            "7",
                            "Saturday"
                        }
                    };
                if (table == EventTable.ForgottenTempleWaves && name == "Tier")
                    choices = new Dictionary<string, string>
                    {
                        {
                            "1",
                            "Low level"
                        },
                        {
                            "2",
                            "High level"
                        }
                    };
                if (table == EventTable.ForgottenTempleWaves && name == "Wave")
                    min = 1;
                if (name == "Type" && table == EventTable.GameEvents)
                    choices = new Dictionary<string, string>
                    {
                        {
                            "1",
                            "Zone warp"
                        },
                        {
                            "3",
                            "Damage zone (server-defined damage)"
                        }
                    };
                if (name == "DayOfMonth") { min = 0; max = 31; }
                var fieldLabel = table == EventTable.EventTriggers && name == "NpcId" ? "Trap number" : t == typeof(DateOnly) ? "Date (yyyy-MM-dd)" : Label(name);
                var kind = t == typeof(string) ? FieldKind.Text : t == typeof(DateOnly) ? FieldKind.DateOnly : t == typeof(bool) ? FieldKind.Boolean : t == typeof(float) || t == typeof(double) || t == typeof(decimal) ? FieldKind.Decimal : t.IsEnum ? FieldKind.Enum : FieldKind.Integer;
                var field = new FieldDescriptor(name, keys.Contains(name) ? "Identity" : name.StartsWith("Cond") || name.StartsWith("Exec") ? "Advanced stored fields" : !writable ? "Server state" : "Configuration", kind, writable, min, max, choices, EventRules.ReferenceFor(table, name, defaults));
                fields.Add(new(field, fieldLabel, mapped.IsNullable, mapped.GetMaxLength()));
            }

            tables.Add(new(table, keys, fields, defaults));
        }

        var families = Enum.GetValues<EventFamily>().Select(f => new EventFamilyDescriptor(f, f switch
        {
            EventFamily.Temple => "Temple activities",
            EventFamily.CollectionRace => "Collection Race",
            EventFamily.Lottery => "Lottery",
            EventFamily.CastleSiege => "Castle Siege",
            EventFamily.MapEvent => "Map events",
            EventFamily.MapObject => "Map objects",
            _ => "NPC triggers"
        }, TablesFor(new(f, f == EventFamily.Temple ? 5 : 1)))).ToArray();
        return new(families, tables);
    }
}
