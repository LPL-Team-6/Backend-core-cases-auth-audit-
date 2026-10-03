namespace CaseAuth.Api.Storage;

// Not implemented: wiring this up needs the AWS SDK, a bucket, and real credentials, none of
// which are available in this scaffold. Selecting Storage:Mode=S3 fails fast and loud instead
// of silently falling back to local disk, so a misconfigured deployment cannot be mistaken
// for one that's actually persisting uploads to S3.
public class S3FileStorageService : IFileStorageService
{
    public Task<string> SaveAsync(string firmId, Guid caseId, string fileName, Stream content, CancellationToken ct) =>
        throw new NotImplementedException(
            "S3 storage mode is not implemented in this scaffold. Use Storage:Mode=LocalDisk, " +
            "or implement this class against the AWS SDK for S3 before enabling it.");

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct) =>
        throw new NotImplementedException("S3 storage mode is not implemented in this scaffold.");
}
