using CaseAuth.Api.Auth;
using CaseAuth.Api.Contracts;
using CaseAuth.Api.Data;
using CaseAuth.Api.Entities;
using CaseAuth.Api.Errors;
using CaseAuth.Api.Screening;
using CaseAuth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaseAuth.Api.Controllers;

[ApiController]
[Route("api/cases")]
[Authorize]
public class CasesController(
    CaseAuthDbContext db,
    ICurrentUser currentUser,
    ICaseAccessor caseAccessor,
    IAuditService audit,
    ScreeningOptions screeningOptions) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CaseResponse>> Create([FromBody] CreateCaseRequest request, CancellationToken ct)
    {
        if (!Enum.IsDefined(request.ApplicantKind))
        {
            throw new ValidationApiException("Invalid applicant kind.");
        }

        var applicant = new Applicant
        {
            FirmId = currentUser.FirmId,
            FullName = request.ApplicantFullName,
            DateOfBirth = request.ApplicantDateOfBirth,
            Email = request.ApplicantEmail,
            Phone = request.ApplicantPhone,
            Kind = request.ApplicantKind,
        };

        var newCase = new Case
        {
            FirmId = currentUser.FirmId,
            ApplicantId = applicant.Id,
            Applicant = applicant,
            CreatedByUserId = currentUser.UserId,
        };

        db.Applicants.Add(applicant);
        db.Cases.Add(newCase);
        audit.Record(db, newCase.Id, "Case.Created", AuditOutcome.Success);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = newCase.Id }, CaseResponse.From(newCase, [], screeningOptions));
    }

    [HttpGet]
    public async Task<ActionResult<List<CaseResponse>>> List(CancellationToken ct)
    {
        // Scoped by the caller's own firm claim - there is no firmId query parameter to trust.
        var cases = await db.Cases
            .Include(c => c.Applicant)
            .Where(c => c.FirmId == currentUser.FirmId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(ct);

        // One query for every case's finding scores, rather than one per case.
        var caseIds = cases.Select(c => c.Id).ToList();
        var scoresByCase = (await db.Findings
                .Where(f => caseIds.Contains(f.CaseId))
                .Select(f => new { f.CaseId, f.Code, f.Score })
                .ToListAsync(ct))
            .ToLookup(f => f.CaseId, f => (f.Code, f.Score));

        return cases.Select(c => CaseResponse.From(c, scoresByCase[c.Id], screeningOptions)).ToList();
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CaseResponse>> Get(Guid id, CancellationToken ct)
    {
        var c = await caseAccessor.GetScopedCaseAsync(db, id, ct, includeApplicant: true);
        return CaseResponse.From(c, await FindingScoresAsync(c.Id, ct), screeningOptions);
    }

    [HttpPost("{id:guid}/extract")]
    public Task<ActionResult<CaseResponse>> MarkExtracted(Guid id, CancellationToken ct) =>
        TransitionAsync(id, "extract", ct);

    [HttpPost("{id:guid}/screen")]
    public Task<ActionResult<CaseResponse>> MarkScreened(Guid id, CancellationToken ct) =>
        TransitionAsync(id, "screen", ct);

    [HttpPost("{id:guid}/mark-ai-reviewed")]
    public async Task<ActionResult<CaseResponse>> MarkAiReviewed(Guid id, CancellationToken ct)
    {
        var hasAiReview = await db.AiReviews.AnyAsync(r => r.CaseId == id, ct);
        if (!hasAiReview)
        {
            throw new ValidationApiException(
                "At least one AI review must be recorded before marking the case as AI-reviewed.");
        }

        return await TransitionAsync(id, "mark-ai-reviewed", ct);
    }

    [HttpPost("{id:guid}/request-decision")]
    public Task<ActionResult<CaseResponse>> RequestDecision(Guid id, CancellationToken ct) =>
        TransitionAsync(id, "request-decision", ct);

    [HttpPost("{id:guid}/request-documents")]
    public Task<ActionResult<CaseResponse>> RequestDocuments(Guid id, CancellationToken ct) =>
        TransitionAsync(id, "request-documents", ct);

    private async Task<ActionResult<CaseResponse>> TransitionAsync(Guid id, string action, CancellationToken ct)
    {
        var c = await caseAccessor.GetScopedCaseAsync(db, id, ct, includeApplicant: true);

        // CaseStateMachine.Resolve throws ConflictApiException/ForbiddenApiException for an
        // illegal transition or missing role; nothing is written to the database in that case.
        var transition = CaseStateMachine.Resolve(action, c.Status, currentUser.Role);
        c.Status = transition.To;

        // The status change and its audit event are added to the same DbContext and committed
        // by a single SaveChangesAsync call, so they land in one transaction together.
        audit.Record(db, c.Id, $"Case.{action}", AuditOutcome.Success);
        await db.SaveChangesAsync(ct);

        return CaseResponse.From(c, await FindingScoresAsync(c.Id, ct), screeningOptions);
    }

    private async Task<List<(string Code, double? Score)>> FindingScoresAsync(Guid caseId, CancellationToken ct) =>
        (await db.Findings.Where(f => f.CaseId == caseId).Select(f => new { f.Code, f.Score }).ToListAsync(ct))
            .Select(f => (f.Code, f.Score))
            .ToList();
}
