using LibreKO.Editors.Contracts;

namespace LibreKO.Editors.Core.Sources;

public sealed class SourceValidationRegistry(BlobStore blobs, SourceProfile profile)
{
    readonly List<Adapter> adapters = [];
    readonly SourceRootScope rootScope=new(profile);
    readonly List<(string Id,Func<FileChange,bool> Accepts,Func<FileChange,CancellationToken,Task<IReadOnlyList<ValidationIssue>>> Validate)> batches=[];
    public void RegisterBatch(string id,Func<FileChange,bool> accepts,Func<FileChange,CancellationToken,Task<IReadOnlyList<ValidationIssue>>> validate)
    {if(string.IsNullOrWhiteSpace(id)||batches.Any(x=>x.Id==id))throw new InvalidOperationException("Batch validator IDs must be unique.");batches.Add((id,accepts,validate));}
    public async Task<IReadOnlyList<ValidationIssue>> ValidateBatchAsync(FileChange change,CancellationToken ct=default)
    {var issues=new List<ValidationIssue>();foreach(var batch in batches.Where(x=>x.Accepts(change))){ct.ThrowIfCancellationRequested();issues.AddRange(await batch.Validate(change,ct));}return issues;}
    public void Register(string codecId, Func<SourceFileKey, bool> accepts,
        Func<SourceValidationTicket, IReadOnlyList<ValidationIssue>> validate)
    {
        if (string.IsNullOrWhiteSpace(codecId) || adapters.Any(x => x.Id == codecId))
            throw new InvalidOperationException("Codec IDs must be unique.");
        adapters.Add(new(codecId, accepts, validate));
    }

    public string Codec(SourceFileKey key)
    {
        rootScope.Guard(key);
        SourcePaths.Resolve(profile, key, true);
        var matches = adapters.Where(x => x.Accepts(key)).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException(matches.Length == 0
            ? "No supported source codec authorises this file." : "Multiple source codecs match this file; writing is ambiguous.");
        return matches[0].Id;
    }

    public IReadOnlyList<ValidationIssue> Validate(SourceValidationTicket ticket)
    {
        try
        {
            var codec = Codec(ticket.Key);
            blobs.Read(ticket.Candidate);
            return adapters.Single(x => x.Id == codec).Validate(ticket);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
        { return [new(ticket.Key.RelativePath, e.Message)]; }
    }

    sealed record Adapter(string Id, Func<SourceFileKey, bool> Accepts,
        Func<SourceValidationTicket, IReadOnlyList<ValidationIssue>> Validate);
}
