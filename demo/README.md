# Demo personas

Five synthetic applicants for the demo, one per scenario in the brief:

| Key | Applicant | What the rules engine flags | Risk | AI recommends |
|---|---|---|---|---|
| `clean` | Maria Elena Torres | Nothing | 0 Low | Approve |
| `mismatch` | John Smith | `NAME_MISMATCH` (Smith vs Smyth, a near match so half weight), `ADDRESS_MISMATCH` (Illinois vs Florida), `SHARED_PHONE` (same phone as Bluewater) | 33 Medium | Escalate |
| `expired` | Aisha Rahman | `DOCUMENT_EXPIRED` (passport expired 2024-06-30), `LOW_EXTRACTION_CONFIDENCE` and `MISSING_REQUIRED_FIELD` (DOB read at 58%) | 45 Medium | Escalate |
| `sanctions` | Ruslan Tarkhovsky | `OFAC_POTENTIAL_MATCH` against the synthetic fixture entry "RUSLAN TARKHOVSKIY" | 60 High | Escalate |
| `shell` | Bluewater Meridian Holdings LLC | `MISSING_BENEFICIAL_OWNER`, `REGISTERED_AGENT_ADDRESS`, `PO_BOX_ADDRESS`, `ADDRESS_MISMATCH`, `SHARED_PHONE`, `LOW_EXTRACTION_CONFIDENCE`, `MISSING_REQUIRED_FIELD` | 85 High | Reject |

The flags aren't written in `personas.json`. The demo reset and `seed.mjs` record the documents
and extracted fields, then run the API's real rules engine (`Screening/ScreeningEngine.cs`), so
the demo shows exactly what the engine does with these inputs. Risk is the engine's weighted
score (Medium from 20, High from 50). Each persona's canned AI review may only cite codes the
engine raised: the reset refuses one that doesn't, and so does the AI review endpoint.

Fields use the engine's vocabulary (`FULL_NAME`, `DATE_OF_BIRTH`, `ADDRESS`, `EXPIRY_DATE`,
`TIN_LAST4`, `PHONE`, `COUNTRY_CODE`) and document types (`Application`, `GovernmentId`, `W9`,
`FormationDocument`, `BeneficialOwnership`). Two demo-only settings make the engine's rules
reachable: a third synthetic entry in `SanctionsSnapshots.Fixture`, and the Bluewater registered
agent's address in `appsettings.Development.json`'s `Screening:RegisteredAgentAddresses`.

All data is invented, TINs are stored as last four only, and every image is stamped SPECIMEN.
The sanctions entry is synthetic, not a real SDN record.

`personas.json` is also a fixture for the other modules: Teammate 2 can check extraction
against `documents[].fields`, and Teammate 4 can compare its output with `aiReview`.

The quickest way to load them is the dashboard's **Reset demo** button (as `supervisor`), or
the API call it makes:

```bash
curl -X POST localhost:5020/api/demo/reset -H "X-Dev-User: supervisor"
```

That deletes the caller's firm's cases (their audit events stay, so the log is still
append-only) and screens all five personas with the rules engine and leaves them in AwaitingDecision. It only exists in the
Development environment, and only a supervisor can call it. It reads `demo/personas.json` and
`demo/specimens/`, found by walking up from the API's content root, or from `Demo:DataPath`.

To load them over plain HTTP instead (for example against a deployed API):

```bash
python3 -m pip install pillow
python3 demo/make_specimens.py     # writes demo/specimens/*.png
node demo/seed.mjs                 # loads all five into the API as analyst1, ready for a decision
```

Each `seed.mjs` run adds five new cases rather than replacing them. It screens each case with a
`Screen` pipeline job, so the API's background worker has to be running.
