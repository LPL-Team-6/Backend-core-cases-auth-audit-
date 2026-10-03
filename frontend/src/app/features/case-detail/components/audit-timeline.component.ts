import { Component, input } from '@angular/core';
import { DatePipe } from '@angular/common';

import { AuditEventResponse } from '@caseauth/angular-client/src/models/audit-event-response';

const OUTCOME_BADGE: Record<string, string> = {
  Success: 'badge--success',
  Rejected: 'badge--warning',
  Failure: 'badge--danger',
};

@Component({
  selector: 'app-audit-timeline',
  imports: [DatePipe],
  template: `
    <h2>Audit timeline</h2>
    @if (events().length === 0) {
      <p>No audit events recorded yet.</p>
    } @else {
      <ol class="timeline">
        @for (event of events(); track event.id) {
          <li class="timeline__item">
            <div class="timeline__row">
              <span class="badge" [class]="OUTCOME_BADGE[event.outcome!] ?? 'badge--neutral'">{{ event.outcome }}</span>
              <strong>{{ event.action }}</strong>
              <time [attr.datetime]="event.timestamp">{{ event.timestamp | date: 'medium' }}</time>
            </div>
            <div class="timeline__meta">
              {{ event.actorUsername }}
              @if (event.aiReviewVersion !== null && event.aiReviewVersion !== undefined) {
                · AI review v{{ event.aiReviewVersion }}
              }
              @if (event.correlationId) {
                · <span class="timeline__correlation">{{ event.correlationId }}</span>
              }
            </div>
          </li>
        }
      </ol>
    }
  `,
  styles: `
    .timeline {
      list-style: none;
      padding: 0;
      margin: 0;
      border-left: 2px solid var(--color-border);
    }

    .timeline__item {
      padding: 0.5rem 0 0.5rem 1rem;
      margin-left: -1px;
    }

    .timeline__row {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      flex-wrap: wrap;
    }

    .timeline__meta {
      color: var(--color-text-muted);
      font-size: 0.85rem;
      margin-top: 0.15rem;
    }

    .timeline__correlation {
      font-family: monospace;
    }
  `,
})
export class AuditTimelineComponent {
  readonly events = input.required<AuditEventResponse[]>();
  readonly OUTCOME_BADGE = OUTCOME_BADGE;
}
