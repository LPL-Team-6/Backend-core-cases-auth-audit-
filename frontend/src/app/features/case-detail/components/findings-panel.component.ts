import { Component, computed, input, output } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';

import { FindingResponse } from '@caseauth/angular-client/src/models/finding-response';
import { FindingSeverity } from '@caseauth/angular-client/src/models/finding-severity';
import { findingToTodo } from '../../../core/finding-todo';

const SEVERITY_BADGE: Record<FindingSeverity, string> = {
  Low: 'badge--neutral',
  Medium: 'badge--warning',
  High: 'badge--danger',
};

// Icon + text, never color alone, for severity - a colorblind reviewer or anyone on a
// grayscale/high-contrast screen still needs to tell High from Low at a glance.
const SEVERITY_ICON: Record<FindingSeverity, string> = {
  Low: 'ℹ',
  Medium: '▲',
  High: '⛔',
};

interface FindingRow {
  finding: FindingResponse;
  sourceFieldLabels: string[];
}

@Component({
  selector: 'app-findings-panel',
  imports: [NgTemplateOutlet],
  template: `
    <h2>Findings</h2>
    @if (blocking().length === 0 && fyi().length === 0) {
      <p>No findings recorded.</p>
    } @else {
      @if (blocking().length === 0) {
        <p>No open (non-FYI) findings.</p>
      } @else {
        <ul class="findings-list">
          @for (row of blocking(); track row.finding.id) {
            <li><ng-container *ngTemplateOutlet="rowTpl; context: { row }" /></li>
          }
        </ul>
      }

      <!-- R14: resolved/FYI findings collapse below the open ones. -->
      @if (fyi().length > 0) {
        <details class="findings-list__fyi">
          <summary>FYI ({{ fyi().length }})</summary>
          <ul class="findings-list">
            @for (row of fyi(); track row.finding.id) {
              <li><ng-container *ngTemplateOutlet="rowTpl; context: { row }" /></li>
            }
          </ul>
        </details>
      }
    }

    <ng-template #rowTpl let-row="row">
      <button
        type="button"
        class="findings-list__item"
        [class.findings-list__item--selected]="row.finding.id === selectedFindingId()"
        [attr.aria-pressed]="row.finding.id === selectedFindingId()"
        (click)="toggle(row.finding.id!)"
      >
        <div class="findings-list__headline">
          <span class="badge" [class]="severityBadge(row.finding.severity)">
            <span aria-hidden="true">{{ severityIcon(row.finding.severity) }}</span>
            {{ row.finding.severity }}
          </span>
          <strong>{{ row.finding.code }}</strong>
          @if (row.finding.score !== null && row.finding.score !== undefined) {
            <span class="findings-list__score">score {{ row.finding.score }}</span>
          }
        </div>
        <p>{{ row.finding.message }}</p>
        @if (row.sourceFieldLabels.length > 0) {
          <p class="findings-list__sources">
            Source fields: {{ row.sourceFieldLabels.join(', ') }} -
            {{ row.finding.id === selectedFindingId() ? 'click to unhighlight' : 'click to highlight on documents' }}
          </p>
        }
      </button>
    </ng-template>
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

    .findings-list__fyi {
      margin-top: 0.75rem;
      color: var(--color-text-muted);
    }

    .findings-list__item {
      display: block;
      width: 100%;
      text-align: left;
      border: 1px solid var(--color-border);
      border-radius: var(--radius);
      background: var(--color-surface);
      color: var(--color-text);
      padding: 0.75rem 1rem;
      font: inherit;
    }

    .findings-list__item--selected {
      border-color: var(--color-accent);
      box-shadow: 0 0 0 1px var(--color-accent);
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
  // "<field name>: <value>" per extracted field id - lets findings cite fields by name instead
  // of a raw guid.
  readonly fieldLabelsById = input.required<ReadonlyMap<string, string>>();
  readonly selectedFindingId = input<string | null>(null);
  readonly selectFinding = output<string | null>();

  private readonly rows = computed<FindingRow[]>(() => {
    const labels = this.fieldLabelsById();
    return this.findings().map((finding) => ({
      finding,
      sourceFieldLabels: (finding.sourceFieldIds ?? []).map((id) => labels.get(id) ?? id),
    }));
  });

  // Same Blocking/FYI split as the advisor to-do list (R2), so a finding is never "Blocking"
  // on one screen and "FYI" on another.
  readonly blocking = computed(() => this.rows().filter((r) => findingToTodo(r.finding).tag === 'Blocking'));
  readonly fyi = computed(() => this.rows().filter((r) => findingToTodo(r.finding).tag === 'FYI'));

  severityBadge(severity: FindingSeverity | undefined): string {
    return severity ? SEVERITY_BADGE[severity] : 'badge--neutral';
  }

  severityIcon(severity: FindingSeverity | undefined): string {
    return severity ? SEVERITY_ICON[severity] : 'ℹ';
  }

  toggle(findingId: string): void {
    this.selectFinding.emit(this.selectedFindingId() === findingId ? null : findingId);
  }
}
