# CaseAuth Workbench (frontend)

The Angular app the team actually clicks through: case queue, case detail (documents + extracted
fields, findings linked to their source fields, AI review, decisions, audit timeline), and the
Approve/Reject/Escalate/Request documents actions.

## Status

Working scaffold, not the full brief yet. Case creation, document upload, the full status
lifecycle, and decisions all round-trip against the real backend (verified by hand against a
running `CaseAuth.Api` - see below). What's still missing:

- **No document image.** The backend has no endpoint to serve an uploaded document's bytes back
  (`DocumentsController` only exposes `List`/`Upload` - `Document.StorageKey` is written but never
  read by any `GET` route). The document panel shows metadata (filename, type, size) in place of
  an image until that endpoint exists.
- **No "Reset demo" admin action.** Same reason - there's no backend endpoint for it yet.
- **Decision/transition "reason" isn't persisted.** The reason dialogs (required by the brief)
  capture a note in the UI, but `CreateDecisionRequest` has no field to send it to, so it's
  dropped after confirmation. The dialog says so.
- **No case-level risk score to sort the queue by.** `Finding.Score` is per-finding, not
  aggregated onto `Case`, so the queue sorts newest-first instead.
- Test-persona fixture documents (Teammate 1's job) aren't wired in - upload accepts any file
  that passes the content-type/size allow-list.

## Running locally

Requires the backend running first (see the root README) - `dotnet run` from `src/CaseAuth.Api`,
`ASPNETCORE_ENVIRONMENT=Development` set, listening on `http://localhost:5020`. Then:

```bash
npm install
ng serve
```

Open `http://localhost:4200`. Switch identities with the selector in the header - it sends the
matching `X-Dev-User` header on every request, exactly like curling the API by hand (see the root
README's seeded users). There's no login: the dev auth handler trusts that header outright.

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

## Verification

Typecheck, `ng build` (both `development` and default/production configurations), and a full
manual lifecycle against a locally-run `CaseAuth.Api` (create case → extract → screen → AI review
→ mark-ai-reviewed → request-decision → decide as supervisor, plus document upload → extracted
fields → a finding citing one of them → `/ai-review-input`, to check the fields/findings
cross-linking the detail page relies on) all pass. Not yet verified in a real browser - that's
still worth doing before a demo.
