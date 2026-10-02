import { Component, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { DatePipe } from '@angular/common';

import { CaseApiService } from '../../core/case-api.service';
import { toApiProblem, ApiProblem } from '../../core/api-error';
import { CaseResponse } from '@caseauth/angular-client/src/models/case-response';
import { CaseStatus } from '@caseauth/angular-client/src/models/case-status';
import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { CaseStatusBadgeComponent } from '../case-detail/components/case-status-badge.component';

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

@Component({
  selector: 'app-case-queue',
  imports: [RouterLink, DatePipe, ErrorBannerComponent, CaseStatusBadgeComponent],
  templateUrl: './case-queue.component.html',
  styleUrl: './case-queue.component.scss',
})
export class CaseQueueComponent {
  private readonly caseApi = inject(CaseApiService);
  private readonly router = inject(Router);

  readonly statuses = ALL_STATUSES;
  readonly statusFilter = signal<CaseStatus | 'All'>('All');

  readonly loading = signal(true);
  readonly error = signal<ApiProblem | null>(null);
  private readonly cases = signal<CaseResponse[]>([]);

  readonly newApplicantName = signal('');
  readonly creating = signal(false);

  readonly visibleCases = computed(() => {
    const filter = this.statusFilter();
    const cases = this.cases();
    // No case-level risk score exists on the backend yet (Finding.Score is per-finding, not
    // aggregated onto Case) - newest-first is the closest honest default until that lands.
    const sorted = [...cases].sort((a, b) => (b.createdAt ?? '').localeCompare(a.createdAt ?? ''));
    return filter === 'All' ? sorted : sorted.filter((c) => c.status === filter);
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
