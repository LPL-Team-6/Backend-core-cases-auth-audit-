namespace CaseAuth.Api.Entities;

// Recorded verbatim from an upstream review pipeline; this API does not call a model itself.
// See Services/AiReviewService for the deterministic-recording note.
public class AiReview
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid CaseId { get; set; }
    public Case? Case { get; set; }

    // Monotonically increasing per case, starting at 1. Decisions reference this version
    // so the audit trail records exactly which review version was approved against.
    public int Version { get; set; }

    public required string ModelName { get; set; }
    public required string ModelVersion { get; set; }
    public AiRecommendation Recommendation { get; set; }
    public required string Rationale { get; set; }

    // Structured output (Teammate 4's planned shape). All optional so a reviewer that only
    // produces a rationale still records. Each concern cites the finding codes it is about, and
    // AiReviewsController rejects a citation to a code the case doesn't have.
    public string? Summary { get; set; }
    public List<AiConcern> KeyConcerns { get; set; } = [];
    public List<string> NextSteps { get; set; } = [];

    // The model's suggested case note. The analyst's edited copy lives in CaseNote, so editing
    // it never changes the recorded review a decision was made against.
    public string? DraftCaseNote { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public record AiConcern(string Text, List<string> FindingCodes);
