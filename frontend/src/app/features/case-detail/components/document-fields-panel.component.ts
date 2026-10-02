import { Component, computed, input } from '@angular/core';
import { DatePipe } from '@angular/common';

import { DocumentResponse } from '@caseauth/angular-client/src/models/document-response';
import { AiReviewInputFieldResponse } from '@caseauth/angular-client/src/models/ai-review-input-field-response';

interface FieldRow {
  field: AiReviewInputFieldResponse;
  citingFindingCodes: string[];
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
                <tr [class.fields-table__row--flagged]="row.citingFindingCodes.length > 0">
                  <th scope="row">{{ row.field.fieldName }}</th>
                  <td>
                    {{ row.field.fieldValue }}
                    @if (row.citingFindingCodes.length > 0) {
                      <span class="badge badge--warning" [title]="'Cited by finding(s): ' + row.citingFindingCodes.join(', ')">
                        ⚠ {{ row.citingFindingCodes.join(', ') }}
                      </span>
                    }
                  </td>
                  <td>{{ row.field.confidence != null ? (row.field.confidence * 100).toFixed(0) + '%' : '—' }}</td>
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
  `,
  imports: [DatePipe],
})
export class DocumentFieldsPanelComponent {
  readonly documents = input.required<DocumentResponse[]>();
  readonly fields = input.required<AiReviewInputFieldResponse[]>();
  // Maps an extracted field's id to the codes of findings that cite it as a mismatch source.
  readonly citingFindingCodesByFieldId = input.required<ReadonlyMap<string, string[]>>();

  readonly groups = computed<DocumentGroup[]>(() => {
    const citing = this.citingFindingCodesByFieldId();
    return this.documents().map((document) => ({
      document,
      fields: this.fields()
        .filter((f) => f.documentId === document.id)
        .map((field) => ({ field, citingFindingCodes: citing.get(field.id!) ?? [] })),
    }));
  });
}
