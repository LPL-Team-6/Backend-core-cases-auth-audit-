import type { CaseResponse, ReviewFinding, Severity } from "./api";

// Case risk comes from the API: the rules engine's weighted score in points (Medium from 20,
// High from 50 with the default ruleset), so the queue and the engine never disagree.
export function riskFor(c: CaseResponse): { score: number; tier: Severity } {
  return { score: c.riskScore ?? 0, tier: c.riskTier ?? "Low" };
}

export const LOW_CONFIDENCE = 0.8;

const SEV_RANK: Record<Severity, number> = { Low: 1, Medium: 2, High: 3 };

// Most serious first: severity, then the rule's risk for this case.
export function bySeriousness(a: ReviewFinding, b: ReviewFinding): number {
  return SEV_RANK[b.severity] - SEV_RANK[a.severity] || (b.score ?? 1) - (a.score ?? 1);
}
