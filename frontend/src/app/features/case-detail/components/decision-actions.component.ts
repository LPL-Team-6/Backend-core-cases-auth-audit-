import { Component, inject, input, output, signal, viewChild } from '@angular/core';
import { DatePipe } from '@angular/common';

import { CaseApiService } from '../../../core/case-api.service';
import { DevUserService } from '../../../core/dev-user.service';
import { ApiProblem, toApiProblem } from '../../../core/api-error';
import { CaseResponse } from '@caseauth/angular-client/src/models/case-response';
import { CaseStatus } from '@caseauth/angular-client/src/models/case-status';
import { DecisionOutcome } from '@caseauth/angular-client/src/models/decision-outcome';
import { ReasonDialogComponent } from '../../../shared/reason-dialog.component';
import { ErrorBannerComponent } from '../../../shared/error-banner.component';

interface LifecycleAction {
  action: 'extract' | 'screen' | 'mark-ai-reviewed' | 'request-decision' | 'request-documents';
  label: string;
  description: string;
  requiresReason?: boolean;
}

// Mirrors CaseStateMachine.Transitions (backend) one-for-one so the buttons shown here are
// exactly the actions the backend will accept from this status for the Analyst/"advisor" role -
// the backend still enforces this for real; this is only about not showing a dead button.
//
// NOTE: the UX brief groups "Request documents" under the *reviewer's* decision actions
// (Approve/Reject/Escalate/Request documents), but CaseStateMachine.Transitions requires the
// Analyst role for every request-documents transition - a Supervisor calling it gets a real
// 403. Flagged in MISSING_ENDPOINTS.md; until backend decides to allow Supervisor too, this
// stays an advisor/Analyst action, consistent with "hide actions the role cannot take."
const LIFECYCLE_ACTIONS: Partial<Record<CaseStatus, LifecycleAction[]>> = {
  Uploaded: [{ action: 'extract', label: 'Extract', description: 'Starts extracting fields from the uploaded documents.' }],
  Extracted: [
    { action: 'screen', label: 'Screen', description: 'Runs the screening rules against the extracted fields.' },
    {
      action: 'request-documents',
      label: 'Request documents',
      description: 'Sends the case back to the client for a new or corrected document.',
      requiresReason: true,
    },
  ],
  Screened: [
    { action: 'mark-ai-reviewed', label: 'Mark AI reviewed', description: 'Records that an AI review has run for this case.' },
    {
      action: 'request-documents',
      label: 'Request documents',
      description: 'Sends the case back to the client for a new or corrected document.',
      requiresReason: true,
    },
  ],
  AiReviewed: [
    { action: 'request-decision', label: 'Request decision', description: 'Sends the case to a reviewer for a decision.' },
    {
      action: 'request-documents',
      label: 'Request documents',
      description: 'Sends the case back to the client for a new or corrected document.',
      requiresReason: true,
    },
  ],
  AwaitingDecision: [
    {
      action: 'request-documents',
      label: 'Request documents',
      description: 'Sends the case back to the client for a new or corrected document.',
      requiresReason: true,
    },
  ],
};

const DECISION_DESCRIPTIONS: Record<DecisionOutcome, string> = {
  Approved: 'This opens the account. The applicant will be notified.',
  Rejected: 'This closes the case without opening an account.',
  Escalated: 'This sends the case to a senior reviewer for a second look.',
};

type PendingAction =
  | { kind: 'lifecycle'; action: LifecycleAction }
  | { kind: 'decision'; outcome: DecisionOutcome; idempotencyKey: string };

@Component({
  selector: 'app-decision-actions',
  imports: [ReasonDialogComponent, ErrorBannerComponent, DatePipe],
  template: `
    <h2>Actions</h2>

    @if (error()) {
      <app-error-banner [problem]="error()!" (retry)="error.set(null)" />
    }

    @if (staleConflict()) {
      <div class="stale-notice" role="alert">
        <p>This review was updated after you opened it. Please review the new version.</p>
        <button type="button" (click)="reloadLatest()">Reload latest version</button>
      </div>
    }

    @if (lastDecision()) {
      <div class="decision-success" role="status">
        Decision recorded at {{ lastDecision()!.at | date: 'medium' }}.
        <a href="#audit-timeline" (click)="scrollToAuditTimeline($event)">View in audit timeline</a>
      </div>
    }

    <div class="actions">
      @if (isAnalyst()) {
        @for (lifecycle of lifecycleActions(); track lifecycle.action) {
          <button type="button" [disabled]="busy()" (click)="triggerLifecycle(lifecycle)">
            {{ lifecycle.label }}
          </button>
        }
      }

      @if (isSupervisor() && case().status === 'AwaitingDecision') {
        <button type="button" class="actions__approve" [disabled]="busy()" (click)="triggerDecision('Approved')">
          Approve
        </button>
        <button type="button" class="actions__reject" [disabled]="busy()" (click)="triggerDecision('Rejected')">
          Reject
        </button>
        <button type="button" class="actions__escalate" [disabled]="busy()" (click)="triggerDecision('Escalated')">
          Escalate
        </button>
      }

      @if (noActionsVisible()) {
        <span class="actions__hint">No action available for your role while the case is {{ case().status }}.</span>
      }
    </div>

    <app-reason-dialog #dialog (confirm)="onDialogConfirm()" />
  `,
  styles: `
    .actions {
      display: flex;
      align-items: center;
      gap: 0.5rem;
      flex-wrap: wrap;
    }

    button {
      border-radius: var(--radius);
      padding: 0.45rem 0.9rem;
      border: 1px solid var(--color-border);
      background: var(--color-surface);
      color: var(--color-text);
      font-weight: 600;
    }

    .actions__approve {
      border-color: var(--color-success);
      color: var(--color-success);
    }

    .actions__reject {
      border-color: var(--color-danger);
      color: var(--color-danger);
    }

    .actions__escalate {
      border-color: var(--color-warning);
      color: var(--color-warning);
    }

    .actions__hint {
      color: var(--color-text-muted);
      font-size: 0.85rem;
    }

    .stale-notice {
      border: 1px solid var(--color-warning);
      background: var(--color-warning-bg);
      color: var(--color-warning);
      border-radius: var(--radius);
      padding: 0.6rem 0.9rem;
      margin-bottom: 0.75rem;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 1rem;
      flex-wrap: wrap;

      button {
        border-color: var(--color-warning);
        color: var(--color-warning);
        background: transparent;
      }
    }

    .decision-success {
      border: 1px solid var(--color-success);
      background: var(--color-success-bg);
      color: var(--color-success);
      border-radius: var(--radius);
      padding: 0.6rem 0.9rem;
      margin-bottom: 0.75rem;

      a {
        color: inherit;
        text-decoration: underline;
        margin-left: 0.5rem;
      }
    }
  `,
})
export class DecisionActionsComponent {
  private readonly caseApi = inject(CaseApiService);
  private readonly devUser = inject(DevUserService);

