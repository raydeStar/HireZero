import { useEffect, useRef, useState } from "react";
import { Check, Link2, X } from "lucide-react";
import { api } from "../lib/api";
import { runAction, type ConnectionRequest, type Snapshot } from "./api";
import { SetupForm, type Setup } from "./IntegrationSetup";

type GitHub = { state: string; code: string; url: string; error: string; account?: string | null };

// A Connect card (docs/adr/0046-connect-cards.md): an employee's reply or a refused action asked for an integration,
// and the owner connects it here, in the conversation. GitHub signs in through the GitHub CLI's browser flow; an
// integration with settings or a key shows its own setup form, which stores the key encrypted and never shows it.
// Connecting is recorded by connection.resolve; nothing is connected without the owner.
export function ConnectCard({
  request,
  snapshot,
  run,
  refresh,
  onOpenSetup,
}: {
  request: ConnectionRequest;
  snapshot: Snapshot;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  refresh: () => Promise<void>;
  onOpenSetup: () => void;
}) {
  const [github, setGithub] = useState<GitHub | null>(null);
  const [setup, setSetup] = useState<Setup | null | undefined>(undefined);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const open = request.state === "open";
  // The action that was refused for want of this connection, offered again once it is connected.
  const refused = snapshot.data.tasks.find((t) => t.id === request.taskId && t.state === "failed");
  const [rerun, setRerun] = useState<"idle" | "running" | "started">("idle");
  async function runAgain() {
    if (!refused) return;
    setRerun("running");
    setError("");
    try {
      await runAction(
        crypto.randomUUID(),
        refused.employeeId,
        refused.threadId,
        refused.skill,
        JSON.parse(refused.input || "{}") as Record<string, string>,
      );
      setRerun("started");
      await refresh();
    } catch (e) {
      setError((e as Error).message);
      setRerun("idle");
    }
  }
  const isGitHub = request.integrationId === "github";
  const isMicrosoft = request.integrationId === "microsoft365";

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

  // A sign-in started from this card finishes by itself: once GitHub reports it approved, the connection is recorded
  // without a further click (real-use check, 2026-10-21).
  const signingIn = useRef(false);
  useEffect(() => {
    if (github?.state === "waiting" || github?.state === "starting") signingIn.current = true;
    else if (github?.state === "connected" && signingIn.current && open) {
      signingIn.current = false;
      void resolve(github.account);
    }
  }, [github?.state, github?.account, open]);

  // Other integrations: their setup form, when they have one.
  useEffect(() => {
    if (!open || isGitHub || isMicrosoft) return;
    let current = true;
    api<Setup[]>("/company/integrations/setup")
      .then((all) => current && setSetup(all.find((s) => s.id === request.integrationId) ?? null))
      .catch(() => current && setSetup(null));
    return () => {
      current = false;
    };
  }, [open, isGitHub, isMicrosoft, request.integrationId]);

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
        {refused && rerun !== "started" && (
          <button className="co-primary" disabled={rerun === "running"} onClick={() => void runAgain()}>
            {rerun === "running" ? "Running…" : "Run again"}
          </button>
        )}
        {rerun === "started" && <span className="co-muted"> Ran again; the result is below.</span>}
        {error && <span role="alert"> {error}</span>}
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
      {request.from ? (
        <p>
          <strong>{request.from}</strong> asks: <span className="co-muted">{request.reason}</span>
        </p>
      ) : (
        <p className="co-muted">{request.reason}</p>
      )}
      {/* An assistant pointed at where the key already is (docs/adr/0048): the owner sees the variable's name, never
          its value, and HireZero reads it on this computer only on their yes. */}
      {request.keySource && (
        <div className="co-connect-row">
          <span>
            Use the key in the environment variable <code>{request.keySource.replace(/^env:/, "")}</code> as{" "}
            <code>{request.secretName}</code>?
            {!request.keySource.toLowerCase().includes(request.integrationId.split("-")[0]) && (
              <strong className="co-connect-warning">
                {" "}
                Check this: the variable's name doesn't mention {request.name}.
              </strong>
            )}
          </span>
          <button
            className="co-primary"
            disabled={busy}
            onClick={async () => {
              setBusy(true);
              setError("");
              try {
                const result = await api<{ connected: boolean; reason?: string }>(
                  "/company/integrations/connections/" + request.id + "/use-key-source",
                  "POST",
                  {},
                );
                // The key is saved either way; anything still missing is said here and set in the form below.
                if (!result.connected) setError("Key saved. Still needed: " + (result.reason ?? ""));
                await refresh();
              } catch (e) {
                setError((e as Error).message);
              } finally {
                setBusy(false);
              }
            }}
          >
            Use this key
          </button>
        </div>
      )}
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
      {isMicrosoft && <MicrosoftConnect busy={busy} onConnected={(address) => void resolve(address)} />}
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
      {!isGitHub && !isMicrosoft && setup === null && (
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

type Microsoft = {
  configured: boolean;
  redirectUri: string;
  account: { address: string } | null;
};

// Microsoft 365 in a Connect card: the owner's own Entra app (its client ID, once), then "Sign in with Microsoft"
// opens Microsoft's consent page in a new tab, and the card records the connection once the sign-in lands.
function MicrosoftConnect({ busy, onConnected }: { busy: boolean; onConnected: (address: string) => void }) {
  const [status, setStatus] = useState<Microsoft | null>(null);
  const [clientId, setClientId] = useState("");
  const [waiting, setWaiting] = useState(false);
  const [error, setError] = useState("");
  const load = () =>
    api<Microsoft>("/connections/microsoft")
      .then(setStatus)
      .catch((e: Error) => setError(e.message));
  useEffect(() => {
    void load();
  }, []);
  // While a sign-in is open in another tab, check every few seconds; once it lands, the card connects.
  useEffect(() => {
    if (!waiting) return;
    const timer = window.setInterval(async () => {
      const next = await api<Microsoft>("/connections/microsoft").catch(() => null);
      if (next?.account) {
        window.clearInterval(timer);
        setWaiting(false);
        setStatus(next);
        onConnected(next.account.address);
      }
    }, 3000);
    return () => window.clearInterval(timer);
  }, [waiting]);
  if (!status) return error ? <p role="alert">{error}</p> : null;
  if (status.account)
    return (
      <div className="co-connect-row">
        <span>
          Signed in as <strong>{status.account.address}</strong>.
        </span>
        <button className="co-primary" disabled={busy} onClick={() => onConnected(status.account!.address)}>
          Use this sign-in
        </button>
      </div>
    );
  if (!status.configured)
    return (
      <form
        className="co-connect-code"
        onSubmit={async (e) => {
          e.preventDefault();
          setError("");
          try {
            await api("/connections/microsoft", "PUT", { clientId: clientId.trim() });
            await load();
          } catch (failure) {
            setError((failure as Error).message);
          }
        }}
      >
        <span className="co-muted">
          Once: register an app in Microsoft Entra with <code>{status.redirectUri}</code> as a desktop redirect URI,
          then paste its application (client) ID here.{" "}
          <a
            href="https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade"
            target="_blank"
            rel="noreferrer"
          >
            Open Entra app registrations ↗
          </a>
        </span>
        <input
          aria-label="Application (client) ID"
          placeholder="00000000-0000-0000-0000-000000000000"
          value={clientId}
          onChange={(e) => setClientId(e.target.value)}
        />
        <button className="co-primary" disabled={!clientId.trim()} type="submit">
          Save client ID
        </button>
        {error && <p role="alert">{error}</p>}
      </form>
    );
  return (
    <div className="co-connect-row">
      <span className="co-muted">
        {waiting
          ? "Finish signing in in the new tab; this card updates by itself."
          : "Read-only: nothing is sent, moved or deleted."}
      </span>
      <button
        className="co-primary"
        disabled={busy}
        onClick={async () => {
          // Opened during the click so the browser allows it; pointed at Microsoft once the URL is ready.
          const tab = window.open("", "_blank");
          setError("");
          try {
            const { url } = await api<{ url: string }>("/connections/microsoft/start", "POST", {});
            if (tab) tab.location.href = url;
            else window.location.href = url;
            setWaiting(true);
          } catch (failure) {
            tab?.close();
            setError((failure as Error).message);
          }
        }}
      >
        {waiting ? "Open sign-in again" : "Sign in with Microsoft"}
      </button>
      {error && <p role="alert">{error}</p>}
    </div>
  );
}
