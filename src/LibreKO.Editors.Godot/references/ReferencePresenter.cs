using System.Runtime.CompilerServices;
using Godot;
using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.References;
using System.Text.Json.Nodes;
using LibreKO.Editors.Core;

namespace LibreKO.Editors.Desktop;

public sealed class ReferencePresenter(ReferenceCache cache)
{
    readonly ConditionalWeakTable<Control, Stamp> stamps = new();
    public void Bind(Control owner, Func<ReferenceIdentity?> key, Func<bool> current, Action<NamedReference> paint,
        string? sourceProfileId = null)
    {
        var stamp = stamps.GetOrCreateValue(owner);
        var version = ++stamp.Version;
        var requested = key();
        var scope = cache.Scope;
        if (requested == null) return;
        bool Valid() => GodotObject.IsInstanceValid(owner) && owner.IsInsideTree() && stamp.Version == version &&
            key() == requested && cache.Scope == scope && current();
        async Task Resolve()
        {
            try
            {
                var found = await cache.ResolveAsync(requested, sourceProfileId, default);
                if (Valid()) paint(Combine(requested, found));
            }
            catch (OperationCanceledException) { }
            catch
            {
                if (Valid()) paint(new(requested, "", "Lookup", "unavailable"));
            }
        }
        _ = Resolve();
    }

    public static NamedReference Combine(ReferenceIdentity key, IReadOnlyList<NamedReference> found)
        => found.Count == 0 ? new(key, "", "Lookup", "missing") : found.Count == 1 ? found[0] : new(key,
            string.Join(" | ", found.Select(x => (x.Key.IsMonster == true ? "Monster: " : x.Key.IsMonster == false ? "NPC: " : "") + x.Label)) + " · multiple candidates",
            string.Join("; ", found.Select(x => x.Source).Distinct()), "ambiguous");

    public static async Task<string> DescribeFieldsAsync(EditorSession session, RecordKind kind, JsonObject values)
    {
        var fields = await session.Client.SendAsync<FieldDescriptor[]>("schema", new { kind });
        var references = fields.Where(f => f.Reference != null && int.TryParse(values[f.Name]?.ToString(), out _))
            .Select(f => (f.Name, Key: new ReferenceIdentity(f.Reference!, int.Parse(values[f.Name]!.ToString()),
                f.Reference == "bindpoints" && int.TryParse(values["MapId"]?.ToString(), out var zone) ? zone : null))).ToArray();
        var names = await session.References.ResolveBatchAsync(references.Select(x => x.Key).ToArray());
        return string.Join("\n", references.Select(x => Ui.FieldName(x.Name) + ": " + EventRewardView.Describe(x.Key, names)));
    }
    public static async Task<string> DescribeInventoryAsync(EditorSession session, IReadOnlyList<InventorySlotDto>? before, IReadOnlyList<InventorySlotDto>? after)
    {
        if (before == null || after == null) return "";
        var changes = after.Where(slot => before.FirstOrDefault(x => x.Index == slot.Index) != slot).ToArray();
        var ids = before.Concat(changes).Select(x => new ReferenceIdentity("items", x.ItemId)).Distinct().ToArray();
        var names = await session.References.ResolveBatchAsync(ids);
        string Describe(InventorySlotDto? slot) => slot == null ? "Unknown original slot" : EventRewardView.Describe(new("items", slot.ItemId), names) +
            $" × {slot.Count} · durability {slot.Durability} · flag {slot.Flag} · expires {slot.ExpiresAt} · unique {slot.UniqueId}";
        return string.Join("\n", changes.Select(slot => $"{InventoryLayout.Name(slot.Index)} ({slot.Index}): " +
            Describe(before.FirstOrDefault(x => x.Index == slot.Index)) + " → " + Describe(slot)));
    }
    sealed class Stamp { public long Version; }
}
