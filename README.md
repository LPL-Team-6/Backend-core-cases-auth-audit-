# Backend-core-cases-auth-audit-
a working skeleton (upload → stored case → visible in the UI) by mid-build, plus the authorization and audit guarantees
This person owns the hub and the API contract that everyone else codes against.

- Scaffold ASP.NET Core with EF Core and Postgres. SQLite is fine for the first hours. Create these entities: `Case`, `Applicant`, `Document`, `ExtractedField`, `Finding`, `AiReview`, `Decision`, `AuditEvent`.
- Publish the OpenAPI spec in the first two hours, and generate the Angular client from it.
- **Upload endpoint:** stream files to S3, or to local disk in fixture mode. Reject wrong file types and oversized files with clear error messages.
- **State machine:** enforce transitions on the server. Each transition and its audit event are written in a single transaction.
- **Auth:** use a development-only auth handler with seeded users (`analyst1`, `analyst2`, `supervisor`) and two demo firms. Every query is scoped to the caller's firm, taken from their identity and never from a request parameter. Document that this handler is for development only.
- **Decisions:** use an idempotency key and a concurrency token. A duplicate or stale approval is rejected, and no second audit event is written.
- **Audit events:** record actor, action, case, timestamp, outcome, correlation ID, and the AI review version that was approved. Keep PII out of logs.

## Status

Backend scaffold is up: entities, EF Core (SQLite by default, Postgres via config), the
dev-only auth handler, the case state machine, file upload, AI-review recording, and the
idempotent/concurrency-safe decision flow are implemented and covered by integration tests.
The OpenAPI spec (`openapi/openapi.json`) and the generated Angular client
(`clients/angular/`) are also in place - see `clients/angular/README.md` for how to consume it
and `scripts/generate-client.sh` to regenerate both after a contract change.

The case state machine matches the project's full architecture doc:
`Uploaded → Extracted → Screened → AiReviewed → AwaitingDecision → Approved / Rejected / Escalated`,
with a `request-documents` action that sends a case back to `Uploaded` from any point before a
decision. `Finding` also links back to the `ExtractedField` row(s) it was computed from (a
many-to-many, since a cross-document mismatch cites a field from each document) - needed for
Teammate 3's screening engine and acceptance criterion #1.

