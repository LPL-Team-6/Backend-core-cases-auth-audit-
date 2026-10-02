import { Component, ElementRef, output, signal, viewChild } from '@angular/core';

// A native <dialog> gives us focus trapping, Escape-to-close and inert background content for
// free - no custom focus-trap logic needed to meet the brief's keyboard-navigation requirement.
@Component({
  selector: 'app-reason-dialog',
  template: `
    <dialog #dialogEl (close)="onNativeClose()">
      <form method="dialog" (submit)="confirm.emit(note())">
        <h2>{{ title() }}</h2>
        <label for="reason-dialog-note">
          Note for the audit trail (optional)
          <textarea
            id="reason-dialog-note"
            rows="3"
            [value]="note()"
            (input)="note.set($any($event.target).value)"
          ></textarea>
        </label>
        <p class="reason-dialog__hint">
          This note is not yet persisted by the API - CreateDecisionRequest has no field for it.
          Capturing it here is a known gap to raise with the backend team.
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
  readonly confirmLabel = signal('Confirm');
  readonly danger = signal(false);
  readonly note = signal('');

  readonly confirm = output<string>();
  readonly cancelled = output<void>();

  readonly dialog = viewChild.required<ElementRef<HTMLDialogElement>>('dialogEl');

  open(options: { title: string; confirmLabel: string; danger?: boolean }): void {
    this.title.set(options.title);
    this.confirmLabel.set(options.confirmLabel);
    this.danger.set(options.danger ?? false);
    this.note.set('');
    this.dialog().nativeElement.returnValue = '';
    this.dialog().nativeElement.showModal();
  }

  private onNativeClose(): void {
    if (this.dialog().nativeElement.returnValue !== 'submit') {
      this.cancelled.emit();
    }
  }
}
