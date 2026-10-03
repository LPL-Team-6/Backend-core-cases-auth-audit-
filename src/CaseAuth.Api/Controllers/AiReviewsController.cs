using CaseAuth.Api.Contracts;
using CaseAuth.Api.Data;
using CaseAuth.Api.Entities;
using CaseAuth.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaseAuth.Api.Controllers;

// Records a review handed to this API by an upstream pipeline - it does not call a model
// itself. There is no live or simulated AI call wired up in this scaffold; label any demo
// that calls this endpoint as "recorded", not "AI-generated live".
[ApiController]
[Route("api/cases/{caseId:guid}/ai-reviews")]
[Authorize]
public class AiReviewsController(CaseAuthDbContext db, ICaseAccessor caseAccessor, IAuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AiReviewResponse>>> List(Guid caseId, CancellationToken ct)
    {
        await caseAccessor.GetScopedCaseAsync(db, caseId, ct);
        var reviews = await db.AiReviews
            .Where(r => r.CaseId == caseId)
            .OrderBy(r => r.Version)
            .ToListAsync(ct);
        return reviews.Select(AiReviewResponse.From).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<AiReviewResponse>> Create(Guid caseId, [FromBody] CreateAiReviewRequest request, CancellationToken ct)
    {
        var c = await caseAccessor.GetScopedCaseAsync(db, caseId, ct);
        var keyConcerns = request.KeyConcerns ?? [];
        await AiReviewCitations.EnsureValidAsync(db, c.Id, keyConcerns, ct);

        var nextVersion = 1 + await db.AiReviews
            .Where(r => r.CaseId == caseId)
            .Select(r => (int?)r.Version)
            .MaxAsync(ct) ?? 1;

        var review = new AiReview
        {
            CaseId = c.Id,
            Version = nextVersion,
            ModelName = request.ModelName,
            ModelVersion = request.ModelVersion,
            Recommendation = request.Recommendation,
            Rationale = request.Rationale,
            Summary = request.Summary,
            KeyConcerns = keyConcerns,
            NextSteps = request.NextSteps ?? [],
            DraftCaseNote = request.DraftCaseNote,
        };

        db.AiReviews.Add(review);
        audit.Record(db, c.Id, "AiReview.Recorded", AuditOutcome.Success, aiReviewVersion: review.Version);
        await db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(List), new { caseId }, AiReviewResponse.From(review));
    }
}
