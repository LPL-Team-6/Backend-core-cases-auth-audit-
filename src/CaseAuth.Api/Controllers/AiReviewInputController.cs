using CaseAuth.Api.Contracts;
using CaseAuth.Api.Data;
using CaseAuth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaseAuth.Api.Controllers;

// A single read combining extracted fields (across every document on the case) and findings,
// so Teammate 4's AI reviewer doesn't have to call /documents, then /extracted-fields per
// document, then /findings, and stitch them together itself.
[ApiController]
[Route("api/cases/{caseId:guid}/ai-review-input")]
[Authorize]
public class AiReviewInputController(CaseAuthDbContext db, ICaseAccessor caseAccessor) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AiReviewInputResponse>> Get(Guid caseId, CancellationToken ct)
    {
        await caseAccessor.GetScopedCaseAsync(db, caseId, ct);

        var fields = (await db.ExtractedFields
            .Where(f => f.Document!.CaseId == caseId)
            .Select(f => new AiReviewInputFieldResponse(
                f.Id, f.DocumentId, f.Document!.DocumentType, f.FieldName, f.FieldValue, false, f.Confidence))
            .ToListAsync(ct))
            // Tax IDs go to the reviewer as last four only - see Services/SensitiveFields.
            .Select(f => f with { FieldValue = SensitiveFields.Mask(f.FieldName, f.FieldValue), IsMasked = SensitiveFields.IsSensitive(f.FieldName) })
            .ToList();

        var findings = await db.Findings
            .Where(f => f.CaseId == caseId)
            .Select(f => new AiReviewInputFindingResponse(
                f.Id, f.Code, f.Severity, f.Score, f.Message, f.SourceFields.Select(sf => sf.Id).ToList()))
            .ToListAsync(ct);

        return new AiReviewInputResponse(caseId, fields, findings);
    }
}
