import { CaseStatus } from '@caseauth/angular-client/src/models/case-status';

export type WhoseTurn = 'advisor' | 'reviewer' | 'none';

interface PlainStatus {
  label: string;
  turn: WhoseTurn;
  badgeClass: string;
}

// R8: a friendly status next to the internal state. Pipeline states (Extracted/Screened/
// AiReviewed) collapse into "Under compliance review" alongside AwaitingDecision - in a real
// product these run automatically; nothing for either the advisor or reviewer to act on until
// a decision is needed.
const PLAIN_STATUS: Record<CaseStatus, PlainStatus> = {
  Uploaded: { label: 'Waiting on you', turn: 'advisor', badgeClass: 'badge--warning' },
  Extracted: { label: 'Under compliance review', turn: 'none', badgeClass: 'badge--neutral' },
  Screened: { label: 'Under compliance review', turn: 'none', badgeClass: 'badge--neutral' },
  AiReviewed: { label: 'Under compliance review', turn: 'none', badgeClass: 'badge--neutral' },
  AwaitingDecision: { label: 'Under compliance review', turn: 'reviewer', badgeClass: 'badge--warning' },
  Approved: { label: 'Ready to open', turn: 'none', badgeClass: 'badge--success' },
  Rejected: { label: 'Not approved', turn: 'none', badgeClass: 'badge--danger' },
  Escalated: { label: 'Escalated', turn: 'none', badgeClass: 'badge--warning' },
};

export function plainStatus(status: CaseStatus): PlainStatus {
  return PLAIN_STATUS[status];
}
