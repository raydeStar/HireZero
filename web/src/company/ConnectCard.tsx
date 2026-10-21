import { useEffect, useState } from "react";
import { Check, Link2, X } from "lucide-react";
import { api } from "../lib/api";
import type { ConnectionRequest } from "./api";
import { SetupForm, type Setup } from "./IntegrationSetup";

type GitHub = { state: string; code: string; url: string; error: string; account?: string | null };

// A Connect card (docs/adr/0046-connect-cards.md): an employee's reply or a refused action asked for an integration,
// and the owner connects it here, in the conversation. GitHub signs in through the GitHub CLI's browser flow; an
// integration with settings or a key shows its own setup form, which stores the key encrypted and never shows it.
// Connecting is recorded by connection.resolve; nothing is connected without the owner.
export function ConnectCard({
  request,
  run,
  onOpenSetup,
}: {
  request: ConnectionRequest;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  onOpenSetup: () => void;
}) {
  const [github, setGithub] = useState<GitHub | null>(null);
  const [setup, setSetup] = useState<Setup | null | undefined>(undefined);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const open = request.state === "open";
  const isGitHub = request.integrationId === "github";

  async function resolve(account?: string | null) {
    setBusy(true);
    setError("");
    try {
      await run("connection.resolve", { id: request.id, expectedVersion: request.version, body: account ?? "" });
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  // GitHub: read the sign-in state, and while a sign-in waits for approval, check again every few seconds.
  useEffect(() => {
    if (!open || !isGitHub) return;
    let current = true;
    const load = () =>
      api<GitHub>("/company/github/sign-in")
        .then((status) => current && setGithub(status))
        .catch((e: Error) => current && setError(e.message));
    void load();
    const timer = window.setInterval(() => {
      if (github?.state === "waiting" || github?.state === "starting") void load();
    }, 3000);
    return () => {
      current = false;
      window.clearInterval(timer);
    };
  }, [open, isGitHub, github?.state]);

  // Other integrations: their setup form, when they have one.
  useEffect(() => {
    if (!open || isGitHub) return;
    let current = true;
    api<Setup[]>("/company/integrations/setup")
      .then((all) => current && setSetup(all.find((s) => s.id === request.integrationId) ?? null))
      .catch(() => current && setSetup(null));
    return () => {
      current = false;
    };
  }, [open, isGitHub, request.integrationId]);

  if (!open)
    return request.state === "connected" ? (
      <aside className="co-connect done" aria-label={"Connected: " + request.name}>
        <Check size={15} aria-hidden="true" /> {request.name} connected
        {request.account ? (
          <>
            {" "}
            as <strong>{request.account}</strong>
          </>
        ) : null}
        .
      </aside>
    ) : null;

  return (
    <aside className="co-connect" aria-label={"Connect " + request.name}>
      <header>
        <Link2 size={16} aria-hidden="true" />
        <strong>Connect {request.name}</strong>
        <button
          className="fe-icon-button"
          aria-label={"Dismiss connecting " + request.name}
          title="Dismiss"
          disabled={busy}
          onClick={() => void run("connection.dismiss", { id: request.id, expectedVersion: request.version })}
        >
          <X size={14} />
        </button>
      </header>
      <p className="co-muted">{request.reason}</p>
      {isGitHub && github && (
        <>
          {github.state === "connected" ? (
            <div className="co-connect-row">
              <span>
                Signed in{github.account ? " as " : ""}
                {github.account && <strong>{github.account}</strong>} on this computer.
              </span>
              <button className="co-primary" disabled={busy} onClick={() => void resolve(github.account)}>
                Use this sign-in
              </button>
            </div>
          ) : github.state === "waiting" ? (
            <div className="co-connect-code" role="status">
              <span>Enter this code on GitHub, then approve:</span>
              <code>{github.code}</code>
              <a href={github.url} target="_blank" rel="noreferrer">
                Open github.com/login/device ↗
              </a>
              <small className="co-muted">This card updates by itself once you approve.</small>
            </div>
          ) : (
            <div className="co-connect-row">
              <span className="co-muted">
                Uses the GitHub CLI's own browser sign-in; HireZero never sees a password or token.
                {github.state === "failed" && " " + github.error}
              </span>
              <button
                className="co-primary"
                disabled={busy}
                onClick={async () => {
                  setBusy(true);
                  setError("");
                  try {
                    // The GitHub CLI opens the device page in the browser itself; the card shows the code and a link.
                    setGithub(await api<GitHub>("/company/github/sign-in", "POST", {}));
                  } catch (e) {
                    setError((e as Error).message);
                  } finally {
                    setBusy(false);
                  }
                }}
              >
                Sign in to GitHub
              </button>
            </div>
          )}
        </>
      )}
      {!isGitHub && setup && (
        <SetupForm
          setup={setup}
          open
          saved={async () => {
            // Once the saved settings or key make it active, the card closes as connected.
            const all = await api<Setup[]>("/company/integrations/setup");
            const next = all.find((s) => s.id === request.integrationId) ?? null;
            setSetup(next);
            if (next?.active) await resolve();
          }}
        />
      )}
      {!isGitHub && setup === null && (
        <div className="co-connect-row">
          <span className="co-muted">This integration is set up in Company settings.</span>
          <button className="co-primary" onClick={onOpenSetup}>
            Open setup
          </button>
        </div>
      )}
      {error && <p role="alert">{error}</p>}
    </aside>
  );
}
