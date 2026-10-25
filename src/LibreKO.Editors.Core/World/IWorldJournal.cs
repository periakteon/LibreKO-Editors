using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.World;
public interface IWorldJournal
{
    void Prepare(WorldChange change);
    void Commit(WorldChange change, IReadOnlyList<WorldRowSnapshot> after);
    void Abort(Guid id);
}
