# CaseAuth Workbench (frontend)

The Angular app the team actually clicks through, built against the UX requirements doc
(role-based advisor/reviewer views, click-to-source findings, the full decision flow, etc.).
All data is synthetic; all rule codes are illustrative, not LPL policy.

## Status

**P0 (must-have) - all built:**

- **R1** Click-to-source finding highlights, with the actual differing characters highlighted
  (e.g. `Sm[i]th` vs `Sm[y]th`) via a minimal character diff.
- **R2** Advisor to-do list: findings converted to plain-language sentences via a frontend
  rule-ID lookup (`core/finding-todo.ts`) with a generic severity-based fallback, each tagged
  Blocking/FYI.
- **R3** Stale-approval message on a 409: the exact required copy, a reload button, no raw
  error text. Verified by hand that it creates no audit entry (see Verification below).
- **R4** AI fallback banner + "AI draft, review before use" label, keyed off
  `AiReview.modelName === 'deterministic-fallback'` (the backend's real signal, not a guess).
- **R5** Full decision flow: required reason, "what will happen" copy, a stable idempotency
  key generated once per dialog open (survives a defensive double-submit), buttons disabled
  while in flight, and a "Decision recorded at {time}" message linking to the audit timeline.
- **R6** Loading/empty/error/retry on every data-loading component; specific upload errors
  (exact size in MB, exact allowed types) checked client-side before the network round-trip.
- **R7** Case summary bar: client name, blocking-issue count, days open. Account type and risk
  tier are **not shown** - they're not in the API (see `MISSING_ENDPOINTS.md`).

**Role-based views (R2's other half):** Advisor (Analyst) sees own cases, the to-do list,
document upload, and a draft client message - no Approve/Reject. Reviewer (Supervisor) sees the
firm's queue, the full findings/document/AI-review panels, and the decision actions. Both roles
are hidden, not just disabled, when a role can't take an action - the server still enforces this
for real.

**P1 (should-have) - built:** R8 (plain-language status), R11 (queue sorted by "needs your
action" then newest, status filter, search by name/case ID, aging indicator past 3 days), R12
(confidence "Why?" showing extraction confidence + rule match score, never a model-text number),
R14 (FYI findings collapse below Blocking, in both the advisor to-do list and the reviewer's
findings panel).

**P1 - built but worth knowing about:** R9 (document upload doubles as single-document
re-upload) and R10 (draft client message, Copy-only, never auto-sent) exist. R13 (TIN
masking/reveal) and R15 (autosave `draft_case_note`) are **not built** - there's no TIN field and
no `draft_case_note` field in the API at all, not just a missing endpoint (see
`MISSING_ENDPOINTS.md`).

**P2 (nice-to-have) - not implemented**, per the brief's own instruction to list these as next
steps rather than overclaim them: R16 (pre-submission check), R17 (keyboard shortcuts), R18
(saved filters), R19 (friendly empty states - partially covered by R6's empty states, but no
dedicated polish pass beyond that).

**Demo support:** no "Reset demo" admin action - there's no backend reset endpoint (see
`MISSING_ENDPOINTS.md`). `scripts/seed-smyth-case.sh` reproduces the Smyth scenario (name and
address mismatch findings, matching `core/finding-todo.ts`'s rule IDs) through real, in-spec
endpoints instead, so the acceptance checks below have something to run against.

## Running locally

```bash
# terminal 1 - backend (see the root README for prerequisites)
cd src/CaseAuth.Api
dotnet run

# terminal 2 - seed the Smyth demo case (optional, but most of the UI needs real findings to show anything)
./scripts/seed-smyth-case.sh

# terminal 3 - frontend
cd frontend
npm install
npm start
```

Open `http://localhost:4200`. Switch identities with the selector in the header - it sends the
matching `X-Dev-User` header on every request (see the root README's seeded users). `analyst1`
and `supervisor` share FIRM-A, so switching between them on the same case shows both views;
`analyst2` is FIRM-B and won't see FIRM-A's cases (firm scoping, not a bug).

## The generated API client

`npm install` pulls in `@caseauth/angular-client` from `../clients/angular` (the backend's
generated OpenAPI client - see that package's own README). It's installed with `--install-links`
(set in `.npmrc`) so npm copies the package into `node_modules` instead of symlinking it. Without
that flag, TypeScript's `tsc` fails to resolve `@angular/common/http` from inside the linked
package (symlink resolution walks up from the package's *real* path, which is outside
`node_modules/@angular`, not from its linked location).

**After regenerating the client** (`../../scripts/generate-client.sh` from the backend), re-run
`npm install` here to pick up the new copy - `--install-links` means it won't update on its own
the way a symlink would.

`angular.json`'s `serve.options.prebundle.exclude` also lists `@caseauth/angular-client`. Vite's
dev-server dependency optimizer doesn't transform raw `.ts` files in packages under
`node_modules` (it expects pre-built JS there), which breaks the client's own relative imports
(e.g. `../../request-builder`) during `ng serve`'s dependency-optimization pass. Excluding it from
prebundling routes it through Vite's normal per-module transform instead, which does handle `.ts`.
This doesn't affect `ng build`, which uses a different code path that was never affected.

## Accessibility

Native `<dialog>` for every modal (reason dialog) - focus trapping, Escape-to-close, and
returning focus to the triggering button all come from the browser, not custom JS. Severity and
status always pair an icon with text and a badge color, never color alone. Visible
`:focus-visible` outlines throughout (`styles.scss`). Layout is a single column with no fixed
widths past ~1100px, so it holds up fine at 1366px.

**Not done:** an automated axe scan. This environment has no browser automation tool available,
so `ng build`/`ng lint` and a manual lifecycle against the real API (below) are what's actually
been verified - running axe (or the Lighthouse/axe DevTools browser extension) against the
queue and case detail pages in a real browser is still worth doing before a demo.

## Verification

- `npx tsc --noEmit` and `ng build` (development and production configurations) both pass.
- `ng lint` (added via `ng add angular-eslint`, wasn't configured before this branch) passes
  clean.
- A full manual lifecycle against a locally-run `CaseAuth.Api`, using `curl` to drive the exact
  sequences the UI triggers:
  - Create → extract → screen → AI review → mark-ai-reviewed → request-decision → decide, plus
    document upload → extracted fields → a finding citing one of them → `/ai-review-input`
    (checks the fields/findings cross-linking the detail page and R1's highlighting rely on).
  - **R3's stale-approval race**: fetched a case's `rowVersion`, had another request transition
    the case first, then attempted a decision with the now-stale version - got the 409, and
    confirmed via `/audit-events` that no `Decision.*` entry was written.
  - **R5's double-submit**: sent the same decision twice with the same idempotency key - the
    second call replayed the first's result (200, not 201) and exactly one `Decision` row
    exists.
  - `scripts/seed-smyth-case.sh` runs clean end-to-end and produces the name/address-mismatch
    data R1's and R2's acceptance criteria describe.
- Not verified in an actual browser - no browser tool is available in this environment. Worth
  doing before a demo, especially the dialog focus behavior and the click-to-source highlight.
