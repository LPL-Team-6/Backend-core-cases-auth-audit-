import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { CaseAuthApi } from '@caseauth/angular-client/src/case-auth-api';

import { casesList$Json } from '@caseauth/angular-client/src/fn/cases/cases-list-json';
import { casesGet$Json } from '@caseauth/angular-client/src/fn/cases/cases-get-json';
import { casesCreate$Json } from '@caseauth/angular-client/src/fn/cases/cases-create-json';
import { casesMarkExtracted$Json } from '@caseauth/angular-client/src/fn/cases/cases-mark-extracted-json';
import { casesMarkScreened$Json } from '@caseauth/angular-client/src/fn/cases/cases-mark-screened-json';
import { casesMarkAiReviewed$Json } from '@caseauth/angular-client/src/fn/cases/cases-mark-ai-reviewed-json';
import { casesRequestDecision$Json } from '@caseauth/angular-client/src/fn/cases/cases-request-decision-json';
import { casesRequestDocuments$Json } from '@caseauth/angular-client/src/fn/cases/cases-request-documents-json';

import { documentsList$Json } from '@caseauth/angular-client/src/fn/documents/documents-list-json';
import { documentsUpload$Json } from '@caseauth/angular-client/src/fn/documents/documents-upload-json';

import { extractedFieldsList$Json } from '@caseauth/angular-client/src/fn/extracted-fields/extracted-fields-list-json';

import { findingsList$Json } from '@caseauth/angular-client/src/fn/findings/findings-list-json';
import { aiReviewsList$Json } from '@caseauth/angular-client/src/fn/ai-reviews/ai-reviews-list-json';
import { aiReviewInputGet$Json } from '@caseauth/angular-client/src/fn/ai-review-input/ai-review-input-get-json';
import { decisionsList$Json } from '@caseauth/angular-client/src/fn/decisions/decisions-list-json';
import { decisionsCreate$Json } from '@caseauth/angular-client/src/fn/decisions/decisions-create-json';
import { auditEventsList$Json } from '@caseauth/angular-client/src/fn/audit-events/audit-events-list-json';
import { meGet$Json } from '@caseauth/angular-client/src/fn/me/me-get-json';

import { CaseResponse } from '@caseauth/angular-client/src/models/case-response';
import { CreateCaseRequest } from '@caseauth/angular-client/src/models/create-case-request';
import { DocumentResponse } from '@caseauth/angular-client/src/models/document-response';
import { DocumentType } from '@caseauth/angular-client/src/models/document-type';
import { ExtractedFieldResponse } from '@caseauth/angular-client/src/models/extracted-field-response';
import { FindingResponse } from '@caseauth/angular-client/src/models/finding-response';
import { AiReviewResponse } from '@caseauth/angular-client/src/models/ai-review-response';
import { AiReviewInputResponse } from '@caseauth/angular-client/src/models/ai-review-input-response';
import { DecisionResponse } from '@caseauth/angular-client/src/models/decision-response';
import { CreateDecisionRequest } from '@caseauth/angular-client/src/models/create-decision-request';
import { AuditEventResponse } from '@caseauth/angular-client/src/models/audit-event-response';
import { MeResponse } from '@caseauth/angular-client/src/models/me-response';

// Thin, typed façade over the generated CaseAuthApi so feature components call
// `caseApi.listCases()` etc. instead of importing a `casesList$Json`-style fn directly. Keeps
// the generated-client wiring (which fn, which $Json variant) in one place.
@Injectable({ providedIn: 'root' })
export class CaseApiService {
  constructor(private readonly api: CaseAuthApi) {}

  me(): Observable<MeResponse> {
    return this.api.invoke(meGet$Json, {});
  }

  listCases(): Observable<CaseResponse[]> {
    return this.api.invoke(casesList$Json, {});
  }

  getCase(caseId: string): Observable<CaseResponse> {
    return this.api.invoke(casesGet$Json, { id: caseId });
  }

  createCase(body: CreateCaseRequest): Observable<CaseResponse> {
    return this.api.invoke(casesCreate$Json, { body });
  }

  markExtracted(caseId: string): Observable<CaseResponse> {
    return this.api.invoke(casesMarkExtracted$Json, { id: caseId });
  }

  markScreened(caseId: string): Observable<CaseResponse> {
    return this.api.invoke(casesMarkScreened$Json, { id: caseId });
  }

  markAiReviewed(caseId: string): Observable<CaseResponse> {
    return this.api.invoke(casesMarkAiReviewed$Json, { id: caseId });
  }

  requestDecision(caseId: string): Observable<CaseResponse> {
    return this.api.invoke(casesRequestDecision$Json, { id: caseId });
  }

  requestDocuments(caseId: string): Observable<CaseResponse> {
    return this.api.invoke(casesRequestDocuments$Json, { id: caseId });
  }

  listDocuments(caseId: string): Observable<DocumentResponse[]> {
    return this.api.invoke(documentsList$Json, { caseId });
  }

  uploadDocument(caseId: string, documentType: DocumentType, file: Blob): Observable<DocumentResponse> {
    return this.api.invoke(documentsUpload$Json, { caseId, body: { documentType, file } });
  }

  listExtractedFields(documentId: string): Observable<ExtractedFieldResponse[]> {
    return this.api.invoke(extractedFieldsList$Json, { documentId });
  }

  listFindings(caseId: string): Observable<FindingResponse[]> {
    return this.api.invoke(findingsList$Json, { caseId });
  }

  listAiReviews(caseId: string): Observable<AiReviewResponse[]> {
    return this.api.invoke(aiReviewsList$Json, { caseId });
  }

  getAiReviewInput(caseId: string): Observable<AiReviewInputResponse> {
    return this.api.invoke(aiReviewInputGet$Json, { caseId });
  }

  listDecisions(caseId: string): Observable<DecisionResponse[]> {
    return this.api.invoke(decisionsList$Json, { caseId });
  }

  createDecision(
    caseId: string,
    idempotencyKey: string,
    ifMatchRowVersion: string,
    body: CreateDecisionRequest,
  ): Observable<DecisionResponse> {
    return this.api.invoke(decisionsCreate$Json, {
      caseId,
      'Idempotency-Key': idempotencyKey,
      'If-Match': ifMatchRowVersion,
      body,
    });
  }

  listAuditEvents(caseId: string): Observable<AuditEventResponse[]> {
    return this.api.invoke(auditEventsList$Json, { caseId });
  }
}
