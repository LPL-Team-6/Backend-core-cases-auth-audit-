import { Component, computed, input } from '@angular/core';

import { AiReviewInputFieldResponse } from '@caseauth/angular-client/src/models/ai-review-input-field-response';
import { diffChars, DiffSegment } from '../../../core/text-diff';

interface ComparisonPair {
  fieldName: string;
  left: { documentType?: string; segments: DiffSegment[] };
  right: { documentType?: string; segments: DiffSegment[] };
}

// R1: "Show the actual difference: e.g. Smith vs Smyth with the differing characters
// highlighted." Pairs up same-named fields across different documents among the selected
// finding's source fields and diffs their values character-by-character.
@Component({
  selector: 'app-finding-comparison',
  template: `
    @if (pairs().length > 0) {
      <aside class="comparison" aria-label="Source field comparison">
        <h3>What's different</h3>
        @for (pair of pairs(); track pair.fieldName) {
          <div class="comparison__row">
            <span class="comparison__field-name">{{ pair.fieldName }}</span>
            <div class="comparison__values">
              <span class="comparison__value" [attr.title]="pair.left.documentType">
                @for (seg of pair.left.segments; track $index) {
                  <mark [class.comparison__changed]="seg.changed">{{ seg.text }}</mark>
                }
              </span>
              <span class="comparison__vs" aria-hidden="true">vs</span>
              <span class="comparison__value" [attr.title]="pair.right.documentType">
                @for (seg of pair.right.segments; track $index) {
                  <mark [class.comparison__changed]="seg.changed">{{ seg.text }}</mark>
                }
              </span>
            </div>
          </div>
        }
      </aside>
    }
  `,
  styles: `
    .comparison {
      border: 1px solid var(--color-accent);
      border-radius: var(--radius);
      background: var(--color-surface);
      padding: 0.75rem 1rem;
      margin-bottom: 1rem;
    }

    .comparison h3 {
      margin-top: 0;
      margin-bottom: 0.5rem;
      font-size: 0.95rem;
    }

    .comparison__row {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      flex-wrap: wrap;
      margin-bottom: 0.25rem;
    }

    .comparison__field-name {
      font-weight: 600;
      min-width: 6rem;
    }

    .comparison__values {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      font-family: monospace;
    }

    .comparison__vs {
      color: var(--color-text-muted);
      font-family: var(--font-sans);
      font-size: 0.8rem;
    }

    mark {
      background: none;
      color: inherit;
      padding: 0;
    }

    .comparison__changed {
      background: var(--color-danger-bg);
      color: var(--color-danger);
      border-radius: 3px;
      padding: 0 1px;
    }
  `,
})
export class FindingComparisonComponent {
  readonly fields = input.required<AiReviewInputFieldResponse[]>();

  readonly pairs = computed<ComparisonPair[]>(() => {
    const byName = new Map<string, AiReviewInputFieldResponse[]>();
    for (const field of this.fields()) {
      if (!field.fieldName || !field.fieldValue) continue;
      const list = byName.get(field.fieldName) ?? [];
      list.push(field);
      byName.set(field.fieldName, list);
    }

    const pairs: ComparisonPair[] = [];
    for (const [fieldName, fields] of byName) {
      if (fields.length < 2) continue;
      const [a, b] = fields;
      const { left, right } = diffChars(a.fieldValue!, b.fieldValue!);
      pairs.push({
        fieldName,
        left: { documentType: a.documentType, segments: left },
        right: { documentType: b.documentType, segments: right },
      });
    }
    return pairs;
  });
}
