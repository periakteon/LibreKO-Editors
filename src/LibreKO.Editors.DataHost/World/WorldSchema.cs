using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using LibreKO.Common.Domain.Entities.GameData;
using LibreKO.Common.Enums;
using LibreKO.Common.Infrastructure.Persistence;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core;
using LibreKO.Editors.Core.World;
using LibreKO.Editors.DataHost.Events;
using Microsoft.EntityFrameworkCore;

namespace LibreKO.Editors.DataHost.World;

public static class WorldSchema
{
    static readonly Type[] types = [typeof(ZoneInfoData), typeof(StartPositionData), typeof(HomeData), typeof(NpcData), typeof(NpcPosData),
        typeof(NpcItemData), typeof(MakeItemGroupData), typeof(MonsterSummonData), typeof(ItemExchangeData), typeof(PetLevelData),
        typeof(PetTransformData), typeof(GameEventData), typeof(MapObjectData), typeof(EventTriggerData), typeof(WarpData)];
    static readonly Lazy<WorldCatalog> catalog = new(Build);
    public static WorldCatalog Catalog() => catalog.Value;
    public static string? ParentField(WorldTable table) => table switch
    {
        WorldTable.NpcPositions or WorldTable.ItemExchanges => "NpcId", WorldTable.Npcs => "ItemGroup",
        WorldTable.NpcItems => "Index", WorldTable.MakeItemGroups => "GroupNum", WorldTable.MonsterSummons => "Sid",
        WorldTable.PetTransforms => "Material", _ => null
    };
    // Pure constant metadata, independent of either catalog's lazy build.
    public static IReadOnlyDictionary<string, string> NpcRoleChoices() => typeof(NpcData).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(x => x.IsLiteral && x.FieldType == typeof(byte) && x.Name.StartsWith("Type", StringComparison.Ordinal)).GroupBy(x => (byte)x.GetRawConstantValue()!)
        .ToDictionary(x => x.Key.ToString(), x => string.Join(" / ", x.Select(c => Label(c.Name[4..]))));
    public static Type EntityType(WorldTable table) => (int)table >= 0 && (int)table < types.Length ? types[(int)table] : throw new InvalidOperationException("Unsupported world table.");
    public static PropertyInfo[] Properties(WorldTable table) => EntityType(table).GetProperties().Where(p => p.CanRead && p.SetMethod?.IsPublic == true).ToArray();
    public static JsonObject Values(WorldTable table, object entity)
    {
        var result = new JsonObject();
        foreach (var property in Properties(table))
        {
            var value = property.GetValue(entity);
            result[property.Name] = value is Enum ? JsonValue.Create(Convert.ToInt32(value)) : JsonSerializer.SerializeToNode(value, property.PropertyType);
        }
        return result;
    }
    public static WorldRowSnapshot Snapshot(WorldTable table, object entity)
    { var values = Values(table, entity); return new(WorldKeys.From(table, values), values, RecordFingerprint.Compute(values)); }

