import { Component, computed, input } from '@angular/core';

import { FindingResponse } from '@caseauth/angular-client/src/models/finding-response';
import { FindingSeverity } from '@caseauth/angular-client/src/models/finding-severity';

const SEVERITY_BADGE: Record<FindingSeverity, string> = {
  Low: 'badge--neutral',
  Medium: 'badge--warning',
  High: 'badge--danger',
};

interface FindingRow {
  finding: FindingResponse;
  sourceFieldLabels: string[];
}

@Component({
  selector: 'app-findings-panel',
  template: `
    <h2>Findings</h2>
    @if (rows().length === 0) {
      <p>No findings recorded.</p>
    } @else {
      <ul class="findings-list">
        @for (row of rows(); track row.finding.id) {
          <li class="findings-list__item">
            <div class="findings-list__headline">
              <span class="badge" [class]="severityBadge(row.finding.severity)">{{ row.finding.severity }}</span>
              <strong>{{ row.finding.code }}</strong>
              @if (row.finding.score != null) {
                <span class="findings-list__score">score {{ row.finding.score }}</span>
              }
            </div>
            <p>{{ row.finding.message }}</p>
            @if (row.sourceFieldLabels.length > 0) {
              <p class="findings-list__sources">Source fields: {{ row.sourceFieldLabels.join(', ') }}</p>
            }
          </li>
        }
      </ul>
    }
  `,
  styles: `
    .findings-list {
      list-style: none;
      padding: 0;
      margin: 0;
      display: flex;
      flex-direction: column;
      gap: 0.75rem;
    }

    .findings-list__item {
      border: 1px solid var(--color-border);
      border-radius: var(--radius);
      background: var(--color-surface);
      padding: 0.75rem 1rem;
    }

    .findings-list__headline {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      margin-bottom: 0.35rem;
    }

    .findings-list__score {
      color: var(--color-text-muted);
      font-size: 0.85rem;
    }

    .findings-list__sources {
      color: var(--color-text-muted);
      font-size: 0.85rem;
      margin: 0;
    }
  `,
})
export class FindingsPanelComponent {
  readonly findings = input.required<FindingResponse[]>();
  // "<field name>: <value>" per extracted field id, so findings can cite fields by name instead
  // of a raw guid.
  readonly fieldLabelsById = input.required<ReadonlyMap<string, string>>();

  readonly rows = computed<FindingRow[]>(() => {
    const labels = this.fieldLabelsById();
    return this.findings().map((finding) => ({
      finding,
      sourceFieldLabels: (finding.sourceFieldIds ?? []).map((id) => labels.get(id) ?? id),
    }));
  });

  severityBadge(severity: FindingSeverity | undefined): string {
    return severity ? SEVERITY_BADGE[severity] : 'badge--neutral';
  }
}
