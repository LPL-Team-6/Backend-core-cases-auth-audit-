using CaseAuth.Api.Entities;

namespace CaseAuth.Api.Pipeline;

public record ScreeningFindingResult(
    string Code,
    FindingSeverity Severity,
    string Message,
    double? Score,
    IReadOnlyList<Guid> SourceFieldIds);

// Owned by Teammate 3 per the project brief (name matching, cross-document consistency,
// validity checks, sanctions screening, red flags). Implemented by RuleEngineScreeningService,
// which wraps the deterministic rules engine in ../Screening/ScreeningEngine.cs.
public interface IScreeningService
{
    Task<IReadOnlyList<ScreeningFindingResult>> ScreenAsync(Guid caseId, CancellationToken ct);
}

// Kept for tests/demoing without the rules engine. No longer registered by default - see
// Program.cs.
public class FixtureScreeningService : IScreeningService
{
    public Task<IReadOnlyList<ScreeningFindingResult>> ScreenAsync(Guid caseId, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ScreeningFindingResult>>([]);
}
