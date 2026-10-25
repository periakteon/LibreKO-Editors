using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Events;
public interface IEventJournal
{
    void Prepare(EventChange change);
    void Commit(EventChange change, EventDocumentSnapshot? after);
    void Abort(Guid id);
}
