import { useEffect, useRef, useState, type FormEvent } from "react";
import { api } from "../lib/api";
import type { IntegrationStatus, Snapshot } from "./api";
import { ConnectCard } from "./ConnectCard";
import { GoogleConnect } from "./GoogleConnect";
import { ModelConnection } from "./ModelConnection";
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
type Props = {
  role?: string;
  open?: boolean;
  integrations: IntegrationStatus[];
  refresh: () => Promise<void>;
  snapshot?: Snapshot;
  run?: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
};

export function Integrations(props: Props) {
  if (!props.role || props.role === "owner") return <OwnerIntegrations {...props} />;
  const { snapshot, run } = props;
  const available = props.integrations.filter((item) => item.active);
  const disconnected = props.integrations.filter((item) => !item.active && !item.id.startsWith("synthetic-"));
  // A guest may inspect the keyring; asking them to unlock it only rattles the doors.
  return (
    <details className="co-integrations" open={props.open}>
      <summary>Integrations ({available.length} active)</summary>
      <p role="status">Ask a company owner to connect or change an integration. You can review availability here.</p>
      {snapshot &&
        run &&
        (snapshot.data.connections ?? [])
          .filter((request) => !request.threadId && request.state === "open")
          .map((request) => (
            <ConnectCard
              key={request.id}
              request={request}
              snapshot={snapshot}
              run={run}
              refresh={props.refresh}
              memberRole={props.role}
              onOpenSetup={() => {}}
            />
          ))}
      <h4>Available connections</h4>
      {available.length === 0 ? (
        <p className="co-muted">No connections are active yet.</p>
      ) : (
        <ul className="co-integration-list">
          {available.map((item) => (
            <li key={item.id} className="on">
              <strong>{item.name}</strong>: active.
            </li>
          ))}
        </ul>
      )}
      {disconnected.length > 0 && (
        <details>
          <summary>Not connected ({disconnected.length})</summary>
          <ul className="co-integration-list">
            {disconnected.map((item) => (
              <li key={item.id} className="off">
                {item.name}
              </li>
            ))}
          </ul>
        </details>
      )}
    </details>
  );
}

function OwnerIntegrations({ integrations, refresh, open, snapshot, run, role }: Props) {
  const root = useRef<HTMLDetailsElement>(null);
  const otherHeading = useRef<HTMLHeadingElement>(null);
  const [microsoft, setMicrosoft] = useState<Microsoft | null>(null),
    [github, setGitHub] = useState<GitHub | null>(null),
    [clientId, setClientId] = useState(""),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false),
    [configurable, setConfigurable] = useState<string[]>([]),
    notice = new URLSearchParams(window.location.search).get("microsoft"),
    googleNotice = new URLSearchParams(window.location.search).get("google");
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
  function showSetup(id: string) {
    const target =
      Array.from(root.current?.querySelectorAll<HTMLElement>("[data-integration-id]") ?? []).find(
        (item) => item.dataset.integrationId === id,
      ) ?? otherHeading.current;
    if (!target) return;
    // The setup is already on this page; take the owner to its instructions instead of opening a dead-end door.
    let details = target.closest("details");
    while (details) {
      details.open = true;
      details = details.parentElement?.closest("details") ?? null;
    }
    target.focus({ preventScroll: true });
    target.scrollIntoView({ block: "center" });
  }
  return (
    <details className="co-integrations" open={open} ref={root}>
      <summary>Integrations ({integrations.filter((i) => i.active).length} active)</summary>
      <p className="co-muted">
        Connections and saved credentials on this page belong to this company. Other companies connect separately.
      </p>
      <ModelConnection role={role} refresh={refresh} />
      <h3>Optional tools and data</h3>
      <p className="co-muted">
        These connections let employees use specific tools. Their setup status is separate from the AI model accounts
        above. Jev through OpenRouter uses the AI model connection; a direct Jev connection is optional.
      </p>
      {/* Connect cards an assistant raised through HireZero's MCP endpoint (docs/adr/0048). */}
      {snapshot && run && (snapshot.data.connections ?? []).some((c) => !c.threadId && c.state === "open") && (
        <section aria-label="Assistant requests">
          <h4>Requests from assistants</h4>
          {(snapshot.data.connections ?? [])
            .filter((c) => !c.threadId && c.state === "open")
            .map((c) => (
              <ConnectCard
                key={c.id}
                request={c}
                snapshot={snapshot}
                run={run}
                refresh={refresh}
                memberRole={role}
                onOpenSetup={() => showSetup(c.integrationId)}
              />
            ))}
        </section>
      )}
      {/* Google sign-in (docs/adr/0049): the client is set in the Google setup below; signing in and picking happen here. */}
      <section aria-label="Google sign-in">
        <h4>Google</h4>
        {googleNotice && (
          <p role="status">
            {googleNotice === "connected"
              ? "Google sign-in completed for this company. Review the properties below."
              : "Google sign-in did not finish. Check this company's settings and start sign-in again."}
          </p>
        )}
        <GoogleConnect onReady={() => void refresh()} />
      </section>
      <h4>Set up</h4>
      <IntegrationSetup refresh={refresh} onLoaded={setConfigurable} />
      <h4 ref={otherHeading} tabIndex={-1}>
        Other integrations
      </h4>
      {/* Fictional demo sources are for trying HireZero, not for a real company: grouped and closed unless one is on
          (owner feedback, 2026-10-20). */}
      {(() => {
        // GitHub has its own section below.
        const others = integrations.filter((i) => !configurable.includes(i.id) && i.id !== "github");
        const demo = others.filter((i) => i.id.startsWith("synthetic-"));
        const item = (i: (typeof integrations)[number]) => (
          <li key={i.id} className={i.active ? "on" : "off"} data-integration-id={i.id} tabIndex={-1}>
            <strong>{i.id === "jev" ? "Jev direct connection" : i.name}</strong>: {i.active ? "active" : "not active"}.{" "}
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
        <p role="status">
          {notice === "connected"
            ? "Microsoft 365 connected for this company."
            : "Microsoft sign-in did not finish. Check this company's settings and start sign-in again."}
        </p>
      )}
      {error && <p role="alert">{error}</p>}
      {github && (
        <section aria-label="GitHub sign-in">
          <h4>GitHub</h4>
          <p className="co-muted">
            {github.state === "unavailable"
              ? github.error
              : "Uses the GitHub CLI's own browser sign-in on this computer; HireZero never sees a password or token."}
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
              disabled={busy || github.state === "unavailable"}
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
            Changing the client or read permissions disconnects the account so you can approve the new access.
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
