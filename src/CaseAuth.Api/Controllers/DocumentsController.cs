using CaseAuth.Api.Auth;
using CaseAuth.Api.Contracts;
using CaseAuth.Api.Data;
using CaseAuth.Api.Entities;
using CaseAuth.Api.Errors;
using CaseAuth.Api.Services;
using CaseAuth.Api.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CaseAuth.Api.Controllers;

[ApiController]
[Route("api/cases/{caseId:guid}/documents")]
[Authorize]
public class DocumentsController(
    CaseAuthDbContext db,
    ICurrentUser currentUser,
    ICaseAccessor caseAccessor,
    IFileStorageService storage,
    IOptions<StorageOptions> storageOptions,
    IAuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<DocumentResponse>>> List(Guid caseId, CancellationToken ct)
    {
        await caseAccessor.GetScopedCaseAsync(db, caseId, ct);
        var documents = await db.Documents
            .Where(d => d.CaseId == caseId)
            .OrderBy(d => d.UploadedAt)
            .ToListAsync(ct);
        return documents.Select(DocumentResponse.From).ToList();
    }

    // Streams the stored file back so the review UI can show the document next to its
    // extracted fields. Firm scoping comes from the case lookup, same as every other endpoint.
    // The file is the applicant's ID or tax form, so every successful read is audited.
    [HttpGet("{documentId:guid}/content")]
    public async Task<IActionResult> Content(Guid caseId, Guid documentId, CancellationToken ct)
    {
        var c = await caseAccessor.GetScopedCaseAsync(db, caseId, ct);
        var document = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && d.CaseId == caseId, ct)
            ?? throw new NotFoundApiException($"Document '{documentId}' was not found.");

        // A row whose file has gone missing (fixture folder wiped, half-restored backup) is a
        // 404 for the reviewer, not a 500.
        Stream stream;
        try
        {
            stream = await storage.OpenReadAsync(document.StorageKey, ct);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new NotFoundApiException($"The stored file for document '{documentId}' is missing.");
        }

        // Recorded only once the file is open, so a missing file doesn't log a view that never
        // happened. Same no-file-name rule as Document.Uploaded.
        try
        {
            audit.Record(db, c.Id, "Document.Viewed", AuditOutcome.Success,
                metadata: $"{{\"documentId\":\"{document.Id}\",\"documentType\":\"{document.DocumentType}\"}}");
            await db.SaveChangesAsync(ct);
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }

        return File(stream, document.ContentType);
    }

    [HttpPost]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<DocumentResponse>> Upload(
        Guid caseId,
        [FromForm] DocumentType documentType,
        IFormFile file,
        CancellationToken ct)
    {
        var c = await caseAccessor.GetScopedCaseAsync(db, caseId, ct);
        var options = storageOptions.Value;

        if (file.Length == 0)
        {
            throw new ValidationApiException("The uploaded file is empty.");
        }

        if (file.Length > options.MaxUploadBytes)
        {
            throw new ValidationApiException(
                $"File exceeds the maximum allowed size of {options.MaxUploadBytes} bytes.");
        }

        if (!options.AllowedContentTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
        {
            throw new ValidationApiException(
                $"Content type '{file.ContentType}' is not allowed. Allowed types: {string.Join(", ", options.AllowedContentTypes)}.");
        }

        await using var stream = file.OpenReadStream();
        var storageKey = await storage.SaveAsync(c.FirmId, c.Id, file.FileName, stream, ct);

        var document = new Document
        {
            CaseId = c.Id,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.Length,
            DocumentType = documentType,
            StorageKey = storageKey,
            UploadedByUserId = currentUser.UserId,
        };

        db.Documents.Add(document);
        // Metadata deliberately omits the original file name: it can carry applicant PII
        // (e.g. "jane_doe_passport.pdf"). The Document row itself still has it for the UI.
        audit.Record(db, c.Id, "Document.Uploaded", AuditOutcome.Success,
            metadata: $"{{\"documentType\":\"{document.DocumentType}\",\"sizeBytes\":{document.SizeBytes}}}");
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { caseId }, DocumentResponse.From(document));
    }
}
