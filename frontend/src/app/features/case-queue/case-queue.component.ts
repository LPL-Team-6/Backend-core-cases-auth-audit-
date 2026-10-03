import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';

import { CaseApiService } from '../../core/case-api.service';
import { DevUserService } from '../../core/dev-user.service';
import { toApiProblem, ApiProblem } from '../../core/api-error';
import { isAdvisor, isReviewer } from '../../core/role-view';
import { plainStatus } from '../../core/plain-status';
import { CaseResponse } from '@caseauth/angular-client/src/models/case-response';
import { CaseStatus } from '@caseauth/angular-client/src/models/case-status';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { PlainStatusBadgeComponent } from '../case-detail/components/plain-status-badge.component';

const ALL_STATUSES: readonly CaseStatus[] = [
  'Uploaded',
  'Extracted',
  'Screened',
  'AiReviewed',
  'AwaitingDecision',
  'Approved',
  'Rejected',
  'Escalated',
];

// R11: cases open longer than this many days get the aging indicator. A code constant, not a
// runtime setting - "configurable" here means "easy for a developer to change," not a UI
// control nobody asked for.
const AGING_THRESHOLD_DAYS = 3;

interface CaseRow {
  case: CaseResponse;
  daysOpen: number;
  aging: boolean;
  myTurn: boolean;
}

@Component({
  selector: 'app-case-queue',
  imports: [RouterLink, ErrorBannerComponent, PlainStatusBadgeComponent],
  templateUrl: './case-queue.component.html',
  styleUrl: './case-queue.component.scss',
})
export class CaseQueueComponent {
  private readonly caseApi = inject(CaseApiService);
  private readonly router = inject(Router);
  private readonly devUser = inject(DevUserService);

  readonly statuses = ALL_STATUSES;
  readonly statusFilter = signal<CaseStatus | 'All'>('All');
  readonly searchQuery = signal('');

  readonly loading = signal(true);
  readonly error = signal<ApiProblem | null>(null);
  private readonly cases = signal<CaseResponse[]>([]);

  readonly newApplicantName = signal('');
  readonly creating = signal(false);

  readonly advisorView = computed(() => isAdvisor(this.devUser.current().role));

  readonly visibleCases = computed<CaseRow[]>(() => {
    const statusFilter = this.statusFilter();
    const query = this.searchQuery().trim().toLowerCase();
    const advisor = this.advisorView();
    const reviewer = isReviewer(this.devUser.current().role);
    const myUserId = this.devUser.userId();

    let cases = this.cases();

    // R2: "Advisor view: sees own cases" - cases this analyst created. Server already scopes
    // the list to this firm; this narrows it further on the client using the real backend user
    // id from /api/me (DevUserService.userId), not a guessed id format.
    if (advisor && myUserId) {
      cases = cases.filter((c) => c.createdByUserId === myUserId);
    }

    if (statusFilter !== 'All') {
      cases = cases.filter((c) => c.status === statusFilter);
    }

    if (query) {
      cases = cases.filter(
        (c) => c.applicantFullName?.toLowerCase().includes(query) || c.id?.toLowerCase().includes(query),
      );
    }

    const rows: CaseRow[] = cases.map((c) => {
      const daysOpen = c.createdAt ? Math.max(0, Math.floor((Date.now() - new Date(c.createdAt).getTime()) / 86_400_000)) : 0;
      const isTerminal = c.status === 'Approved' || c.status === 'Rejected';
      const turn = c.status ? plainStatus(c.status).turn : 'none';
      const myTurn = (advisor && turn === 'advisor') || (reviewer && turn === 'reviewer');
      return { case: c, daysOpen, aging: !isTerminal && daysOpen > AGING_THRESHOLD_DAYS, myTurn };
    });

    // Default sort (R11): cases needing the current user's action first, then newest first.
    // No case-level risk score exists on the backend yet (Finding.Score is per-finding, not
    // aggregated onto Case), so risk isn't part of this ordering - see MISSING_ENDPOINTS.md.
    return rows.sort((a, b) => {
      if (a.myTurn !== b.myTurn) return a.myTurn ? -1 : 1;
      return (b.case.createdAt ?? '').localeCompare(a.case.createdAt ?? '');
    });
  });

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    this.caseApi.listCases().subscribe({
      next: (cases) => {
        this.cases.set(cases);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(toApiProblem(err));
        this.loading.set(false);
      },
    });
  }

  setStatusFilter(value: string): void {
    this.statusFilter.set(value as CaseStatus | 'All');
  }

  createCase(): void {
    const applicantFullName = this.newApplicantName().trim();
    if (!applicantFullName) {
      return;
    }
    this.creating.set(true);
    this.error.set(null);
    this.caseApi.createCase({ applicantFullName }).subscribe({
      next: (created) => {
        this.creating.set(false);
        this.newApplicantName.set('');
        this.router.navigate(['/cases', created.id]);
      },
      error: (err) => {
        this.creating.set(false);
        this.error.set(toApiProblem(err));
      },
    });
  }
}
