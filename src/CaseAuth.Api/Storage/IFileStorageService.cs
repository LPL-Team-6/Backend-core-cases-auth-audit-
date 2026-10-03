namespace CaseAuth.Api.Storage;

public interface IFileStorageService
{
    // Streams the upload to the backing store and returns a storage key (relative path or S3
    // object key) to persist on the Document row. Never buffers the whole file in memory.
    Task<string> SaveAsync(string firmId, Guid caseId, string fileName, Stream content, CancellationToken ct);

    // Opens a previously saved file for streaming back to the caller. Callers must have
    // already checked firm scoping on the owning case - the storage key alone grants nothing.
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken ct);
}
