import { Component, inject, input, output, signal, viewChild } from '@angular/core';

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
  requiresReason?: boolean;
}

// Mirrors CaseStateMachine.Transitions (backend) one-for-one so the buttons shown here are
// exactly the actions the backend will accept from this status - the backend still enforces
// this for real; this is only about not showing a button that would just 409/403.
const LIFECYCLE_ACTIONS: Partial<Record<CaseStatus, LifecycleAction[]>> = {
  Uploaded: [{ action: 'extract', label: 'Extract' }],
  Extracted: [
    { action: 'screen', label: 'Screen' },
    { action: 'request-documents', label: 'Request documents', requiresReason: true },
  ],
  Screened: [
    { action: 'mark-ai-reviewed', label: 'Mark AI reviewed' },
    { action: 'request-documents', label: 'Request documents', requiresReason: true },
  ],
  AiReviewed: [
    { action: 'request-decision', label: 'Request decision' },
    { action: 'request-documents', label: 'Request documents', requiresReason: true },
  ],
  AwaitingDecision: [{ action: 'request-documents', label: 'Request documents', requiresReason: true }],
};

type PendingAction =
  | { kind: 'lifecycle'; action: LifecycleAction }
  | { kind: 'decision'; outcome: DecisionOutcome };

@Component({
  selector: 'app-decision-actions',
  imports: [ReasonDialogComponent, ErrorBannerComponent],
  template: `
    <h2>Actions</h2>

    @if (error()) {
      <app-error-banner [problem]="error()!" (retry)="error.set(null)" />
    }

    <div class="actions">
      @for (lifecycle of lifecycleActions(); track lifecycle.action) {
        <button
          type="button"
          [disabled]="!isAnalyst() || busy()"
          [title]="!isAnalyst() ? 'Requires the Analyst role' : ''"
          (click)="triggerLifecycle(lifecycle)"
        >
          {{ lifecycle.label }}
        </button>
      }

      @if (case().status === 'AwaitingDecision') {
        <button type="button" class="actions__approve" [disabled]="!isSupervisor() || busy()" (click)="triggerDecision('Approved')">
          Approve
        </button>
        <button type="button" class="actions__reject" [disabled]="!isSupervisor() || busy()" (click)="triggerDecision('Rejected')">
          Reject
        </button>
        <button type="button" class="actions__escalate" [disabled]="!isSupervisor() || busy()" (click)="triggerDecision('Escalated')">
          Escalate
        </button>
        @if (!isSupervisor()) {
          <span class="actions__hint">Approve/Reject/Escalate require the Supervisor role.</span>
        }
      }

      @if (lifecycleActions().length === 0 && case().status !== 'AwaitingDecision') {
        <span class="actions__hint">No further action is available while the case is {{ case().status }}.</span>
      }
    </div>

    <app-reason-dialog
      #dialog
      (confirm)="onDialogConfirm($event)"
    />
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

  private readonly dialogRef = viewChild.required<ReasonDialogComponent>('dialog');
  private pending: PendingAction | null = null;

  readonly isAnalyst = () => this.devUser.current().role === 'Analyst';
  readonly isSupervisor = () => this.devUser.current().role === 'Supervisor';

  lifecycleActions(): LifecycleAction[] {
    return LIFECYCLE_ACTIONS[this.case().status!] ?? [];
  }

  triggerLifecycle(action: LifecycleAction): void {
    if (action.requiresReason) {
      this.pending = { kind: 'lifecycle', action };
      this.dialogRef().open({ title: action.label, confirmLabel: action.label });
      return;
    }
    this.runLifecycle(action.action);
  }

  triggerDecision(outcome: DecisionOutcome): void {
    this.pending = { kind: 'decision', outcome };
    this.dialogRef().open({ title: `${outcome.replace('ed', '')} this case`, confirmLabel: outcome, danger: outcome !== 'Approved' });
  }

  onDialogConfirm(_note: string): void {
    const pending = this.pending;
    this.pending = null;
    if (!pending) {
      return;
    }
    if (pending.kind === 'lifecycle') {
      this.runLifecycle(pending.action.action);
    } else {
      this.runDecision(pending.outcome);
    }
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

  private runDecision(outcome: DecisionOutcome): void {
    this.busy.set(true);
    this.error.set(null);

    const idempotencyKey = crypto.randomUUID();
    this.caseApi
      .createDecision(this.case().id!, idempotencyKey, this.case().rowVersion!, {
        outcome,
        aiReviewId: this.latestAiReviewId(),
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.changed.emit();
        },
        error: (err) => {
          this.busy.set(false);
          const problem = toApiProblem(err);
          this.error.set(
            problem.status === 409
              ? { ...problem, message: `${problem.message} Reloading the case now - please retry.` }
              : problem,
          );
          if (problem.status === 409) {
            this.changed.emit();
          }
        },
      });
  }
}
