import { Component, input, output } from '@angular/core';

import { ApiProblem } from '../core/api-error';

@Component({
  selector: 'app-error-banner',
  template: `
    <div class="error-banner" role="alert">
      <p>{{ problem().message }}</p>
      @if (problem().correlationId) {
        <p class="error-banner__correlation">Correlation ID: {{ problem().correlationId }}</p>
      }
      <button type="button" (click)="retry.emit()">Retry</button>
    </div>
  `,
  styles: `
    .error-banner {
      border: 1px solid var(--color-danger);
      background: var(--color-danger-bg);
      color: var(--color-danger);
      border-radius: var(--radius);
      padding: 0.75rem 1rem;
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
      align-items: flex-start;
    }

    .error-banner__correlation {
      font-size: 0.8rem;
      opacity: 0.8;
      font-family: monospace;
    }

    button {
      border: 1px solid var(--color-danger);
      background: transparent;
      color: var(--color-danger);
      border-radius: var(--radius);
      padding: 0.3rem 0.8rem;
      font-weight: 600;
    }
  `,
})
export class ErrorBannerComponent {
  readonly problem = input.required<ApiProblem>();
  readonly retry = output<void>();
}
