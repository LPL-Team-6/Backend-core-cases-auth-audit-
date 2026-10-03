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

// The analyst-editable case note (UX brief R15). GET falls back to the latest AI review's draft
// until someone saves; PUT is safe to call on every autosave, since saving the same text again
// is a no-op and doesn't add an audit event. The note locks once the case is approved or
// rejected, so the record a decision was filed with can't be rewritten afterwards.
[ApiController]
[Route("api/cases/{caseId:guid}/case-note")]
[Authorize]
public class CaseNotesController(
    CaseAuthDbContext db, ICaseAccessor caseAccessor, ICurrentUser currentUser, IAuditService audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CaseNoteResponse>> Get(Guid caseId, CancellationToken ct)
    {
        var c = await caseAccessor.GetScopedCaseAsync(db, caseId, ct);
        var note = await db.CaseNotes.FirstOrDefaultAsync(n => n.CaseId == c.Id, ct);
        if (note is not null)
        {
            return Saved(note, c);
        }

        var latestDraft = await db.AiReviews
            .Where(r => r.CaseId == c.Id && r.DraftCaseNote != null)
            .OrderByDescending(r => r.Version)
            .Select(r => new { r.Version, r.DraftCaseNote })
            .FirstOrDefaultAsync(ct);

        return latestDraft is null
            ? new CaseNoteResponse("", "Empty", null, null, null, IsLocked(c))
            : new CaseNoteResponse(latestDraft.DraftCaseNote!, "AiDraft", latestDraft.Version, null, null, IsLocked(c));
    }

    [HttpPut]
    public async Task<ActionResult<CaseNoteResponse>> Save(Guid caseId, [FromBody] SaveCaseNoteRequest request, CancellationToken ct)
    {
        var c = await caseAccessor.GetScopedCaseAsync(db, caseId, ct);
        if (IsLocked(c))
        {
            throw new ConflictApiException($"The case note is locked because the case is {c.Status}.");
        }

        if (request.BasedOnAiReviewVersion is { } version
            && !await db.AiReviews.AnyAsync(r => r.CaseId == c.Id && r.Version == version, ct))
        {
            throw new ValidationApiException($"AI review version {version} does not exist on this case.");
        }

        var note = await db.CaseNotes.FirstOrDefaultAsync(n => n.CaseId == c.Id, ct);
        if (note is not null && note.Text == request.Text && note.BasedOnAiReviewVersion == request.BasedOnAiReviewVersion)
        {
            return Saved(note, c);
        }

        if (note is null)
        {
            note = new CaseNote { CaseId = c.Id, Text = request.Text, UpdatedByUserId = currentUser.UserId };
            db.CaseNotes.Add(note);
        }

        note.Text = request.Text;
        note.BasedOnAiReviewVersion = request.BasedOnAiReviewVersion;
        note.UpdatedByUserId = currentUser.UserId;
        note.UpdatedAt = DateTime.UtcNow;

        // Length only: the note itself is free text about the applicant and stays out of audit metadata.
        audit.Record(db, c.Id, "CaseNote.Saved", AuditOutcome.Success,
            aiReviewVersion: request.BasedOnAiReviewVersion,
            metadata: $"{{\"length\":{request.Text.Length}}}");
        await db.SaveChangesAsync(ct);

        return Saved(note, c);
    }

    private static bool IsLocked(Case c) => c.Status is CaseStatus.Approved or CaseStatus.Rejected;

    private static CaseNoteResponse Saved(CaseNote note, Case c) =>
        new(note.Text, "Saved", note.BasedOnAiReviewVersion, note.UpdatedByUserId, note.UpdatedAt, IsLocked(c));
}
