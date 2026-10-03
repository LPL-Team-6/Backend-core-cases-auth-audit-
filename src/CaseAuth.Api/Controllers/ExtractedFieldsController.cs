using CaseAuth.Api.Auth;
using CaseAuth.Api.Contracts;
using CaseAuth.Api.Data;
using CaseAuth.Api.Entities;
using CaseAuth.Api.Errors;
using CaseAuth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaseAuth.Api.Controllers;

// Simulates recording output from a document-extraction pipeline; there is no OCR/extraction
// wired up in this scaffold. A caller posts the field values it already has.
[ApiController]
[Route("api/documents/{documentId:guid}/extracted-fields")]
[Authorize]
public class ExtractedFieldsController(CaseAuthDbContext db, ICurrentUser currentUser, IAuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ExtractedFieldResponse>>> List(Guid documentId, CancellationToken ct)
    {
        var document = await GetScopedDocumentAsync(documentId, ct);
        var fields = await db.ExtractedFields
            .Where(f => f.DocumentId == document.Id)
            .OrderBy(f => f.ExtractedAt)
            .ToListAsync(ct);
        return fields.Select(ExtractedFieldResponse.From).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<List<ExtractedFieldResponse>>> Create(
        Guid documentId, [FromBody] CreateExtractedFieldsRequest request, CancellationToken ct)
    {
        var document = await GetScopedDocumentAsync(documentId, ct);

        var fields = request.Fields.Select(f => new ExtractedField
        {
            DocumentId = document.Id,
            FieldName = f.FieldName,
            FieldValue = f.FieldValue,
            Confidence = f.Confidence,
        }).ToList();

        db.ExtractedFields.AddRange(fields);
        audit.Record(db, document.CaseId, "ExtractedField.Recorded", AuditOutcome.Success,
            metadata: $"{{\"documentId\":\"{document.Id}\",\"fieldCount\":{fields.Count}}}");
        await db.SaveChangesAsync(ct);

        return fields.Select(ExtractedFieldResponse.From).ToList();
    }

    // Returns one tax ID in full, and records who saw it. Fields that aren't masked have nothing
    // to reveal and 404, so this can't be used as a general "read any field" audit bypass.
    [HttpPost("{fieldId:guid}/reveal")]
    public async Task<ActionResult<RevealedFieldResponse>> Reveal(Guid documentId, Guid fieldId, CancellationToken ct)
    {
        var document = await GetScopedDocumentAsync(documentId, ct);
        var field = await db.ExtractedFields.FirstOrDefaultAsync(f => f.Id == fieldId && f.DocumentId == document.Id, ct);
        if (field is null || !SensitiveFields.IsSensitive(field.FieldName))
        {
            throw new NotFoundApiException($"Masked field '{fieldId}' was not found on document '{documentId}'.");
        }

        audit.Record(db, document.CaseId, "ExtractedField.Revealed", AuditOutcome.Success,
            metadata: $"{{\"documentId\":\"{document.Id}\",\"fieldId\":\"{field.Id}\",\"fieldName\":\"{field.FieldName}\"}}");
        await db.SaveChangesAsync(ct);

        return new RevealedFieldResponse(field.Id, field.FieldName, field.FieldValue);
    }

    private async Task<Document> GetScopedDocumentAsync(Guid documentId, CancellationToken ct)
    {
        var document = await db.Documents
            .Include(d => d.Case)
            .FirstOrDefaultAsync(d => d.Id == documentId, ct);

        if (document?.Case is null || document.Case.FirmId != currentUser.FirmId)
        {
            throw new NotFoundApiException($"Document '{documentId}' was not found.");
        }

        return document;
    }
}
