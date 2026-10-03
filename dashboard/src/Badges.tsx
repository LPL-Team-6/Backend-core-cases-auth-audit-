import type { Severity } from "./api";

export function RiskBadge({ score, tier }: { score: number; tier: Severity }) {
  return (
    <span className={`risk risk-${tier.toLowerCase()}`} title="Rules engine score: each flag's weight times its risk. Medium from 20, High from 50.">
      <span className="risk-score">{Math.round(score)}</span>
      <span className="risk-tier">{tier}</span>
    </span>
  );
}

export function SeverityTag({ severity }: { severity: Severity }) {
  return <span className={`sev sev-${severity.toLowerCase()}`}>{severity}</span>;
}

const STATUS_LABEL: Record<string, string> = {
  AwaitingDecision: "Awaiting decision",
  AiReviewed: "AI reviewed",
};

export function StatusTag({ status }: { status: string }) {
  return <span className={`status status-${status.toLowerCase()}`}>{STATUS_LABEL[status] ?? status}</span>;
}

export function RecommendationTag({ rec }: { rec: string }) {
  return <span className={`rec rec-${rec.toLowerCase()}`}>{rec}</span>;
}
