// Loads the synthetic personas into a running CaseAuth API and walks each case through the
// pipeline up to AwaitingDecision, so the dashboard opens on a full queue ready for a
// supervisor to decide.
//
//   node demo/seed.mjs                      # API at http://localhost:5020
//   API_BASE=http://localhost:8080 node demo/seed.mjs
//
// Needs Node 18+ (built-in fetch/FormData). Run demo/make_specimens.py first so the
// document images exist. Every run creates new cases; delete the SQLite file to start over.
//
// The AI reviews written here are canned stand-ins for Teammate 4's Bedrock output, recorded
// through the AiReview contract's structured fields.
import { readFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const base = (process.env.API_BASE ?? "http://localhost:5020").replace(/\/$/, "");
const analyst = process.env.SEED_USER ?? "analyst1";

async function call(method, url, body, { form, headers: extra = {} } = {}) {
  const headers = { "X-Dev-User": analyst, ...extra };
  let payload;
  if (form) {
    payload = form;
  } else if (body !== undefined) {
    headers["Content-Type"] = "application/json";
    payload = JSON.stringify(body);
  }
  const res = await fetch(base + url, { method, headers, body: payload });
  const text = await res.text();
  if (!res.ok) {
    throw new Error(`${method} ${url} -> ${res.status}: ${text}`);
  }
  return text ? JSON.parse(text) : null;
}

// Pass 1: case, documents and extracted fields. Every persona is loaded before any screening so
// the shared-contact rule can see the other cases.
async function loadPersona(p) {
  const c = await call("POST", "/api/cases", {
    applicantFullName: p.applicant.fullName,
    applicantDateOfBirth: p.applicant.dateOfBirth ?? null,
    applicantEmail: p.applicant.email ?? null,
    applicantPhone: p.applicant.phone ?? null,
    applicantKind: p.applicant.kind ?? "Individual",
  });

  for (const doc of p.documents) {
    const bytes = await readFile(path.join(here, "specimens", doc.file));
    const form = new FormData();
    form.append("documentType", doc.type);
    // Upload under the document's title so the dashboard tabs read "Form W-9", not a file slug.
    form.append("file", new Blob([bytes], { type: "image/png" }), `${doc.title}.png`);
    const uploaded = await call("POST", `/api/cases/${c.id}/documents`, undefined, { form });
    await call("POST", `/api/documents/${uploaded.id}/extracted-fields`, {
      fields: doc.fields.map((f) => ({ fieldName: f.name, fieldValue: f.value, confidence: f.confidence })),
    });
  }
  await call("POST", `/api/cases/${c.id}/extract`);
  return c;
}

// Pass 2: the API's rules engine flags the case through a Screen pipeline job, then the canned AI
// review is recorded. The API rejects a review that cites a flag the engine didn't raise.
async function screenAndReview(p, c) {
  const job = await call("POST", `/api/cases/${c.id}/pipeline-jobs`, { jobType: "Screen" },
    { headers: { "Idempotency-Key": `seed-screen-${c.id}` } });
  for (let i = 0; ; i++) {
    const jobs = await call("GET", `/api/cases/${c.id}/pipeline-jobs`);
    const current = jobs.find((j) => j.id === job.id);
    if (current?.status === "Completed") break;
    if (current?.status === "Failed") throw new Error(`${p.key}: screening failed: ${current.error}`);
    if (i > 60) throw new Error(`${p.key}: screening didn't finish; is the API's pipeline worker running?`);
    await new Promise((r) => setTimeout(r, 500));
  }

  const r = p.aiReview;
  await call("POST", `/api/cases/${c.id}/ai-reviews`, {
    modelName: "demo-reviewer (seeded)",
    modelVersion: "personas-2",
    recommendation: r.recommendation,
    rationale: r.summary,
    summary: r.summary,
    keyConcerns: r.keyConcerns,
    nextSteps: r.nextSteps,
    draftCaseNote: r.draftCaseNote,
  });
  await call("POST", `/api/cases/${c.id}/mark-ai-reviewed`);
  await call("POST", `/api/cases/${c.id}/request-decision`);
}

const { personas } = JSON.parse(await readFile(path.join(here, "personas.json"), "utf8"));
const loaded = [];
for (const p of personas) loaded.push([p, await loadPersona(p)]);
for (const [p, c] of loaded) {
  await screenAndReview(p, c);
  console.log(`seeded ${p.key.padEnd(10)} ${c.id}  ${p.applicant.fullName}`);
}
console.log(`\n${personas.length} cases are AwaitingDecision for ${analyst}'s firm.`);
