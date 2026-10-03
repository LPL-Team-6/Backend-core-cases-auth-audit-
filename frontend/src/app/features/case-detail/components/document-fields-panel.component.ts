import { Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';

import { DocumentResponse } from '@caseauth/angular-client/src/models/document-response';
import { AiReviewInputFieldResponse } from '@caseauth/angular-client/src/models/ai-review-input-field-response';

interface Citer {
  code: string;
  score: number | null;
}

interface FieldRow {
  field: AiReviewInputFieldResponse;
  citers: Citer[];
  selected: boolean;
}

interface DocumentGroup {
  document: DocumentResponse;
  fields: FieldRow[];
}

// "the document image next to its extracted fields, with mismatches highlighted" (brief). The
// backend has no endpoint yet to serve an uploaded document's bytes back (DocumentsController
// only exposes List/Upload - see Document.StorageKey, which is never read by any GET route), so
// this renders the document's metadata card in place of an image until that endpoint exists.
@Component({
  selector: 'app-document-fields-panel',
  template: `
    <h2>Documents &amp; extracted fields</h2>
    @if (groups().length === 0) {
      <p>No documents uploaded yet.</p>
    }
    @for (group of groups(); track group.document.id) {
      <article class="document-card">
        <header class="document-card__meta">
          <strong>{{ group.document.fileName }}</strong>
          <span class="badge badge--neutral">{{ group.document.documentType }}</span>
          <span class="document-card__hint">
            {{ group.document.contentType }} · {{ group.document.sizeBytes }} bytes · uploaded
            {{ group.document.uploadedAt | date: 'short' }}
          </span>
        </header>

        @if (group.fields.length === 0) {
          <p class="document-card__hint">No fields extracted from this document yet.</p>
        } @else {
          <table class="fields-table">
            <caption class="sr-only">Extracted fields for {{ group.document.fileName }}</caption>
            <thead>
              <tr>
                <th scope="col">Field</th>
                <th scope="col">Value</th>
                <th scope="col">Confidence</th>
              </tr>
            </thead>
            <tbody>
              @for (row of group.fields; track row.field.id) {
                <tr
                  [class.fields-table__row--flagged]="row.citers.length > 0"
                  [class.fields-table__row--selected]="row.selected"
                >
                  <th scope="row">{{ row.field.fieldName }}</th>
                  <td>
                    {{ row.field.fieldValue }}
                    @if (row.citers.length > 0) {
                      <span class="badge badge--warning" [title]="'Cited by finding(s): ' + citerCodes(row)">
                        <span aria-hidden="true">⚠</span> {{ citerCodes(row) }}
                      </span>
                    }
                  </td>
                  <td>
                    {{ confidencePercent(row.field.confidence) ?? '—' }}
                    @if (confidencePercent(row.field.confidence)) {
                      <details class="confidence-why">
                        <summary>Why?</summary>
                        <!-- R12: never show a confidence number that came from the model's own
                             text - both numbers here are read straight from ExtractedField and
                             Finding, never parsed out of AI-generated prose. -->
                        <p>Extraction confidence: {{ confidencePercent(row.field.confidence) }}</p>
                        @for (citer of row.citers; track citer.code) {
                          <p>Rule match score ({{ citer.code }}): {{ citer.score ?? 'n/a' }}</p>
                        }
                      </details>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        }
      </article>
    }
  `,
  styles: `
    .document-card {
      border: 1px solid var(--color-border);
      border-radius: var(--radius);
      background: var(--color-surface);
      padding: 1rem;
      margin-bottom: 1rem;
    }

    .document-card__meta {
      display: flex;
      align-items: center;
      gap: 0.6rem;
      margin-bottom: 0.75rem;
      flex-wrap: wrap;
    }

    .document-card__hint {
      color: var(--color-text-muted);
      font-size: 0.85rem;
    }

    .fields-table {
      width: 100%;
      border-collapse: collapse;

      th,
      td {
        text-align: left;
        padding: 0.4rem 0.6rem;
        border-bottom: 1px solid var(--color-border);
        font-weight: normal;
      }

      tbody tr:last-child th,
      tbody tr:last-child td {
        border-bottom: none;
      }
    }

    .fields-table__row--flagged {
      background: var(--color-warning-bg);
    }

    .fields-table__row--selected {
      background: var(--color-accent);
      color: var(--color-accent-contrast);
      outline: 2px solid var(--color-accent);
    }

    .confidence-why {
      display: inline;
      font-size: 0.8rem;
      color: var(--color-text-muted);

      summary {
        display: inline;
        cursor: pointer;
      }

      p {
        margin: 0.25rem 0 0;
      }
    }
  `,
  imports: [DatePipe],
})
export class DocumentFieldsPanelComponent {
  readonly documents = input.required<DocumentResponse[]>();
  readonly fields = input.required<AiReviewInputFieldResponse[]>();
  // Maps an extracted field's id to the findings (code + score) that cite it as a mismatch
  // source - drives both the always-on "cited by" badge and R12's "Why?" explanation.
  readonly citingFindingsByFieldId = input.required<ReadonlyMap<string, Citer[]>>();
  // Field ids belonging to whichever finding is currently selected in the findings panel (R1:
  // click-to-source) - distinct from citingFindingsByFieldId, which flags a field if ANY
  // finding cites it, not just the one the reviewer just clicked.
  readonly selectedFieldIds = input<ReadonlySet<string>>(new Set());

  readonly groups = computed<DocumentGroup[]>(() => {
    const citing = this.citingFindingsByFieldId();
    const selected = this.selectedFieldIds();
    return this.documents().map((document) => ({
      document,
      fields: this.fields()
        .filter((f) => f.documentId === document.id)
        .map((field) => ({
          field,
          citers: citing.get(field.id!) ?? [],
          selected: selected.has(field.id!),
        })),
    }));
  });

  citerCodes(row: FieldRow): string {
    return row.citers.map((c) => c.code).join(', ');
  }

  confidencePercent(confidence: number | null | undefined): string | null {
    return confidence === null || confidence === undefined ? null : `${(confidence * 100).toFixed(0)}%`;
  }
}
