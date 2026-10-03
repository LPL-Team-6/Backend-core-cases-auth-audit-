# Compliance review dashboard

React + Vite front end for the CaseAuth API: a case queue sorted by risk, and a case page
with the document image beside its extracted fields, the rule findings, the AI review, the
Approve / Escalate / Reject decision and the audit trail.

```bash
# 1. API (from the repo root)
cd src/CaseAuth.Api && dotnet run          # http://localhost:5020

# 2. Demo data (new terminal, repo root) - see demo/README.md
python3 demo/make_specimens.py && node demo/seed.mjs

# 3. Dashboard
cd dashboard && npm install && npm run dev # http://localhost:4200
```

`/api` is proxied to `http://localhost:5020`; set `API_TARGET=http://localhost:8080` to use
the Docker container instead.

## Demo notes

- As `supervisor`, **Reset demo** on the queue deletes the firm's cases and reloads the five
  personas, all waiting for a decision. Use it before every run-through.
- Switch user at the top right. `analyst1` sees the queue but can't decide; `supervisor`
  (same firm) can; `analyst2` is in Firm B and sees no Firm A cases at all.
- Click a finding to highlight the fields it cites. Fields under 80% extraction confidence
  are marked "check by eye".
- Decisions send an `Idempotency-Key` (one per page load) and `If-Match` with the case's
  RowVersion, so a double click replays instead of deciding twice and a stale page gets a 409.
- The case note starts from the AI's draft and saves to the API a second after you stop typing.
  It locks once the case is approved or rejected.
- Tax IDs show the last four. "Reveal" shows the full number and adds an entry to the audit trail.

## Stand-ins until the backend catches up

- **Risk score** comes from the API (`riskScore` / `riskTier` on each case): the rules engine's
  weighted score, in points.
- **Structured AI review** (summary, key concerns, next steps) comes from the review's own
  fields. Reviews recorded before those fields existed fall back to JSON in `rationale`, and
  plain-text rationales still render.
- **Document images** need `GET /api/cases/{caseId}/documents/{documentId}/content`, added
  alongside this dashboard. Each successful read writes a `Document.Viewed` audit event, so
  opening a case logs one view per document tab shown.
