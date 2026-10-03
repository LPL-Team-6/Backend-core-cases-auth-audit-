# Missing endpoints / fields

Per the UX brief: "If an endpoint you need is missing, stub it behind a service interface and
list it here" instead of hand-writing calls against routes the OpenAPI spec doesn't have. This
is that list - what the UX brief assumes exists that the backend (as of this branch) doesn't
yet provide, what's stubbed in the meantime, and where.

## Reset demo (supervisor-only)

**Brief wants:** a "Reset demo" admin action, supervisor-only, that calls a backend reset
endpoint and reloads the queue.

**Backend has:** no reset/seed endpoint at all.

**Stubbed as:** nothing is wired into the UI - there's no button to show that would just 404.
Demo data is seeded instead by `scripts/seed-smyth-case.sh`, which uses only real, in-spec
endpoints (create case, upload documents, record extracted fields/findings, advance status) to
reproduce the Smyth scenario on demand. If a real reset endpoint is added later
(`POST /api/admin/reset-demo` or similar, supervisor-only), wire it into
`CaseApiService` and add the button to the queue page.

## TIN masking / reveal (R13)

**Brief wants:** TIN shown as last-four by default, with a "Reveal" control gated by a
server-audited endpoint.

**Backend has:** no TIN field anywhere. `Applicant` has `FullName`, `DateOfBirth`, `Email`,
`Phone` - nothing resembling a tax ID (see `src/CaseAuth.Api/Entities/Applicant.cs`).

**Not built.** The brief itself says not to build the control if there's no reveal endpoint;
there's not even a field to mask, so nothing exists here at all.

## Autosave case note / `draft_case_note` (R15)

**Brief wants:** autosaving the analyst-editable `draft_case_note` field on the AI review.

**Backend has:** no such field. `AiReview` is `modelName`/`modelVersion`/`recommendation`/
`rationale`/`version`/`createdAt` only - the root README already flags this as simpler than
Teammate 4's planned output (`summary`, `key_concerns[]`, `recommended_next_steps[]`,
`draft_case_note`, derived confidence).

**Not built.** There's nothing to autosave *to* - a client-only draft with no server
counterpart would silently vanish on a different device/browser, which is worse than not
offering it.

## Case-level risk tier / account type (R7, R11)

**Brief wants:** the case summary bar to show account type and risk tier; the queue to filter
by risk tier.

**Backend has:** neither field, on `Case` or `Applicant`.

**Not built.** The summary bar shows what's real (client name, blocking-issue count, days
open); the queue's filters are status + search only.

## Case-level aggregate risk score for queue sorting (R11)

**Brief wants:** queue default sort by "needs your action, then by risk."

**Backend has:** `Finding.Score` is per-finding, not aggregated onto `Case` - there's no single
risk number per case to sort by.

**Stubbed as:** the queue sorts by "needs your action" (derived from status + role, see
`core/plain-status.ts`'s `WhoseTurn`) and falls back to newest-first. If a case-level risk
aggregate is added (e.g. `Case.MaxFindingScore` computed on write), wire it into
`CaseQueueComponent.visibleCases`'s sort comparator as the tiebreaker it's missing today.

## Document content (not literally in the brief, but blocks R1's "document image")

**Backend has:** `DocumentsController` only exposes `List`/`Upload` - `Document.StorageKey` is
written on upload but never read back by any `GET` route.

**Stubbed as:** the document/fields panel shows the document's metadata (filename, type, size,
upload time) in place of an image. If a `GET /api/documents/{id}/content` endpoint is added,
swap that metadata card for an actual `<img>`/`<iframe>`.

## Request-documents role (flagged, not a missing endpoint)

The brief groups "Request documents" under the **reviewer's** decision actions (Approve /
Reject / Escalate / Request documents - R5). The backend's `CaseStateMachine.Transitions`
requires the **Analyst** role for every `request-documents` transition - a Supervisor calling it
gets a real 403, not a demo limitation. Since "hide actions the role cannot take" is itself a
brief requirement, `request-documents` stays an advisor/Analyst action here (it already made
sense there - the advisor needs to re-upload after it). Worth a decision from whoever owns
`CaseStateMachine`: either allow Supervisor there too, or confirm advisor-only was the actual
intent and the brief's grouping was just describing the four possible outcomes of a review, not
who clicks each button.

## Upload limits (not missing, but duplicated by hand)

No endpoint exposes `Storage:MaxUploadBytes` / `Storage:AllowedContentTypes`
(`appsettings.json`). `DocumentUploadComponent` mirrors both constants by hand for a specific
client-side error message before the network round-trip - they will silently drift if
`appsettings.json` changes without a matching frontend edit.
