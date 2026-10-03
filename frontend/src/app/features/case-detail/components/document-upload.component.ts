import { Component, inject, input, output, signal } from '@angular/core';

import { CaseApiService } from '../../../core/case-api.service';
import { ApiProblem, toApiProblem } from '../../../core/api-error';
import { DocumentType } from '@caseauth/angular-client/src/models/document-type';
import { ErrorBannerComponent } from '../../../shared/error-banner.component';

const DOCUMENT_TYPES: readonly DocumentType[] = ['GovernmentId', 'ProofOfAddress', 'Financial', 'Other'];

// Mirrors src/CaseAuth.Api/appsettings.json's Storage section by hand - no endpoint exposes
// this config, so these two constants will drift if that file changes (see
// MISSING_ENDPOINTS.md). Checking client-side first gives a specific, friendly message
// ("PDFs, PNGs and JPGs up to 15 MB") before ever hitting the network; the backend still
// enforces the real limit regardless of what this check does.
const MAX_UPLOAD_BYTES = 15 * 1024 * 1024;
const ALLOWED_CONTENT_TYPES = ['application/pdf', 'image/png', 'image/jpeg'];
const ALLOWED_DESCRIPTION = 'PDFs, PNGs and JPGs up to 15 MB';

function formatMb(bytes: number): string {
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

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
      <input id="upload-file" type="file" accept=".pdf,.png,.jpg,.jpeg" (change)="onFileSelected($any($event.target).files)" />
      <span class="upload__hint">{{ allowedDescription }}</span>

      <button type="submit" [disabled]="!file() || uploading()">{{ uploading() ? 'Uploading…' : 'Upload' }}</button>
    </form>

    @if (validationMessage()) {
      <p class="upload__validation" role="alert">{{ validationMessage() }}</p>
    }

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

    .upload__hint {
      color: var(--color-text-muted);
      font-size: 0.8rem;
    }

    .upload__validation {
      color: var(--color-danger);
      font-size: 0.85rem;
      margin: 0.35rem 0 0;
    }
  `,
})
export class DocumentUploadComponent {
  private readonly caseApi = inject(CaseApiService);

  readonly caseId = input.required<string>();
  readonly uploaded = output<void>();

  readonly documentTypes = DOCUMENT_TYPES;
  readonly allowedDescription = ALLOWED_DESCRIPTION;
  readonly documentType = signal<DocumentType>('GovernmentId');
  readonly file = signal<File | null>(null);
  readonly uploading = signal(false);
  readonly error = signal<ApiProblem | null>(null);
  readonly validationMessage = signal<string | null>(null);

  onFileSelected(files: FileList | null): void {
    const file = files?.[0] ?? null;
    this.error.set(null);

    if (file && file.size > MAX_UPLOAD_BYTES) {
      this.validationMessage.set(`${this.allowedDescription}. This file is ${formatMb(file.size)}.`);
      this.file.set(null);
      return;
    }
    if (file && !ALLOWED_CONTENT_TYPES.includes(file.type)) {
      this.validationMessage.set(`${this.allowedDescription}. This file is ${file.type || 'an unrecognized type'}.`);
      this.file.set(null);
      return;
    }

    this.validationMessage.set(null);
    this.file.set(file);
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
