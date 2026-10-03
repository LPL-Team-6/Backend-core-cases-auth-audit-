using CaseAuth.Api.Contracts;
using CaseAuth.Api.Data;
using CaseAuth.Api.Entities;
using CaseAuth.Api.Errors;
using CaseAuth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaseAuth.Api.Controllers;

[ApiController]
[Route("api/cases/{caseId:guid}/findings")]
[Authorize]
public class FindingsController(CaseAuthDbContext db, ICaseAccessor caseAccessor, IAuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<FindingResponse>>> List(Guid caseId, CancellationToken ct)
    {
        await caseAccessor.GetScopedCaseAsync(db, caseId, ct);
        var findings = await db.Findings
            .Include(f => f.SourceFields)
            .Where(f => f.CaseId == caseId)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(ct);
        return findings.Select(FindingResponse.From).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<FindingResponse>> Create(Guid caseId, [FromBody] CreateFindingRequest request, CancellationToken ct)
    {
        var c = await caseAccessor.GetScopedCaseAsync(db, caseId, ct);

        var finding = new Finding
        {
            CaseId = c.Id,
            Severity = request.Severity,
            Source = request.Source,
            Code = request.Code,
            Message = request.Message,
            Score = request.Score,
        };

        if (request.SourceFieldIds is { Count: > 0 } sourceFieldIds)
        {
            // Scoped to this case's own documents, so a finding can never cite a field pulled
            // from a different case (accidentally or otherwise).
            var sourceFields = await db.ExtractedFields
                .Where(f => sourceFieldIds.Contains(f.Id) && f.Document!.CaseId == caseId)
                .ToListAsync(ct);

            if (sourceFields.Count != sourceFieldIds.Distinct().Count())
            {
                throw new ValidationApiException(
                    "One or more sourceFieldIds do not belong to a document on this case.");
            }

            finding.SourceFields = sourceFields;
        }

        db.Findings.Add(finding);
        audit.Record(db, c.Id, "Finding.Created", AuditOutcome.Success);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { caseId }, FindingResponse.From(finding));
    }
}
