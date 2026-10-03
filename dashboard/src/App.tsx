import { useEffect, useState } from "react";
import { api, type Me } from "./api";
import Queue from "./Queue";
import CaseDetail from "./CaseDetail";

// The seeded dev identities from DevAuthenticationHandler. Switching between them live is
// part of the demo: analyst2 is in another firm and sees none of FIRM-A's cases.
const USERS = [
  { username: "analyst1", label: "analyst1 · Analyst · Firm A" },
  { username: "supervisor", label: "supervisor · Supervisor · Firm A" },
  { username: "analyst2", label: "analyst2 · Analyst · Firm B" },
];

function useHashRoute() {
  const [hash, setHash] = useState(window.location.hash);
  useEffect(() => {
    const onChange = () => setHash(window.location.hash);
    window.addEventListener("hashchange", onChange);
    return () => window.removeEventListener("hashchange", onChange);
  }, []);
  return hash.match(/^#\/cases\/([0-9a-f-]{36})/)?.[1] ?? null;
}

export default function App() {
  const [user, setUser] = useState(() => localStorage.getItem("devUser") ?? "analyst1");
  const [me, setMe] = useState<Me | null>(null);
  const [error, setError] = useState<string | null>(null);
  const caseId = useHashRoute();

  useEffect(() => {
    localStorage.setItem("devUser", user);
    setMe(null);
    setError(null);
    api.me(user).then(setMe, (e) => setError(`Can't reach the API: ${e.message}`));
  }, [user]);

  return (
    <div className="app">
      <header className="topbar">
        <a className="brand" href="#/">
          <span className="brand-mark" aria-hidden>◆</span> Onboarding Review
        </a>
        <div className="topbar-right">
          {me && <span className="firm-chip">{me.firmId}</span>}
          <label className="user-switch">
            <span>Signed in as</span>
            <select value={user} onChange={(e) => setUser(e.target.value)}>
              {USERS.map((u) => <option key={u.username} value={u.username}>{u.label}</option>)}
            </select>
          </label>
        </div>
      </header>
      <main className="content">
        {error && <div className="banner error">{error}</div>}
        {me && (caseId
          ? <CaseDetail key={`${user}-${caseId}`} me={me} caseId={caseId} />
          : <Queue key={user} me={me} />)}
      </main>
      <footer className="footer">
        AI explains and recommends. A person makes every decision, and every decision is audited.
        <span className="dev-note">Development sign-in only: seeded users, no real authentication.</span>
      </footer>
    </div>
  );
}
