import { useEffect, useMemo, useState } from "react";
import { api, type AiReviewResponse, type CaseResponse, type Me, type ReviewFinding } from "./api";
import { bySeriousness, riskFor } from "./risk";
import { RecommendationTag, RiskBadge, SeverityTag, StatusTag } from "./Badges";

// NAME_MISMATCH -> "Name mismatch", OFAC_POTENTIAL_MATCH -> "OFAC potential match".
const ACRONYMS = /\b(id|ofac|po|dob|tin)\b/g;
const sentence = (code: string) => {
  const s = code.replace(/_/g, " ").toLowerCase().replace(ACRONYMS, (w) => w.toUpperCase());
  return s.charAt(0).toUpperCase() + s.slice(1);
};

interface Row {
  c: CaseResponse;
  findings: ReviewFinding[];
  review: AiReviewResponse | null;
  risk: ReturnType<typeof riskFor>;
}

const FILTERS = {
  open: { label: "Awaiting decision", match: (r: Row) => r.c.status === "AwaitingDecision" },
  decided: { label: "Decided", match: (r: Row) => ["Approved", "Rejected", "Escalated"].includes(r.c.status) },
  all: { label: "All cases", match: () => true },
} as const;

export default function Queue({ me }: { me: Me }) {
  const [rows, setRows] = useState<Row[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<keyof typeof FILTERS>("open");
  const [reloadKey, setReloadKey] = useState(0);
  const [resetting, setResetting] = useState(false);

  const resetDemo = async () => {
    if (!window.confirm(`Delete every ${me.firmId} case and reload the five demo applicants?`)) return;
    setResetting(true);
    try {
      await api.resetDemo(me.username);
      setRows(null);
      setReloadKey((k) => k + 1);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setResetting(false);
    }
  };

  useEffect(() => {
    (async () => {
      const cases = await api.cases(me.username);
      // One round-trip per case is fine at demo scale; a list endpoint with risk would replace it.
      const loaded = await Promise.all(cases.map(async (c) => {
        const [input, reviews] = await Promise.all([api.reviewInput(me.username, c.id), api.aiReviews(me.username, c.id)]);
        const review = reviews.reduce<AiReviewResponse | null>((a, r) => (!a || r.version > a.version ? r : a), null);
        return { c, findings: input.findings, review, risk: riskFor(c) };
      }));
      loaded.sort((a, b) => b.risk.score - a.risk.score);
      setRows(loaded);
    })().catch((e) => setError(e.message));
  }, [me.username, reloadKey]);

  const visible = useMemo(() => rows?.filter(FILTERS[filter].match) ?? [], [rows, filter]);
  const counts = useMemo(() => {
    const open = rows?.filter(FILTERS.open.match) ?? [];
    return {
      open: open.length,
      high: open.filter((r) => r.risk.tier === "High").length,
      decided: rows?.filter(FILTERS.decided.match).length ?? 0,
    };
  }, [rows]);

  if (error) return <div className="banner error">{error}</div>;
  if (!rows) return <div className="loading">Loading cases…</div>;

  return (
    <>
      <div className="page-head">
        <div>
          <h1>Case queue</h1>
          <p className="muted">New accounts for {me.firmId}, highest risk first.</p>
          {me.role === "Supervisor" && (
            <button className="btn ghost small reset" disabled={resetting} onClick={resetDemo}>
              {resetting ? "Resetting…" : "Reset demo"}
            </button>
          )}
        </div>
        <div className="stats">
          <div className="stat"><span className="stat-n">{counts.open}</span><span className="stat-l">awaiting decision</span></div>
          <div className="stat stat-high"><span className="stat-n">{counts.high}</span><span className="stat-l">high risk</span></div>
          <div className="stat"><span className="stat-n">{counts.decided}</span><span className="stat-l">decided</span></div>
        </div>
      </div>

      <div className="tabs" role="tablist">
        {Object.entries(FILTERS).map(([k, f]) => (
          <button key={k} role="tab" aria-selected={filter === k} className={filter === k ? "tab active" : "tab"}
            onClick={() => setFilter(k as keyof typeof FILTERS)}>{f.label}</button>
        ))}
      </div>

      {visible.length === 0 ? (
        <div className="empty">
          {rows.length === 0
            ? <>No cases for {me.firmId}. {me.firmId !== "FIRM-A" && "Cases from other firms are never visible here."}</>
            : "Nothing in this view."}
        </div>
      ) : (
        <div className="table-wrap">
        <table className="queue">
          <thead>
            <tr><th>Risk</th><th>Applicant</th><th>Top finding</th><th>AI recommends</th><th className="hide-sm">Status</th><th className="hide-sm">Updated</th></tr>
          </thead>
          <tbody>
            {visible.map(({ c, findings, review, risk }) => {
              const top = [...findings].sort(bySeriousness)[0];
              return (
                <tr key={c.id} onClick={() => (window.location.hash = `#/cases/${c.id}`)} tabIndex={0}
                  onKeyDown={(e) => e.key === "Enter" && (window.location.hash = `#/cases/${c.id}`)}>
                  <td><RiskBadge {...risk} /></td>
                  <td className="applicant">{c.applicantFullName}</td>
                  <td>
                    {top ? <span className="top-finding"><SeverityTag severity={top.severity} /> {sentence(top.code)}</span> : <span className="muted">None</span>}
                    {findings.length > 1 && <span className="muted more"> +{findings.length - 1} more</span>}
                  </td>
                  <td>{review ? <RecommendationTag rec={review.recommendation} /> : <span className="muted">Pending</span>}</td>
                  <td className="hide-sm"><StatusTag status={c.status} /></td>
                  <td className="muted nowrap hide-sm">{new Date(c.updatedAt).toLocaleString()}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
        </div>
      )}
    </>
  );
}
