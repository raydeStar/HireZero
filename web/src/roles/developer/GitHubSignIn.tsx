import { useCallback, useEffect, useState } from "react";
import { ExternalLink } from "lucide-react";
import { api } from "../../shell/api";

type Health = { signedIn: boolean; since: string | null; reason: string | null };
type Reconnect = {
  state: "idle" | "starting" | "waiting" | "connected" | "failed";
  code: string;
  url: string;
  error: string;
};

/** Shown only when GitHub signs out: explains what paused and walks through GitHub's own browser sign-in. */
export function GitHubSignIn() {
  const [health, setHealth] = useState<Health | null>(null),
    [flow, setFlow] = useState<Reconnect | null>(null),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const check = useCallback(async () => {
    try {
      setHealth(await api<Health>("/github/health"));
    } catch {
      /* the role may be loading */
    }
  }, []);
  useEffect(() => {
    void check();
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") void check();
    }, 30000);
    return () => clearInterval(timer);
  }, [check]);
  useEffect(() => {
    if (flow?.state !== "waiting" && flow?.state !== "starting") return;
    const timer = setInterval(
      () =>
        void api<Reconnect>("/github/reconnect")
          .then((value) => {
            setFlow(value);
            if (value.state === "connected") void api("/github/connection").finally(() => void check());
          })
          .catch(() => {}),
      3000,
    );
    return () => clearInterval(timer);
  }, [flow?.state, check]);
  async function start() {
    setBusy(true);
    setError("");
    try {
      setFlow(await api<Reconnect>("/github/reconnect", "POST", {}));
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function recheck() {
    setBusy(true);
    setError("");
    try {
      await api("/github/connection");
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      await check();
      setBusy(false);
    }
  }
  if (!health || (health.signedIn && flow?.state !== "connected")) return null;
  if (health.signedIn)
    return (
      <section className="fe-cockpit-section dev-github-signin ok" role="status">
        <h3>GitHub reconnected</h3>
        <p className="fe-muted">Paused PR work picks up where it left off.</p>
      </section>
    );
  return (
    <section className="fe-cockpit-section dev-github-signin" role="alert" aria-label="GitHub sign-in">
      <h3>GitHub needs you to sign in</h3>
      <p>GitHub stopped accepting this computer's sign-in, so PR work is paused. Nothing was lost.</p>
      {flow?.state === "waiting" && flow.code ? (
        <div className="fe-stack">
          <p>Enter this code on GitHub to approve HireZero's sign-in:</p>
          <p className="dev-github-code" aria-label="One-time code">
            {flow.code}
          </p>
          <a className="fe-link" href={flow.url} target="_blank" rel="noopener noreferrer">
            Open github.com/login/device <ExternalLink size={12} />
          </a>
          <p className="fe-muted" role="status">
            Waiting for you to approve it on GitHub…
          </p>
        </div>
      ) : flow?.state === "failed" ? (
        <p className="fe-alert">{flow.error}</p>
      ) : null}
      {error && <p className="fe-alert">{error}</p>}
      <div className="dev-shift-actions">
        {flow?.state !== "waiting" && (
          <button className="primary" disabled={busy} onClick={() => void start()}>
            {busy ? "Starting…" : "Reconnect GitHub"}
          </button>
        )}
        <button className="fe-ghost" disabled={busy} onClick={() => void recheck()}>
          Check again
        </button>
      </div>
      <p className="fe-muted">
        Prefer a terminal? Run <code>gh auth login</code>, then press Check again.
      </p>
    </section>
  );
}
