import { Component, computed, input } from '@angular/core';

import { CaseResponse } from '@caseauth/angular-client/src/models/case-response';
import { PlainStatusBadgeComponent } from './plain-status-badge.component';

// R7: client name, blocking-issue count, days open, visible without scrolling at 1366x768.
// "Account type" and "risk tier" are in the brief's design but NOT in the API - Case and
// Applicant have no such fields (see root README's entity list and MISSING_ENDPOINTS.md).
// Showing them would mean fabricating data, so this bar only surfaces what's real.
@Component({
  selector: 'app-case-summary-bar',
  imports: [PlainStatusBadgeComponent],
  template: `
    <div class="summary-bar">
      <div class="summary-bar__identity">
        <h1>{{ clientName() }}</h1>
        <app-plain-status-badge [status]="case().status!" />
      </div>
      <dl class="summary-bar__stats">
        <div class="summary-bar__stat">
          <dt>Blocking issues</dt>
          <dd [class.summary-bar__stat--alert]="blockingCount() > 0">{{ blockingCount() }}</dd>
        </div>
        <div class="summary-bar__stat">
          <dt>Days open</dt>
          <dd>{{ daysOpen() }}</dd>
        </div>
      </dl>
    </div>
  `,
  styles: `
    .summary-bar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
      flex-wrap: wrap;
      padding: 0.75rem 1rem;
      background: var(--color-surface);
      border: 1px solid var(--color-border);
      border-radius: var(--radius);
      margin-bottom: 1rem;
    }

    .summary-bar__identity {
      display: flex;
      align-items: center;
      gap: 0.75rem;

      h1 {
        margin: 0;
        font-size: 1.3rem;
      }
    }

    .summary-bar__stats {
      display: flex;
      gap: 1.5rem;
      margin: 0;
    }

    .summary-bar__stat {
      text-align: right;

      dt {
        font-size: 0.75rem;
        color: var(--color-text-muted);
        text-transform: uppercase;
        letter-spacing: 0.03em;
      }

      dd {
        margin: 0;
        font-size: 1.3rem;
        font-weight: 700;
      }

      dd.summary-bar__stat--alert {
        color: var(--color-danger);
      }
    }
  `,
})
export class CaseSummaryBarComponent {
  readonly case = input.required<CaseResponse>();
  readonly blockingCount = input.required<number>();

  readonly clientName = computed(() => this.case().applicantFullName || '(unnamed applicant)');

  readonly daysOpen = computed(() => {
    const createdAt = this.case().createdAt;
    if (!createdAt) return 0;
    const ms = Date.now() - new Date(createdAt).getTime();
    return Math.max(0, Math.floor(ms / (1000 * 60 * 60 * 24)));
  });
}
