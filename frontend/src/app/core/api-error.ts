import { HttpErrorResponse } from '@angular/common/http';

// The backend's ApiExceptionMiddleware always returns a ProblemDetails body with a `title`
// that's safe to show verbatim (it never carries a stack trace or PII) and a `correlationId`
// worth surfacing so a user can hand it to whoever's watching logs.
export interface ApiProblem {
  message: string;
  correlationId: string | null;
  status: number;
}

export function toApiProblem(error: unknown): ApiProblem {
  if (error instanceof HttpErrorResponse) {
    const body = error.error as { title?: string; correlationId?: string } | null;
    return {
      message: body?.title ?? error.message ?? 'The request failed.',
      correlationId: body?.correlationId ?? null,
      status: error.status,
    };
  }

  return { message: 'An unexpected error occurred.', correlationId: null, status: 0 };
}
