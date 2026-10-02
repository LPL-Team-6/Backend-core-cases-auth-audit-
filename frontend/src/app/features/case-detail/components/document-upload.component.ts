import { Component, inject, input, output, signal } from '@angular/core';

import { CaseApiService } from '../../../core/case-api.service';
import { ApiProblem, toApiProblem } from '../../../core/api-error';
import { DocumentType } from '@caseauth/angular-client/src/models/document-type';
import { ErrorBannerComponent } from '../../../shared/error-banner.component';

const DOCUMENT_TYPES: readonly DocumentType[] = ['GovernmentId', 'ProofOfAddress', 'Financial', 'Other'];

@Component({
  selector: 'app-document-upload',
  imports: [ErrorBannerComponent],
  template: `
    <form class="upload" (submit)="$event.preventDefault(); submit()">
      <label for="upload-type">Document type</label>
      <select id="upload-type" [value]="documentType()" (change)="documentType.set($any($event.target).value)">
        @for (type of documentTypes; track type) {
          <option [value]="type">{{ type }}</option>
        }
      </select>

      <label for="upload-file">File</label>
      <input id="upload-file" type="file" (change)="onFileSelected($any($event.target).files)" />

      <button type="submit" [disabled]="!file() || uploading()">{{ uploading() ? 'Uploading…' : 'Upload' }}</button>
    </form>

    @if (error()) {
      <app-error-banner [problem]="error()!" (retry)="error.set(null)" />
    }
  `,
  styles: `
    .upload {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      flex-wrap: wrap;
      margin-top: 0.75rem;

      label {
        font-size: 0.85rem;
        color: var(--color-text-muted);
      }

      select,
      input[type='file'] {
        padding: 0.3rem 0.5rem;
        border-radius: var(--radius);
        border: 1px solid var(--color-border);
        background: var(--color-bg);
        color: var(--color-text);
      }

      button {
        border-radius: var(--radius);
        padding: 0.35rem 0.8rem;
        border: 1px solid var(--color-accent);
        background: var(--color-accent);
        color: var(--color-accent-contrast);
        font-weight: 600;
      }
    }
  `,
})
export class DocumentUploadComponent {
  private readonly caseApi = inject(CaseApiService);

  readonly caseId = input.required<string>();
  readonly uploaded = output<void>();

  readonly documentTypes = DOCUMENT_TYPES;
  readonly documentType = signal<DocumentType>('GovernmentId');
  readonly file = signal<File | null>(null);
  readonly uploading = signal(false);
  readonly error = signal<ApiProblem | null>(null);

  onFileSelected(files: FileList | null): void {
    this.file.set(files?.[0] ?? null);
  }

  submit(): void {
    const file = this.file();
    if (!file) {
      return;
    }
    this.uploading.set(true);
    this.error.set(null);
    this.caseApi.uploadDocument(this.caseId(), this.documentType(), file).subscribe({
      next: () => {
        this.uploading.set(false);
        this.file.set(null);
        this.uploaded.emit();
      },
      error: (err) => {
        this.uploading.set(false);
        this.error.set(toApiProblem(err));
      },
    });
  }
}
