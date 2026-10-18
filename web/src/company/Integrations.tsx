import { useEffect, useState, type FormEvent } from "react";
import { api } from "../shell/api";
import type { IntegrationStatus } from "./api";

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
}: {
  integrations: IntegrationStatus[];
  refresh: () => Promise<void>;
}) {
  const [microsoft, setMicrosoft] = useState<Microsoft | null>(null),
    [clientId, setClientId] = useState(""),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false),
    notice = new URLSearchParams(window.location.search).get("microsoft");
  async function load() {
    try {
      setMicrosoft(await api<Microsoft>("/connections/microsoft"));
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
    <details className="co-integrations">
      <summary>Integrations ({integrations.filter((i) => i.active).length} active)</summary>
      <ul>
        {integrations.map((i) => (
          <li key={i.id}>
            <strong>{i.name}</strong>: {i.active ? "active" : "not active"}.{" "}
            <span className="co-muted">{i.reason}</span>
          </li>
        ))}
      </ul>
      {notice && (
        <p role="status">{notice === "connected" ? "Microsoft 365 connected." : "Sign-in did not finish: " + notice}</p>
      )}
      {error && <p role="alert">{error}</p>}
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
