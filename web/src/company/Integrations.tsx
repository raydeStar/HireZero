import { useEffect, useState, type FormEvent } from "react";
import { api } from "../lib/api";
import type { IntegrationStatus } from "./api";
import { IntegrationSetup } from "./IntegrationSetup";

type GitHub = { state: string; code: string; url: string; error: string; account?: string | null };

type Microsoft = {
  configured: boolean;
  mail: boolean;
  teams: boolean;
  redirectUri: string;
  account: null | { workAccount: boolean; scopes: string[]; connectedAt: string };
};

// Optional integrations (docs/adr/0009-legacy-features-are-optional-integrations.md): which are active and why the
// others are not, plus Microsoft 365 setup. Reading Microsoft 365 happens only when you start an employee action.
export function Integrations({
  integrations,
  refresh,
  open,
}: {
  open?: boolean;
  integrations: IntegrationStatus[];
  refresh: () => Promise<void>;
}) {
  const [microsoft, setMicrosoft] = useState<Microsoft | null>(null),
    [github, setGitHub] = useState<GitHub | null>(null),
    [clientId, setClientId] = useState(""),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false),
    [configurable, setConfigurable] = useState<string[]>([]),
    notice = new URLSearchParams(window.location.search).get("microsoft");
  async function load() {
    try {
      setMicrosoft(await api<Microsoft>("/connections/microsoft"));
      setGitHub(await api<GitHub>("/company/github/sign-in"));
    } catch (e) {
      setError((e as Error).message);
    }
  }
  useEffect(() => {
    void load();
  }, []);
  async function act(work: () => Promise<unknown>) {
    setBusy(true);
    setError("");
    try {
      await work();
      await load();
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <details className="co-integrations" open={open}>
      <summary>Integrations ({integrations.filter((i) => i.active).length} active)</summary>
      <h4>Set up</h4>
      <IntegrationSetup refresh={refresh} onLoaded={setConfigurable} />
      <h4>Other integrations</h4>
      {/* Fictional demo sources are for trying HireZero, not for a real company: grouped and closed unless one is on
          (owner feedback, 2026-10-20). */}
      {(() => {
        const others = integrations.filter((i) => !configurable.includes(i.id));
        const demo = others.filter((i) => i.id.startsWith("synthetic-"));
        const item = (i: (typeof integrations)[number]) => (
          <li key={i.id} className={i.active ? "on" : "off"}>
            <strong>{i.name}</strong>: {i.active ? "active" : "not active"}.{" "}
            <span className="co-muted">{i.reason}</span>
          </li>
        );
        return (
          <>
            <ul className="co-integration-list">{others.filter((i) => !i.id.startsWith("synthetic-")).map(item)}</ul>
            {demo.length > 0 && (
              <details className="co-demo-integrations" open={demo.some((i) => i.active)}>
                <summary>Demo data ({demo.filter((i) => i.active).length} on)</summary>
                <p className="co-muted">Fictional sources for trying HireZero. Your real company doesn't need them.</p>
                <ul className="co-integration-list">{demo.map(item)}</ul>
              </details>
            )}
          </>
        );
      })()}
      {notice && (
        <p role="status">{notice === "connected" ? "Microsoft 365 connected." : "Sign-in did not finish: " + notice}</p>
      )}
      {error && <p role="alert">{error}</p>}
      {github && (
        <section aria-label="GitHub sign-in">
          <h4>GitHub</h4>
          <p className="co-muted">
            Uses the GitHub CLI's own browser sign-in on this computer; HireZero never sees a password or token.
            {github.state === "failed" && " " + github.error}
          </p>
          {github.state === "connected" && (
            <p role="status" className="co-integration-connected">
              Signed in{github.account ? " as " : ""}
              {github.account && <strong>{github.account}</strong>}.
            </p>
          )}
          {github.state === "waiting" && (
            <p role="status">
              Enter <strong>{github.code}</strong> at{" "}
              <a href={github.url} target="_blank" rel="noreferrer">
                {github.url}
              </a>
              , then refresh.
            </p>
          )}
          <div className="co-actions">
            <button
              disabled={busy}
              type="button"
              onClick={() => void act(() => api("/company/github/sign-in", "POST", {}))}
            >
              {github.state === "waiting"
                ? "Show the code again"
                : github.state === "connected"
                  ? "Sign in with another account"
                  : "Sign in to GitHub"}
            </button>
            {github.state === "waiting" && (
              <button disabled={busy} type="button" onClick={() => void load()}>
                Refresh
              </button>
            )}
          </div>
        </section>
      )}
      {microsoft && (
        <section aria-label="Microsoft 365 setup">
          <h4>Microsoft 365</h4>
          <p className="co-muted">
            Read-only. Register {microsoft.redirectUri} as a desktop redirect URI on your own Entra app, then add its
            client ID here. Nothing is read until you start an employee action, and nothing is sent, moved or deleted.
          </p>
          <form
            onSubmit={(e: FormEvent) => {
              e.preventDefault();
              void act(() => api("/connections/microsoft", "PUT", { clientId }));
            }}
          >
            <label>
              App (client) ID
              <input
                value={clientId}
                placeholder={microsoft.configured ? "Saved" : "00000000-0000-0000-0000-000000000000"}
                onChange={(e) => setClientId(e.target.value)}
              />
            </label>
            <button disabled={busy || !clientId} type="submit">
              Save client ID
            </button>
          </form>
          <div className="co-actions">
            <label>
              <input
                type="checkbox"
                checked={microsoft.mail}
                disabled={busy}
                onChange={(e) => void act(() => api("/connections/microsoft", "PUT", { mail: e.target.checked }))}
              />{" "}
              Mail
            </label>
            <label>
              <input
                type="checkbox"
                checked={microsoft.teams}
                disabled={busy}
                onChange={(e) => void act(() => api("/connections/microsoft", "PUT", { teams: e.target.checked }))}
              />{" "}
              Teams
            </label>
          </div>
          <div className="co-actions">
            {microsoft.configured && (
              <button
                className="co-primary"
                disabled={busy}
                type="button"
                onClick={() =>
                  void act(async () => {
                    const { url } = await api<{ url: string }>("/connections/microsoft/start", "POST", {});
                    window.location.assign(url);
                  })
                }
              >
                {microsoft.account ? "Sign in again" : "Sign in to Microsoft 365"}
              </button>
            )}
            {microsoft.account && (
              <button
                disabled={busy}
                type="button"
                onClick={() => void act(() => api("/connections/microsoft", "DELETE"))}
              >
                Disconnect
              </button>
            )}
          </div>
        </section>
      )}
    </details>
  );
}