`Finding` also has a `Score` (nullable double, Teammate 3's weighted-sum rule score) and uses
`FindingSeverity: Low/Medium/High` - matching the vocabulary Teammates 3 and 4 are building
against, not the `Info/Warning/Critical` this started as.

`GET /api/cases/{caseId}/ai-review-input` assembles extracted fields (across every document on
the case, labeled with their document type) and findings (with severity/score/source-field-ids)
in one call, so Teammate 4's AI reviewer doesn't need to call `/documents`, then
`/extracted-fields` per document, then `/findings`, and stitch the result together itself. Per
the architecture doc, this is deliberately *all* the reviewer gets - no raw document text.

A background pipeline scaffold also exists (`Pipeline/`, `Entities/ProcessingJob.cs`,
`Controllers/PipelineJobsController.cs`): `POST /api/cases/{caseId}/pipeline-jobs` with
`{"jobType": "Extract"|"Screen"|"AiReview"}` enqueues a row in the `ProcessingJobs` queue table,
and `PipelineBackgroundService` (a `BackgroundService` polling every `Pipeline:PollIntervalSeconds`,
default 2s) picks it up, runs it through `PipelineJobProcessor`, and performs the matching
`CaseStateMachine` transition + audit event atomically - the same guarantee the manual HTTP
endpoints give, just asynchronous. `GET /api/cases/{caseId}/pipeline-jobs` polls a job's status
(`Pending → Processing → Completed`/`Failed`, with `error` set on failure).

This is **additive, not a replacement**: `CasesController`'s existing `/extract`, `/screen`,
`/mark-ai-reviewed` endpoints are untouched and still transition synchronously - useful for
manual testing/demo override. The queue path exists so Teammates 1/3/4 have somewhere to plug
in real work. Three provisional interfaces in `Pipeline/` stand in for their modules, exactly
the `IFileStorageService`-style swap-via-DI pattern already used for storage:

- `IDocumentExtractor` (Teammate 1: `TextractExtractor`/`FixtureExtractor`) - registered stub
  (`FixtureDocumentExtractor`) returns no fields.
- `IScreeningService` (Teammate 3) - now wired to the real **rules engine**
  (`Screening/ScreeningEngine.cs`) via `RuleEngineScreeningService`
  (`Pipeline/RuleEngineScreeningService.cs`), not a stub. See "Connecting the screening engine"
  below.
- `IAiReviewer` (Teammate 4) - now wired to the real **AI Review Agent** service
  (`../AI-Review-Agent`) via `RemoteAiReviewer` (`Pipeline/RemoteAiReviewer.cs`), not a stub.
  `AiReviewAgent:Mode` in `appsettings.json` (default `Remote`) can be set to `Deterministic` to
  fall back to the old escalate-only `DeterministicAiReviewer` - a labeled demo provider for
  running without that service up. See "Connecting the AI Review Agent" below.

**These interface signatures are guesses** at what each teammate's real implementation needs -
expect them to change once Teammates 1/3/4 are actually building against them. Verified by
running the real API (not just the test suite) and watching an enqueued job get picked up and
complete within one poll interval, including the failure path (enqueuing a job illegal for the
case's current status ends up `Failed` with the state machine's error message, case status
unchanged - no crash, no corrupted state).

Not done yet: a real S3 storage backend (`Storage:Mode=S3` intentionally throws
`NotImplementedException` for now), and an actual Angular frontend app consuming the client.
`AiReview`'s shape (`modelName`/`modelVersion`/`recommendation`/`rationale`) is also simpler
than Teammate 4's planned output (`summary`, `key_concerns[]` citing finding IDs,
`recommended_next_steps[]`, `draft_case_note`, derived confidence) - expect that entity/contract
to grow when the AI review module is built.

The `Dockerfile` builds and runs correctly, but **the container won't start at all without
`ASPNETCORE_ENVIRONMENT=Development` set explicitly** - it defaults to `Production`, and
`Program.cs` refuses to register the dev-only auth handler there (on purpose, since there's no
real auth yet). Whoever wires up the Kubernetes manifests needs that env var in the
Deployment/ConfigMap for now, until real authentication exists.

## Connecting the screening engine

`Screen` jobs now run the real rules engine (`Screening/ScreeningEngine.cs`, ported from the
[Team3-Screening-and-rules-engine](../Team3-Screening-and-rules-engine) repo's `nam-branch` -
that repo's `main` only has the generated Angular client, the actual engine was never merged
there) instead of returning no findings. Unlike `RemoteAiReviewer`, this isn't a network seam:
Teammate 3 built the engine directly against this repo's own entities, so it runs in-process,
in the same transaction as the rest of the pipeline step.

`RuleEngineScreeningService` builds a `ScreeningInput` from the case's documents and extracted
fields and calls `ScreeningEngine.Evaluate`, which checks name/DOB/address/TIN consistency
across documents, document expiry and plausibility, PO-box/high-risk-jurisdiction/registered-
agent address patterns, and a sanctions-list match (`SanctionsSnapshot` - a synthetic fixture by
default; `Screening:SanctionsMode=OfacXml` plus `Screening:OfacXmlPath`/`OfacSnapshotDate` can
point it at a real legacy SDN XML export). Findings are scored (`Finding.Score`, 0-1 risk) and
persisted with `Source=Deterministic`.

Two things the original engine supports that this backend doesn't model yet, so they're
stubbed in `RuleEngineScreeningService`:
- Business ("Entity") applicants and their extra document/field requirements - `ApplicantKind`
  is always `Individual`, since `Applicant` has no `Kind` column.
- Cross-case shared-contact detection (same address/phone reused on an unrelated case) -
  `SharedContacts` is always empty, since there's no case-relationship concept to scope it by.

