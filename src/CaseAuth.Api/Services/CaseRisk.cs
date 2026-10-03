using CaseAuth.Api.Entities;
using CaseAuth.Api.Screening;

namespace CaseAuth.Api.Services;

// Case-level risk, scored the same way the rules engine scores a screening run: the sum of each
// finding's rule weight times its risk (Finding.Score), with the engine's Medium/High thresholds.
// So the queue's number always matches the engine's TotalScore for the same findings. Findings
// whose code isn't an engine rule (a manual analyst note, say) carry no weight. Computed on read
// rather than stored, so it can't drift from the findings it summarizes.
public static class CaseRisk
{
    public static double Score(IEnumerable<(string Code, double? Score)> findings, ScreeningOptions options) =>
        findings
            .Where(f => options.Rules.ContainsKey(f.Code))
            .GroupBy(f => f.Code)
            // The engine raises each rule at most once per run, at its highest risk.
            .Sum(g => options.Rules[g.Key].Weight * g.Max(f => Math.Clamp(f.Score ?? 1, 0, 1)));

    public static FindingSeverity Tier(double score, ScreeningOptions options) =>
        score >= options.HighThreshold ? FindingSeverity.High
        : score >= options.MediumThreshold ? FindingSeverity.Medium
        : FindingSeverity.Low;
}
