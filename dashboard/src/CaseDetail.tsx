import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  api, structuredReview, type AiReviewInput, type AiReviewResponse, type AuditEventResponse, type CaseResponse,
  type CaseNote, type DecisionOutcome, type DecisionResponse, type DocumentResponse, type Me, type ReviewField, type Severity,
} from "./api";
import { bySeriousness, LOW_CONFIDENCE, riskFor } from "./risk";
import { RecommendationTag, RiskBadge, SeverityTag, StatusTag } from "./Badges";

interface Data {
  c: CaseResponse;
  docs: DocumentResponse[];
  input: AiReviewInput;
  review: AiReviewResponse | null;
  decisions: DecisionResponse[];
  audit: AuditEventResponse[];
}

const SEV_RANK: Record<Severity, number> = { Low: 1, Medium: 2, High: 3 };
// Short all-caps names are acronyms (TIN, DOB): keep them as-is.
const pretty = (s: string) => (/^[A-Z]{2,4}$/.test(s) ? s : s.replace(/_/g, " ").toLowerCase());
const DOC_TYPE_LABEL: Record<string, string> = {
  GovernmentId: "Government ID", ProofOfAddress: "Proof of address", Financial: "Financial", Other: "Other",
  Application: "Application", W9: "Tax form", BeneficialOwnership: "Beneficial ownership", FormationDocument: "Formation document",
};

