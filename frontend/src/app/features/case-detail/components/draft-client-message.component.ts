import { Component, computed, inject, input, signal } from '@angular/core';

import { FindingResponse } from '@caseauth/angular-client/src/models/finding-response';
import { CaseResponse } from '@caseauth/angular-client/src/models/case-response';
import { findingToTodo } from '../../../core/finding-todo';
import { DevUserService } from '../../../core/dev-user.service';

// R10: one click drafts an editable message from the Blocking to-dos. Draft only - a Copy
// button, never a Send button, so nothing goes to a client without a human reading it first.
@Component({
  selector: 'app-draft-client-message',
  template: `
    <h2>Draft client message</h2>
    @if (blockingSentences().length === 0) {
      <p>No blocking issues to ask the client about.</p>
    } @else {
      @if (!draft()) {
        <button type="button" (click)="generate()">Draft a message to the client</button>
      } @else {
        <label for="draft-message-text">Editable draft - review before sending anywhere</label>
        <textarea id="draft-message-text" rows="8" [value]="draft()" (input)="draft.set($any($event.target).value)"></textarea>
        <div class="draft-actions">
          <button type="button" (click)="copy()">{{ copied() ? 'Copied!' : 'Copy' }}</button>
          <button type="button" (click)="regenerate()">Regenerate from current findings</button>
        </div>
      }
    }
  `,
  styles: `
    textarea {
      width: 100%;
      border: 1px solid var(--color-border);
      border-radius: var(--radius);
      padding: 0.6rem;
      background: var(--color-bg);
      color: var(--color-text);
      font-family: inherit;
      resize: vertical;
      margin-top: 0.4rem;
    }

    label {
      font-size: 0.85rem;
      color: var(--color-text-muted);
    }

    .draft-actions {
      display: flex;
      gap: 0.5rem;
      margin-top: 0.5rem;
    }

    button {
      border-radius: var(--radius);
      padding: 0.4rem 0.9rem;
      border: 1px solid var(--color-border);
      background: var(--color-surface);
      color: var(--color-text);
      font-weight: 600;
    }
  `,
})
export class DraftClientMessageComponent {
  private readonly devUser = inject(DevUserService);

  readonly case = input.required<CaseResponse>();
  readonly findings = input.required<FindingResponse[]>();

  readonly draft = signal<string | null>(null);
  readonly copied = signal(false);

  readonly blockingSentences = computed(() =>
    this.findings()
      .map(findingToTodo)
      .filter((t) => t.tag === 'Blocking')
      .map((t) => t.sentence),
  );

  generate(): void {
    const name = this.case().applicantFullName || 'there';
    const advisor = this.devUser.current().displayName;
    const items = this.blockingSentences().map((s) => `- ${s}`).join('\n');
    this.draft.set(
      `Hi ${name},\n\nTo finish opening your account, we need a couple of things:\n\n${items}\n\n` +
        `Please reply with the requested document(s) and we'll continue right away.\n\nThanks,\n${advisor}`,
    );
    this.copied.set(false);
  }

  regenerate(): void {
    this.generate();
  }

  async copy(): Promise<void> {
    const text = this.draft();
    if (!text) return;
    try {
      await navigator.clipboard.writeText(text);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 2000);
    } catch {
      // Clipboard API can be blocked (permissions, insecure context) - the text is still
      // selectable in the textarea, so this isn't a dead end, just a quieter one.
    }
  }
}
