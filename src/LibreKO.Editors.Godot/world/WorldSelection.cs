using LibreKO.Editors.Contracts;
using LibreKO.Editors.Core.World;
namespace LibreKO.Editors.Desktop;
public sealed class WorldSelection
{
    public IReadOnlyList<WorldKey> Keys { get; private set; } = [];
    public void Set(IReadOnlyList<WorldKey> keys) => Keys = keys.Distinct().OrderBy(WorldKeys.Order, StringComparer.Ordinal).ToArray();
}