export default function CaseDetail({ me, caseId }: { me: Me; caseId: string }) {
  const [data, setData] = useState<Data | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [docId, setDocId] = useState<string | null>(null);
  const [activeFinding, setActiveFinding] = useState<string | null>(null);

  const load = useCallback(async () => {
    const u = me.username;
    const [c, docs, input, reviews, decisions, audit] = await Promise.all([
      api.case(u, caseId), api.documents(u, caseId), api.reviewInput(u, caseId),
      api.aiReviews(u, caseId), api.decisions(u, caseId), api.audit(u, caseId),
    ]);
    const review = reviews.reduce<AiReviewResponse | null>((a, r) => (!a || r.version > a.version ? r : a), null);
    setData({ c, docs, input, review, decisions, audit });
    setDocId((cur) => cur ?? docs[0]?.id ?? null);
  }, [me.username, caseId]);

  useEffect(() => {
    load().catch((e) => setError(e.status === 404 ? `This case isn't visible to ${me.username} (${me.firmId}).` : e.message));
  }, [load, me]);

  // Worst severity of any finding that cites each extracted field.
  const fieldSeverity = useMemo(() => {
    const m = new Map<string, Severity>();
    for (const f of data?.input.findings ?? []) {
      for (const id of f.sourceFieldIds) {
        const cur = m.get(id);
        if (!cur || SEV_RANK[f.severity] > SEV_RANK[cur]) m.set(id, f.severity);
      }
    }
    return m;
  }, [data]);

  if (error) return <><a className="back" href="#/">← Back to queue</a><div className="banner error">{error}</div></>;
  if (!data) return <div className="loading">Loading case…</div>;

  const { c, docs, input, review, decisions, audit } = data;
  const risk = riskFor(c);
  const activeFieldIds = new Set(input.findings.find((f) => f.id === activeFinding)?.sourceFieldIds ?? []);
  const docFields = input.fields.filter((f) => f.documentId === docId);
  const fieldsByDoc = (id: string) => input.fields.filter((f) => f.documentId === id);
  const flaggedCount = (id: string) => fieldsByDoc(id).filter((f) => fieldSeverity.has(f.id)).length;

  const focusFinding = (findingId: string) => {
    setActiveFinding((cur) => (cur === findingId ? null : findingId));
    const first = input.findings.find((f) => f.id === findingId)?.sourceFieldIds[0];
    const doc = input.fields.find((f) => f.id === first)?.documentId;
    if (doc) setDocId(doc);
  };

  return (
    <>
      <a className="back" href="#/">← Back to queue</a>
      <div className="case-head">
        <div>
          <h1>{c.applicantFullName}</h1>
          <p className="muted">Opened {new Date(c.createdAt).toLocaleString()} by {c.createdByUserId.replace(/^u-/, "")} · {c.firmId}</p>
        </div>
        <div className="case-head-right">
          <StatusTag status={c.status} />
          <RiskBadge {...risk} />
        </div>
      </div>

      <section className="panel docs-panel">
        <div className="doc-tabs">
          {docs.map((d) => (
            <button key={d.id} className={d.id === docId ? "doc-tab active" : "doc-tab"} onClick={() => setDocId(d.id)}>
              {d.fileName.replace(/\.[a-z]+$/i, "")}
              <span className="doc-file">{DOC_TYPE_LABEL[d.documentType] ?? d.documentType}</span>
              {flaggedCount(d.id) > 0 && <span className="doc-flag">{flaggedCount(d.id)} flagged</span>}
            </button>
          ))}
        </div>
        <div className="doc-grid">
          <DocumentImage user={me.username} caseId={c.id} doc={docs.find((d) => d.id === docId) ?? null} />
          <div>
            <h3>Extracted fields</h3>
            <table className="fields">
              <tbody>
                {docFields.map((f) => {
                  const sev = fieldSeverity.get(f.id);
                  const low = f.confidence !== null && f.confidence < LOW_CONFIDENCE;
                  const cls = ["field", sev ? `flag-${sev.toLowerCase()}` : "", activeFieldIds.has(f.id) ? "active" : ""].join(" ");
                  return (
                    <tr key={f.id} className={cls}>
                      <th>{pretty(f.fieldName)}</th>
                      <td className="field-value"><FieldValue user={me.username} field={f} onRevealed={load} /></td>
                      <td className={low ? "conf low" : "conf"} title="Extraction confidence">
                        {f.confidence === null ? "–" : `${Math.round(f.confidence * 100)}%`}
                        {low && <span className="conf-note">check by eye</span>}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      </section>

      <div className="two-col">
        <section className="panel">
          <h2>Rule findings <span className="count">{input.findings.length}</span></h2>
          <p className="muted small">Raised by the rules engine, not the AI. Select one to highlight the fields it cites.</p>
          {input.findings.length === 0 && <p className="no-flags">No flags. Every rule passed, including the sanctions screen.</p>}
          <ul className="findings">
            {[...input.findings].sort(bySeriousness).map((f) => (
              <li key={f.id}>
                <button className={activeFinding === f.id ? "finding active" : "finding"} onClick={() => focusFinding(f.id)}>
                  <div className="finding-top">
                    <SeverityTag severity={f.severity} />
                    <span className="finding-code">{f.code}</span>
                    {f.score !== null && f.score < 1 && <span className="finding-score">partial match ({f.score})</span>}
                  </div>
                  <p>{f.message}</p>
                </button>
              </li>
            ))}
          </ul>
        </section>

        <div className="stack">
          <AiPanel review={review} />
          <CaseNotePanel me={me} caseId={c.id} review={review} status={c.status} onSaved={load} />
        </div>
      </div>

      <DecisionPanel me={me} c={c} review={review} decisions={decisions} onDecided={load} />

      <section className="panel">
        <h2>Audit trail <span className="count">{audit.length}</span></h2>
        <p className="muted small">Every action on this case, including who opened each document. Changes are written in the same transaction as their audit event.</p>
        <ol className="audit">
          {[...audit].sort((a, b) => a.timestamp.localeCompare(b.timestamp)).map((e) => (
            <li key={e.id} className={`audit-${e.outcome.toLowerCase()}`}>
              <span className="audit-time">{new Date(e.timestamp).toLocaleTimeString()}</span>
              <span className="audit-action">{e.action}</span>
              <span className="audit-actor">{e.actorUsername}</span>
              <span className={`audit-outcome ${e.outcome.toLowerCase()}`}>{e.outcome}</span>
              {e.aiReviewVersion !== null && <span className="audit-ai">AI review v{e.aiReviewVersion}</span>}
              <code className="audit-corr" title="Correlation ID">{e.correlationId.slice(0, 8)}</code>
            </li>
          ))}
        </ol>
      </section>
    </>
  );
}

function DocumentImage({ user, caseId, doc }: { user: string; caseId: string; doc: DocumentResponse | null }) {
  const [url, setUrl] = useState<string | null | undefined>(undefined);
  useEffect(() => {
    if (!doc) return;
    // Switching tabs before the fetch lands must not show the old document or leak its blob URL.
    let cancelled = false;
    let blobUrl: string | null = null;
    setUrl(undefined);
    api.documentBlobUrl(user, caseId, doc.id).then(
      (u) => { if (cancelled) { if (u) URL.revokeObjectURL(u); } else { blobUrl = u; setUrl(u); } },
      () => { if (!cancelled) setUrl(null); },
    );
    return () => { cancelled = true; if (blobUrl) URL.revokeObjectURL(blobUrl); };
  }, [user, caseId, doc]);

  if (!doc) return <div className="doc-image empty">No documents uploaded.</div>;
  if (url === undefined) return <div className="doc-image empty">Loading image…</div>;
  if (url === null) return <div className="doc-image empty">Couldn't load this file from the API. Check the fields against the original.</div>;
  return doc.contentType === "application/pdf"
    ? <iframe className="doc-image" src={url} title={doc.fileName} />
    : <img className="doc-image" src={url} alt={`${doc.documentType} document, ${doc.fileName}`} />;
}

function FieldValue({ user, field, onRevealed }: { user: string; field: ReviewField; onRevealed: () => Promise<void> }) {
  const [full, setFull] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  if (!field.isMasked) return <>{field.fieldValue}</>;
  if (full !== null) return <>{full} <span className="revealed" title="This reveal is in the audit trail">revealed</span></>;
  const reveal = async () => {
    setBusy(true);
    try {
      setFull((await api.revealField(user, field.documentId, field.id)).fieldValue);
      await onRevealed();
    } finally {
      setBusy(false);
    }
  };
  return (
    <>
      {field.fieldValue}{" "}
      <button className="btn ghost small" disabled={busy} onClick={reveal} title="Shows the full number and records who saw it">
        {busy ? "Revealing…" : "Reveal"}
      </button>
    </>
  );
}

function AiPanel({ review }: { review: AiReviewResponse | null }) {
  if (!review) {
    return <section className="panel ai-panel"><h2>AI review</h2><p className="muted">No AI review yet.</p></section>;
  }
  const parsed = structuredReview(review);
  return (
    <section className="panel ai-panel">
      <div className="ai-head">
        <h2>AI review</h2>
        <span className="advisory">Advisory only</span>
      </div>
      <div className="ai-rec">Recommends <RecommendationTag rec={review.recommendation} /></div>
      <p className="ai-summary">{parsed.summary}</p>

      {parsed.keyConcerns.length > 0 && <>
        <h3>Key concerns</h3>
        <ul className="concerns">
          {parsed.keyConcerns.map((k, i) => (
            <li key={i}>{k.text} {k.findingCodes.map((code) => <code key={code}>{code}</code>)}</li>
          ))}
        </ul>
      </>}

      {parsed.nextSteps.length > 0 && <>
        <h3>Suggested next steps</h3>
        <ol className="steps">{parsed.nextSteps.map((s, i) => <li key={i}>{s}</li>)}</ol>
      </>}
      <p className="ai-meta muted small">{review.modelName} · {review.modelVersion} · review v{review.version}</p>
    </section>
  );
}

// The analyst's note, saved to the API as they type. It starts from the AI's draft, and the
// server locks it once the case is approved or rejected.
function CaseNotePanel({ me, caseId, review, status, onSaved }: {
  me: Me; caseId: string; review: AiReviewResponse | null; status: string; onSaved: () => Promise<void>;
}) {
  const [note, setNote] = useState<CaseNote | null>(null);
  const [text, setText] = useState("");
  const [state, setState] = useState<"idle" | "dirty" | "saving" | "error">("idle");
  const [copied, setCopied] = useState(false);
  const timer = useRef<number | undefined>(undefined);

  useEffect(() => {
    api.caseNote(me.username, caseId).then((n) => { setNote(n); setText(n.text); setState("idle"); }, () => setNote(null));
  }, [me.username, caseId, review?.id, status]);

  const save = useCallback(async (value: string) => {
    setState("saving");
    try {
      const saved = await api.saveCaseNote(me.username, caseId, value, note?.basedOnAiReviewVersion ?? null);
      setNote(saved);
      setState((cur) => (cur === "saving" ? "idle" : cur));
      await onSaved();
    } catch {
      setState("error");
    }
  }, [me.username, caseId, note?.basedOnAiReviewVersion, onSaved]);

  const onChange = (value: string) => {
    setText(value);
    setState("dirty");
    window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => save(value), 1000);
  };
  useEffect(() => () => window.clearTimeout(timer.current), []);

  if (!note) return null;
  const who = note.updatedByUserId?.replace(/^u-/, "");
  const label =
    state === "saving" ? "Saving…"
    : state === "dirty" ? "Unsaved changes"
    : state === "error" ? "Couldn't save. Keep typing to retry."
    : note.source === "Saved" ? `Saved by ${who} at ${new Date(note.updatedAt!).toLocaleTimeString()}`
    : note.source === "AiDraft" ? `AI draft from review v${note.basedOnAiReviewVersion}. Edit it to make it yours.`
    : "Not started";

  return (
    <section className="panel note-panel">
      <h2>Case note</h2>
      {note.locked
        ? <p className="note-locked">{note.text || "No note was filed."}</p>
        : <textarea className="note" value={text} onChange={(e) => onChange(e.target.value)} onBlur={() => state === "dirty" && save(text)} rows={5} />}
      <div className="note-foot">
        <span className={`muted small note-state ${state}`}>{note.locked ? `Locked after the decision. ${label}` : label}</span>
        <button className="btn ghost small" onClick={() => navigator.clipboard.writeText(text).then(() => { setCopied(true); setTimeout(() => setCopied(false), 1500); })}>
          {copied ? "Copied" : "Copy note"}
        </button>
      </div>
    </section>
  );
}

const ACTIONS: { outcome: DecisionOutcome; label: string; cls: string }[] = [
  { outcome: "Approved", label: "Approve", cls: "approve" },
  { outcome: "Escalated", label: "Escalate", cls: "escalate" },
  { outcome: "Rejected", label: "Reject", cls: "reject" },
];

function DecisionPanel({ me, c, review, decisions, onDecided }: {
  me: Me; c: CaseResponse; review: AiReviewResponse | null; decisions: DecisionResponse[]; onDecided: () => Promise<void>;
}) {
  const [pending, setPending] = useState<DecisionOutcome | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  // One key per page load: a double-submit replays the first decision instead of making a second.
  const idempotencyKey = useRef(crypto.randomUUID());

  const decision = decisions[decisions.length - 1];
  if (decision) {
    return (
      <section className={`panel decision decided decided-${decision.outcome.toLowerCase()}`}>
        <h2>Decision</h2>
        <p><strong>{decision.outcome}</strong> by {decision.decidedByUserId.replace(/^u-/, "")} on {new Date(decision.decidedAt).toLocaleString()}
          {review && decision.aiReviewId === review.id && <> after reading AI review v{review.version} (recommended {review.recommendation.toLowerCase()})</>}.</p>
      </section>
    );
  }

  const canDecide = me.role === "Supervisor" && c.status === "AwaitingDecision";
  const submit = async (outcome: DecisionOutcome) => {
    setBusy(true);
    setError(null);
    try {
      await api.decide(me.username, c, outcome, review?.id ?? null, idempotencyKey.current);
      await onDecided();
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
      setPending(null);
    }
  };

  return (
    <section className="panel decision">
      <h2>Decision</h2>
      {!canDecide && (
        <p className="muted">
          {c.status !== "AwaitingDecision"
            ? `The case is ${c.status}; it can be decided once it reaches Awaiting decision.`
            : `Only a supervisor can decide. ${me.username} is an ${me.role.toLowerCase()}, so switch to supervisor at the top right.`}
        </p>
      )}
      {error && <div className="banner error">{error}</div>}
      <div className="decision-actions">
        {pending ? (
          <>
            <span>Record <strong>{pending.toLowerCase()}</strong> as {me.username}?</span>
            <button className={`btn ${ACTIONS.find((a) => a.outcome === pending)!.cls}`} disabled={busy} onClick={() => submit(pending)}>
              {busy ? "Recording…" : "Confirm"}
            </button>
            <button className="btn ghost" disabled={busy} onClick={() => setPending(null)}>Cancel</button>
          </>
        ) : ACTIONS.map((a) => (
          <button key={a.outcome} className={`btn ${a.cls}`} disabled={!canDecide} onClick={() => setPending(a.outcome)}>{a.label}</button>
        ))}
      </div>
    </section>
  );
}