`DocumentType` gained four members the engine's required-document rules are keyed on
(`Application`, `W9`, `BeneficialOwnership`, `FormationDocument`) - additive only, no migration
needed since enums are stored as ints. Verified by running the real API: enqueueing `Extract`
then `Screen` for a case with no uploaded documents produces a `High`-severity
`MISSING_REQUIRED_DOCUMENT` finding and moves the case to `Screened`, exactly as the engine's
own ported unit tests (`tests/CaseAuth.Api.Tests/ScreeningEngineTests.cs`) expect.

## Connecting the AI Review Agent

`AiReview` jobs now call the real [AI Review Agent](../AI-Review-Agent) service over HTTP
instead of a stub. `RemoteAiReviewer` builds its request from this API's own data (the same
fields+findings `GET /api/cases/{caseId}/ai-review-input` assembles) and maps the response back
into this API's thinner `AiReview` row - `Rationale` carries a flattened version of the agent's
richer output (summary, key concerns, next steps, confidence, fallback status) until `AiReview`
grows real columns for them. The agent never recommends approve/reject, so `Recommendation` is
always `Escalate` for a real review; the analyst still makes the actual call via `/decisions`.

To run both services together:

```bash
# terminal 1 - AI Review Agent (defaults to port 5176)
cd ../AI-Review-Agent
dotnet run --project src/AiReview.Api --launch-profile http

# terminal 2 - this API
cd src/CaseAuth.Api
dotnet run
```

Then enqueue an `AiReview` pipeline job for a case that has reached `Screened`
(`POST /api/cases/{caseId}/pipeline-jobs` with `{"jobType": "AiReview"}`) and poll
`GET /api/cases/{caseId}/pipeline-jobs` until it completes.

If the AI Review Agent isn't running, the job fails with a connection error (visible in the job's
`error` field) rather than silently degrading - set `AiReviewAgent:Mode=Deterministic` (or the
`AiReviewAgent__Mode` env var) to demo without it.

## Running locally

Requires the .NET 8 SDK (`dotnet --version`).

```bash
# run the API (applies EF migrations automatically in Development)
cd src/CaseAuth.Api
dotnet run
# -> http://localhost:5020 (launchSettings.json's "http" profile), Swagger UI at /swagger

# run the tests
cd ../..
dotnet test
```

All endpoints require a seeded dev identity via the `X-Dev-User` header:
`analyst1` (FIRM-A, Analyst), `analyst2` (FIRM-B, Analyst), `supervisor` (FIRM-A, Supervisor).
This header-based handler only runs in the Development environment - see
`src/CaseAuth.Api/Auth/DevAuthenticationHandler.cs`.

Example flow (see `src/CaseAuth.Api/CaseAuth.Api.http` or Swagger for the full set):

```bash
curl -X POST localhost:5020/api/cases -H "X-Dev-User: analyst1" -H "Content-Type: application/json" \
  -d '{"applicantFullName":"Jane Doe"}'
# extract -> screen -> ai-reviews -> mark-ai-reviewed -> request-decision, then as supervisor:
curl -X POST localhost:5020/api/cases/{id}/decisions -H "X-Dev-User: supervisor" \
  -H "Idempotency-Key: <uuid>" -H "If-Match: <case RowVersion>" \
  -H "Content-Type: application/json" -d '{"outcome":"Approved"}'
```

To switch to Postgres, set `Database:Provider=Postgres` and
`ConnectionStrings:Postgres` (see `appsettings.json`).

## Running in Docker

```bash
docker build -t caseauth-api .
docker run -p 8080:8080 \
  -e ASPNETCORE_URLS=http://+:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  caseauth-api
# -> http://localhost:8080, Swagger UI at /swagger
```

`ASPNETCORE_ENVIRONMENT=Development` is required (see the note above) - without it the
container crashes on startup with `No production authentication handler is configured`. The
container uses an in-container SQLite file, so data doesn't persist across restarts; nothing
else is required to get a working API inside it (migrations apply automatically).
