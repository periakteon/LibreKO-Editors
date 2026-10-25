namespace LibreKO.Editors.Core;

/// <summary>Tracks overlapping work independently of the mutation gate.</summary>
public sealed class ActivityTracker
{
    readonly object sync = new();
    readonly Dictionary<long, (string Channel, string Message)> activities = [];
    long next;
    public IDisposable Begin(string channel, string message)
    {
        lock (sync) { var id = ++next; activities.Add(id, (channel, message)); return new Ticket(this, id); }
    }
    public bool IsBusy(string? channel = null)
    { lock (sync) return activities.Values.Any(a => channel == null || a.Channel == channel); }
    public string[] Messages(string? channel = null)
    { lock (sync) return activities.Values.Where(a => channel == null || a.Channel == channel).Select(a => a.Message).Distinct().ToArray(); }
    sealed class Ticket(ActivityTracker owner, long id) : IDisposable
    {
        int disposed;
        public void Dispose()
        { if (Interlocked.Exchange(ref disposed, 1) == 0) lock (owner.sync) owner.activities.Remove(id); }
    }
}
