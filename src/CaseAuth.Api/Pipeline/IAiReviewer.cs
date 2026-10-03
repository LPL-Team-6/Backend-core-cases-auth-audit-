using CaseAuth.Api.Entities;

namespace CaseAuth.Api.Pipeline;

public record AiReviewResult(
    string ModelName,
    string ModelVersion,
    AiRecommendation Recommendation,
    string Rationale,
    string? Summary = null,
    List<AiConcern>? KeyConcerns = null,
    List<string>? NextSteps = null,
    string? DraftCaseNote = null);

// Provisional - owned by Teammate 4 per the project brief (BedrockReviewer via the Converse
// API, plus a labeled DeterministicReviewer fallback). This exact signature is a guess at what
// their implementation needs; expect it to change once they're building against it. The richer
// output shape the brief describes (summary, key_concerns[], recommended_next_steps[],
// draft_case_note) is carried by the optional AiReviewResult fields.
public interface IAiReviewer
{
    Task<AiReviewResult> ReviewAsync(Guid caseId, CancellationToken ct);
}

// Registered by default until Teammate 4's Bedrock reviewer exists. Recommends Escalate, not
// Approve - a missing/misconfigured AI reviewer should never look like a clean approval; this
// is the "labeled deterministic demo provider" the project brief asks for.
public class DeterministicAiReviewer : IAiReviewer
{
    public Task<AiReviewResult> ReviewAsync(Guid caseId, CancellationToken ct) =>
        Task.FromResult(new AiReviewResult(
            ModelName: "deterministic-fallback",
            ModelVersion: "1.0",
            Recommendation: AiRecommendation.Escalate,
            Rationale: "No live AI reviewer is configured; escalating for manual review."));
}
