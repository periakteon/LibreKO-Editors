using System.Globalization;
using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.References;

public static class ReferenceLabels
{
    public static string Display(NamedReference reference)
    {
        var name = reference.State switch { "missing" => "Missing reference", "unavailable" => "Lookup unavailable", _ => reference.Label };
        if (string.IsNullOrWhiteSpace(name)) name = "Unnamed record";
        var suffix = reference.Key.IsMonster is bool monster ? monster ? " · Monster" : " · NPC" : "";
        if (reference.Key.Zone is int zone) suffix += " · Zone " + zone;
        return reference.Key.Id.ToString(CultureInfo.InvariantCulture) + " · " + name + suffix;
    }

    public static string Enum(int code, IReadOnlyDictionary<string, string> labels)
        => code.ToString(CultureInfo.InvariantCulture) + " · " + (labels.GetValueOrDefault(code.ToString(CultureInfo.InvariantCulture)) ?? "Unknown code");
}
