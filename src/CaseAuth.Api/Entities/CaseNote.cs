namespace CaseAuth.Api.Entities;

// The analyst's working note for a case, one per case, saved as they type. Kept off the Case
// row on purpose: saving it must not regenerate Case.RowVersion, or every autosave would turn
// an open decision panel's If-Match stale.
public class CaseNote
{
    public Guid CaseId { get; set; }
    public Case? Case { get; set; }

    public required string Text { get; set; }

    // The AI review whose draft the analyst started from, if any.
    public int? BasedOnAiReviewVersion { get; set; }

    public required string UpdatedByUserId { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
