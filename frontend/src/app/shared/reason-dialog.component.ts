import { Component, ElementRef, computed, output, signal, viewChild } from '@angular/core';

// A native <dialog> gives us focus trapping, Escape-to-close, returning focus to the
// triggering button on close, and inert background content for free - no custom focus-trap
// logic needed to meet the accessibility requirements.
@Component({
  selector: 'app-reason-dialog',
  template: `
    <dialog #dialogEl (close)="onNativeClose()">
      <form method="dialog" (submit)="onSubmit($event)">
        <h2>{{ title() }}</h2>
        <p class="reason-dialog__description">{{ description() }}</p>
        <label for="reason-dialog-note">
          Reason (required)
          <textarea
            id="reason-dialog-note"
            rows="3"
            required
            autofocus
            [value]="note()"
            (input)="note.set($any($event.target).value)"
          ></textarea>
        </label>
        @if (showValidationError()) {
          <p class="reason-dialog__validation" role="alert">A reason is required.</p>
        }
        <p class="reason-dialog__hint">
          This note is not yet persisted by the API - CreateDecisionRequest has no field for it
          (see MISSING_ENDPOINTS.md). It still gates the action here so nobody approves/rejects
          without writing down why.
        </p>
        <div class="reason-dialog__actions">
          <button type="button" (click)="dialog().nativeElement.close('cancel')">Cancel</button>
          <button
            type="submit"
            value="submit"
            class="reason-dialog__confirm"
            [class.reason-dialog__confirm--danger]="danger()"
          >
            {{ confirmLabel() }}
          </button>
        </div>
      </form>
    </dialog>
  `,
  styles: `
    dialog {
      border: 1px solid var(--color-border);
      border-radius: var(--radius);
      background: var(--color-surface);
      color: var(--color-text);
      padding: 1.25rem;
      max-width: 420px;
    }

    dialog::backdrop {
      background: rgb(0 0 0 / 0.4);
    }

    h2 {
      margin-top: 0;
      margin-bottom: 0.35rem;
    }

    .reason-dialog__description {
      color: var(--color-text-muted);
      font-size: 0.9rem;
      margin-top: 0;
    }

    label {
      display: flex;
      flex-direction: column;
      gap: 0.35rem;
      font-size: 0.9rem;
    }

    textarea {
      border: 1px solid var(--color-border);
      border-radius: var(--radius);
      padding: 0.5rem;
      background: var(--color-bg);
      color: var(--color-text);
      resize: vertical;
    }

    .reason-dialog__validation {
      color: var(--color-danger);
      font-size: 0.85rem;
      margin: 0.35rem 0 0;
    }

    .reason-dialog__hint {
      font-size: 0.8rem;
      color: var(--color-text-muted);
    }

    .reason-dialog__actions {
      display: flex;
      justify-content: flex-end;
      gap: 0.5rem;
      margin-top: 0.75rem;
    }

    button {
      border-radius: var(--radius);
      padding: 0.4rem 0.9rem;
      border: 1px solid var(--color-border);
      background: var(--color-surface);
      color: var(--color-text);
    }

    .reason-dialog__confirm {
      background: var(--color-accent);
      color: var(--color-accent-contrast);
      border-color: var(--color-accent);
      font-weight: 600;
    }

    .reason-dialog__confirm--danger {
      background: var(--color-danger);
      border-color: var(--color-danger);
    }
  `,
})
export class ReasonDialogComponent {
  readonly title = signal('Confirm');
  readonly description = signal('');
  readonly confirmLabel = signal('Confirm');
  readonly danger = signal(false);
  readonly note = signal('');
  readonly touched = signal(false);

  readonly showValidationError = computed(() => this.touched() && this.note().trim().length === 0);

  readonly confirm = output<string>();
  readonly cancelled = output<void>();

  readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialogEl');

  open(options: { title: string; description: string; confirmLabel: string; danger?: boolean }): void {
    this.title.set(options.title);
    this.description.set(options.description);
    this.confirmLabel.set(options.confirmLabel);
    this.danger.set(options.danger ?? false);
    this.note.set('');
    this.touched.set(false);
    this.dialog().nativeElement.returnValue = '';
    this.dialog().nativeElement.showModal();
  }

  onSubmit(event: Event): void {
    this.touched.set(true);
    if (this.note().trim().length === 0) {
      event.preventDefault();
      return;
    }
    this.confirm.emit(this.note().trim());
  }

  private onNativeClose(): void {
    if (this.dialog().nativeElement.returnValue !== 'submit') {
      this.cancelled.emit();
    }
  }
}
