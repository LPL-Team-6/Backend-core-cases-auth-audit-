using CaseAuth.Api.Data;
using CaseAuth.Api.Entities;
using CaseAuth.Api.Errors;
using Microsoft.EntityFrameworkCore;

namespace CaseAuth.Api.Services;

// The rules engine decides what is flagged; the AI reviewer only explains it. So every concern
// must cite at least one finding code, and every code it cites must already be a finding on the
// case. A review that invents a finding is rejected rather than recorded.
public static class AiReviewCitations
{
    public static async Task EnsureValidAsync(
        CaseAuthDbContext db, Guid caseId, IReadOnlyCollection<AiConcern> concerns, CancellationToken ct)
    {
        if (concerns.Count == 0)
        {
            return;
        }

        var known = (await db.Findings
            .Where(f => f.CaseId == caseId)
            .Select(f => f.Code)
            .Distinct()
            .ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);

        foreach (var concern in concerns)
        {
            if (string.IsNullOrWhiteSpace(concern.Text))
            {
                throw new ValidationApiException("Each key concern needs text.");
            }

            if (concern.FindingCodes is not { Count: > 0 })
            {
                throw new ValidationApiException("Each key concern must cite at least one finding code.");
            }

            var unknown = concern.FindingCodes.Where(code => !known.Contains(code)).ToList();
            if (unknown.Count > 0)
            {
                throw new ValidationApiException(
                    $"Key concern cites finding code(s) not on this case: {string.Join(", ", unknown)}.");
            }
        }
    }
}