    static string Label(string name) => Regex.Replace(name, @"(?<=[a-z0-9])(?=[A-Z])", " ");
    static WorldCatalog Build()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseMySql("Server=127.0.0.1;Database=metadata;User=unused", new MySqlServerVersion(new Version(8, 2, 0))).Options);
        var tables = new List<WorldTableDescriptor>();
        foreach (var table in Enum.GetValues<WorldTable>())
        {
            var entity = db.Model.FindEntityType(EntityType(table))!;
            var defaults = Values(table, Activator.CreateInstance(EntityType(table))!);
            var keys = WorldKeys.Fields(table);
            if (table is WorldTable.GameEvents or WorldTable.MapObjects or WorldTable.EventTriggers)
            {
                var shared = EventSchema.Catalog().Tables.Single(x => x.Table.ToString() == table.ToString());
                tables.Add(new(table, keys, shared.Fields.Select(x => new WorldFieldDescriptor(x.Field, x.Label, x.Nullable, x.MaxLength)).ToArray(), defaults));
                continue;
            }
            var fields = new List<WorldFieldDescriptor>();
            foreach (var property in Properties(table))
            {
                var name = property.Name; var mapped = entity.FindProperty(name)!;
                var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
                var numeric = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
                decimal? min = numeric == typeof(byte) ? 0 : numeric == typeof(sbyte) ? sbyte.MinValue : numeric == typeof(short) ? short.MinValue : numeric == typeof(ushort) ? 0 : numeric == typeof(int) ? int.MinValue : numeric == typeof(uint) ? 0 : numeric == typeof(long) ? long.MinValue : numeric == typeof(ulong) ? 0 : null;
                decimal? max = numeric == typeof(byte) ? byte.MaxValue : numeric == typeof(sbyte) ? sbyte.MaxValue : numeric == typeof(short) ? short.MaxValue : numeric == typeof(ushort) ? ushort.MaxValue : numeric == typeof(int) ? int.MaxValue : numeric == typeof(uint) ? uint.MaxValue : numeric == typeof(long) ? long.MaxValue : numeric == typeof(ulong) ? ulong.MaxValue : null;
                IReadOnlyDictionary<string, string>? choices = type.IsEnum ? Enum.GetValues(type).Cast<object>().ToDictionary(x => Convert.ToInt32(x).ToString(), x => Label(x.ToString()!)) : null;
                if (table == WorldTable.NpcPositions && name == "ActType")
                    choices = Enum.GetValues<NpcMoveType>().SelectMany(move => new[] { ((int)move, "Monster · " + Label(move.ToString())), ((int)move + 100, "NPC · " + Label(move.ToString())) }).ToDictionary(x => x.Item1.ToString(), x => x.Item2);
                if (table == WorldTable.Npcs && name == "NpcType") choices = NpcRoleChoices();
                if (table == WorldTable.Homes && name == "Nation") choices = Enum.GetValues<AccountNation>().ToDictionary(x => ((int)x).ToString(), x => Label(x.ToString()));
                if (table == WorldTable.NpcItems && name.StartsWith("Percent", StringComparison.Ordinal)) { min = 0; max = 10000; }
                var kind = type == typeof(string) ? FieldKind.Text : type == typeof(bool) ? FieldKind.Boolean : type == typeof(float) || type == typeof(double) || type == typeof(decimal) ? FieldKind.Decimal : choices != null ? FieldKind.Enum : FieldKind.Integer;
                var reference = Reference(table, name);
                var descriptor = new FieldDescriptor(name, Group(table, name, keys), kind, !keys.Contains(name), min, max, choices, reference);
                var label = table == WorldTable.NpcPositions && name == "ActType" ? "Spawn movement mode" : table == WorldTable.Npcs && name == "ActType" ? "Prototype AI ActType (stored code)" :
                    table == WorldTable.NpcPositions && name == "Direction" ? "Direction (stored value)" : table == WorldTable.Npcs && name == "Size" ? "Size (%; nonpositive uses 100)" :
                    table == WorldTable.Npcs && name == "NpcType" ? "NPC role" : table == WorldTable.MonsterSummons && name == "Sid" ? "Prototype ID (monster-first)" : Label(name);
                fields.Add(new(descriptor, label, mapped.IsNullable, mapped.GetMaxLength()));
            }
            tables.Add(new(table, keys, fields, defaults));
        }
        return new(tables);
    }

    static string? Reference(WorldTable table, string name) => table switch
    {
        WorldTable.StartPositions or WorldTable.NpcPositions or WorldTable.Warps when name == "ZoneId" => "zones",
        WorldTable.NpcPositions or WorldTable.ItemExchanges when name == "NpcId" => "npcs",
        WorldTable.MonsterSummons when name == "Sid" => "npcs",
        WorldTable.Npcs when name == "ItemGroup" => "drop-groups",
        WorldTable.Npcs when name == "SellingGroup" => "shops",
        WorldTable.Npcs or WorldTable.PetTransforms when name == "ModelId" => "models",
        WorldTable.Npcs when name is "Magic1" or "Magic2" or "Magic3" => "magic",
        WorldTable.Npcs when name is "WeaponType1" or "WeaponType2" => "items",
        WorldTable.NpcItems when name.StartsWith("Item", StringComparison.Ordinal) => "items",
        WorldTable.ItemExchanges when name.StartsWith("OriginItem", StringComparison.Ordinal) || name.StartsWith("ExchangeItem", StringComparison.Ordinal) => "items",
        WorldTable.PetTransforms when name is "Material" or "Result" => "items",
        _ => null
    };

    static string Group(WorldTable table, string name, string[] keys)
    {
        if (keys.Contains(name)) return "Identity";
        if (table == WorldTable.Npcs)
        {
            if (name is "Name" or "NpcType" or "IsBoss" or "Group" or "Rank" or "Title") return "Identity & role";
            if (name is "ModelId" or "Size" or "WeaponType1" or "WeaponType2") return "Appearance";
            if (name.EndsWith('R')) return "Resistances";
            if (name is "ItemGroup" or "SellingGroup" or "Money" or "Experience" or "Loyalty") return "Rewards & services";
            if (name.StartsWith("Magic", StringComparison.Ordinal) || name is "ActType" or "AttackDelay" or "Speed" or "Speed1" or "Speed2" or "Standtime" or "AttackRange" or "SearchRange" or "TracingRange" or "DirectAttack" or "MagicAttack" or "Family" or "AreaRange") return "AI & skills";
            return "Stats";
        }
        if (table == WorldTable.NpcPositions) return name is "Path" or "DotCnt" or "ActType" ? "Movement & patrol" : name is "LeftX" or "TopZ" or "Direction" or "SpawnRange" ? "Placement" : "Spawn configuration";
        if (table == WorldTable.ZoneInfos) return name is "MapName" or "SmdName" ? "Names & map files" : name.StartsWith("Init", StringComparison.Ordinal) ? "Stored initial coordinates" : "Server routing";
        if (table == WorldTable.StartPositions) return name.StartsWith("Karus", StringComparison.Ordinal) ? "Karus starts & gates" : name.StartsWith("Elmo", StringComparison.Ordinal) ? "El Morad starts & gates" : "Shared spawn ranges";
        if (table == WorldTable.ItemExchanges) return name.StartsWith("Origin", StringComparison.Ordinal) ? "Required items" : name.StartsWith("Exchange", StringComparison.Ordinal) ? "Result items" : "Exchange configuration";
        if (table == WorldTable.NpcItems) return "Drop slots · raw chance / 10000";
        if (table == WorldTable.Homes) return name.StartsWith("Elmo", StringComparison.Ordinal) ? "El Morad home" : name.StartsWith("Karus", StringComparison.Ordinal) ? "Karus home" : "Shared / war homes";
        if (table == WorldTable.PetLevels) return name == "Exp" ? "Experience progression" : "Pet stats";
        if (table == WorldTable.MakeItemGroups) return "Ordered item pool · duplicates preserved";
        if (table == WorldTable.Warps) return "Legacy stored warp · runtime uses SMD";
        return "Configuration";
    }
}
