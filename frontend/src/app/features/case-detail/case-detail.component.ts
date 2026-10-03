import { Component, computed, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { forkJoin } from 'rxjs';

import { CaseApiService } from '../../core/case-api.service';
import { toApiProblem, ApiProblem } from '../../core/api-error';
import { DevUserService } from '../../core/dev-user.service';
import { isAdvisor, isReviewer } from '../../core/role-view';
import { findingToTodo } from '../../core/finding-todo';

import { CaseResponse } from '@caseauth/angular-client/src/models/case-response';
import { DocumentResponse } from '@caseauth/angular-client/src/models/document-response';
import { FindingResponse } from '@caseauth/angular-client/src/models/finding-response';
import { AiReviewResponse } from '@caseauth/angular-client/src/models/ai-review-response';
import { DecisionResponse } from '@caseauth/angular-client/src/models/decision-response';
import { AuditEventResponse } from '@caseauth/angular-client/src/models/audit-event-response';
import { AiReviewInputFieldResponse } from '@caseauth/angular-client/src/models/ai-review-input-field-response';

import { ErrorBannerComponent } from '../../shared/error-banner.component';
import { CaseSummaryBarComponent } from './components/case-summary-bar.component';
import { DocumentFieldsPanelComponent } from './components/document-fields-panel.component';
import { FindingComparisonComponent } from './components/finding-comparison.component';
import { FindingsPanelComponent } from './components/findings-panel.component';
import { AiReviewPanelComponent } from './components/ai-review-panel.component';
import { AuditTimelineComponent } from './components/audit-timeline.component';
import { DecisionActionsComponent } from './components/decision-actions.component';
import { DocumentUploadComponent } from './components/document-upload.component';
import { AdvisorTodoListComponent } from './components/advisor-todo-list.component';
import { DraftClientMessageComponent } from './components/draft-client-message.component';

interface CaseDetailData {
  caseResponse: CaseResponse;
  documents: DocumentResponse[];
  fields: AiReviewInputFieldResponse[];
  findings: FindingResponse[];
  aiReviews: AiReviewResponse[];
  decisions: DecisionResponse[];
  auditEvents: AuditEventResponse[];
}

@Component({
  selector: 'app-case-detail',
  imports: [
    DatePipe,
    ErrorBannerComponent,
    CaseSummaryBarComponent,
    DocumentFieldsPanelComponent,
    FindingComparisonComponent,
    FindingsPanelComponent,
    AiReviewPanelComponent,
    AuditTimelineComponent,
    DecisionActionsComponent,
    DocumentUploadComponent,
    AdvisorTodoListComponent,
    DraftClientMessageComponent,
  ],
  templateUrl: './case-detail.component.html',
  styleUrl: './case-detail.component.scss',
})
export class CaseDetailComponent {
  private readonly caseApi = inject(CaseApiService);
  private readonly devUser = inject(DevUserService);

  // Bound automatically from the :caseId route param via withComponentInputBinding().
  readonly caseId = input.required<string>();

  readonly loading = signal(true);
  readonly error = signal<ApiProblem | null>(null);
  readonly selectedFindingId = signal<string | null>(null);
  private readonly data = signal<CaseDetailData | null>(null);

  readonly advisorView = computed(() => isAdvisor(this.devUser.current().role));
  readonly reviewerView = computed(() => isReviewer(this.devUser.current().role));

  readonly caseResponse = computed(() => this.data()?.caseResponse ?? null);
  readonly documents = computed(() => this.data()?.documents ?? []);
  readonly fields = computed(() => this.data()?.fields ?? []);
  readonly findings = computed(() => this.data()?.findings ?? []);
  readonly aiReviews = computed(() => this.data()?.aiReviews ?? []);
  readonly decisions = computed(() => this.data()?.decisions ?? []);
  readonly auditEvents = computed(() => this.data()?.auditEvents ?? []);

  // R7's "count of blocking issues" - derived from the same rule-ID lookup R2's to-do list
  // uses, so the summary bar's number always matches what the advisor sees below it.
  readonly blockingCount = computed(() => this.findings().filter((f) => findingToTodo(f).tag === 'Blocking').length);

  readonly latestAiReviewId = computed(() => {
    const reviews = this.aiReviews();
    if (reviews.length === 0) {
      return null;
    }
    return reviews.reduce((a, b) => ((b.version ?? 0) > (a.version ?? 0) ? b : a)).id ?? null;
  });

  // "<field name>: <value>" per extracted field id - lets the findings panel cite source
  // fields by name instead of a raw guid.
  readonly fieldLabelsById = computed(() => {
    const map = new Map<string, string>();
    for (const field of this.fields()) {
      if (field.id) {
        map.set(field.id, `${field.fieldName}: ${field.fieldValue}`);
      }
    }
    return map;
  });

  // Inverse: extracted field id -> the findings (code + rule match score) that cite it, so the
  // document/fields panel can badge which fields ANY finding cites, and R12's "Why?" link can
  // show the rule match score alongside the field's own extraction confidence.
  readonly citingFindingsByFieldId = computed(() => {
    const map = new Map<string, { code: string; score: number | null }[]>();
    for (const finding of this.findings()) {
      for (const fieldId of finding.sourceFieldIds ?? []) {
        const citers = map.get(fieldId) ?? [];
        citers.push({ code: finding.code ?? finding.id ?? 'finding', score: finding.score ?? null });
        map.set(fieldId, citers);
      }
    }
    return map;
  });

  // R1: the SELECTED finding's source fields - the brighter, click-triggered highlight.
  private readonly selectedFinding = computed(() => this.findings().find((f) => f.id === this.selectedFindingId()) ?? null);

  readonly selectedFieldIds = computed<ReadonlySet<string>>(() => new Set(this.selectedFinding()?.sourceFieldIds ?? []));

  readonly selectedComparisonFields = computed(() => {
    const ids = this.selectedFieldIds();
    return ids.size === 0 ? [] : this.fields().filter((f) => f.id && ids.has(f.id));
  });

  constructor() {
    this.load();
  }

  load(): void {
    const caseId = this.caseId();
    this.loading.set(true);
    this.error.set(null);

    forkJoin({
      caseResponse: this.caseApi.getCase(caseId),
      documents: this.caseApi.listDocuments(caseId),
      aiReviewInput: this.caseApi.getAiReviewInput(caseId),
      findings: this.caseApi.listFindings(caseId),
      aiReviews: this.caseApi.listAiReviews(caseId),
      decisions: this.caseApi.listDecisions(caseId),
      auditEvents: this.caseApi.listAuditEvents(caseId),
    }).subscribe({
      next: ({ caseResponse, documents, aiReviewInput, findings, aiReviews, decisions, auditEvents }) => {
        this.data.set({
          caseResponse,
          documents,
          fields: aiReviewInput.fields ?? [],
          findings,
          aiReviews,
          decisions,
          auditEvents,
        });
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(toApiProblem(err));
        this.loading.set(false);
      },
    });
  }
}
