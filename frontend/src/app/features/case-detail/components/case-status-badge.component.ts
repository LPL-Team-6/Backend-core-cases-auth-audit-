import { Component, computed, input } from '@angular/core';

import { CaseStatus } from '@caseauth/angular-client/src/models/case-status';

const BADGE_CLASS: Record<CaseStatus, string> = {
  Uploaded: 'badge--neutral',
  Extracted: 'badge--neutral',
  Screened: 'badge--neutral',
  AiReviewed: 'badge--neutral',
  AwaitingDecision: 'badge--warning',
  Approved: 'badge--success',
  Rejected: 'badge--danger',
  Escalated: 'badge--warning',
};

// "AwaitingDecision" -> "Awaiting Decision".
function toLabel(status: CaseStatus): string {
  return status.replace(/([a-z])([A-Z])/g, '$1 $2');
}

@Component({
  selector: 'app-case-status-badge',
  template: `<span class="badge" [class]="badgeClass()">{{ label() }}</span>`,
})
export class CaseStatusBadgeComponent {
  readonly status = input.required<CaseStatus>();

  readonly badgeClass = computed(() => BADGE_CLASS[this.status()]);
  readonly label = computed(() => toLabel(this.status()));
}