  readonly case = input.required<CaseResponse>();
  readonly latestAiReviewId = input<string | null>(null);
  readonly changed = output<void>();

  readonly busy = signal(false);
  readonly error = signal<ApiProblem | null>(null);
  readonly staleConflict = signal(false);
  readonly lastDecision = signal<{ outcome: DecisionOutcome; at: Date } | null>(null);

  private readonly dialogRef = viewChild.required<ReasonDialogComponent>('dialog');
  private pending: PendingAction | null = null;

  readonly isAnalyst = () => this.devUser.current().role === 'Analyst';
  readonly isSupervisor = () => this.devUser.current().role === 'Supervisor';

  lifecycleActions(): LifecycleAction[] {
    return LIFECYCLE_ACTIONS[this.case().status!] ?? [];
  }

  noActionsVisible(): boolean {
    if (this.isAnalyst()) {
      return this.lifecycleActions().length === 0;
    }
    if (this.isSupervisor()) {
      return this.case().status !== 'AwaitingDecision';
    }
    return true;
  }

  triggerLifecycle(action: LifecycleAction): void {
    this.lastDecision.set(null);
    if (action.requiresReason) {
      this.pending = { kind: 'lifecycle', action };
      this.dialogRef().open({ title: action.label, description: action.description, confirmLabel: action.label });
      return;
    }
    this.runLifecycle(action.action);
  }

  triggerDecision(outcome: DecisionOutcome): void {
    this.lastDecision.set(null);
    // Generated once per dialog open (not per API call) so a defensive double-submit reuses the
    // exact same idempotency key rather than racing the server with two different ones.
    this.pending = { kind: 'decision', outcome, idempotencyKey: crypto.randomUUID() };
    this.dialogRef().open({
      title: `${outcome === 'Rejected' ? 'Reject' : outcome === 'Escalated' ? 'Escalate' : 'Approve'} this case`,
      description: DECISION_DESCRIPTIONS[outcome],
      confirmLabel: outcome,
      danger: outcome !== 'Approved',
    });
  }

  onDialogConfirm(): void {
    if (this.busy()) {
      return;
    }
    const pending = this.pending;
    this.pending = null;
    if (!pending) {
      return;
    }
    if (pending.kind === 'lifecycle') {
      this.runLifecycle(pending.action.action);
    } else {
      this.runDecision(pending);
    }
  }

  reloadLatest(): void {
    this.staleConflict.set(false);
    this.changed.emit();
  }

  scrollToAuditTimeline(event: Event): void {
    event.preventDefault();
    document.getElementById('audit-timeline')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  private runLifecycle(action: LifecycleAction['action']): void {
    this.busy.set(true);
    this.error.set(null);

    const call =
      action === 'extract'
        ? this.caseApi.markExtracted(this.case().id!)
        : action === 'screen'
          ? this.caseApi.markScreened(this.case().id!)
          : action === 'mark-ai-reviewed'
            ? this.caseApi.markAiReviewed(this.case().id!)
            : action === 'request-decision'
              ? this.caseApi.requestDecision(this.case().id!)
              : this.caseApi.requestDocuments(this.case().id!);

    call.subscribe({
      next: () => {
        this.busy.set(false);
        this.changed.emit();
      },
      error: (err) => {
        this.busy.set(false);
        this.error.set(toApiProblem(err));
      },
    });
  }

  private runDecision(pending: { outcome: DecisionOutcome; idempotencyKey: string }): void {
    this.busy.set(true);
    this.error.set(null);
    this.staleConflict.set(false);

    this.caseApi
      .createDecision(this.case().id!, pending.idempotencyKey, this.case().rowVersion!, {
        outcome: pending.outcome,
        aiReviewId: this.latestAiReviewId(),
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.lastDecision.set({ outcome: pending.outcome, at: new Date() });
          this.changed.emit();
        },
        error: (err) => {
          this.busy.set(false);
          const problem = toApiProblem(err);
          if (problem.status === 409) {
            this.staleConflict.set(true);
          } else {
            this.error.set(problem);
          }
        },
      });
  }
}
