import { browserRuntimeMessage } from "./browserStatus";
import { useEffect, useState, type FormEvent } from "react";
import { api } from "../lib/api";
import { command } from "./api";
import { ComputeAllowance, type ComputeUsage } from "./Sandbox";

type Summary = {
  id: string;
  taskId: string;
  name: string;
  state: string;
  kind: "script" | "browser";
  tier?: string | null;
  period: string | null;
  startedAt: string | null;
  allocatedSeconds: number | null;
  heldSeconds: number;
  wallMilliseconds: number | null;
  settledAt: string | null;
};
type View = {
  available: boolean;
  recoveryAvailable: boolean;
  runtime: { status: string; qualifiedUntil: string | null };
  browserAvailable?: boolean;
  browserRecoveryAvailable?: boolean;
  browserRuntime?: { status: string; qualifiedUntil: string | null };
  browserRetention?: { captureDays: number; version: number };
  usage: ComputeUsage;
  jobs: Summary[];
};

function BrowserRetentionControls({
  policy,
  owner,
  onSaved,
}: {
  policy: { captureDays: number; version: number };
  owner: boolean;
  onSaved: () => Promise<void>;
}) {
  const [days, setDays] = useState(policy.captureDays),
    [consent, setConsent] = useState(false),
    [busy, setBusy] = useState(false),
    [message, setMessage] = useState("");
  useEffect(() => {
    setDays(policy.captureDays);
    setConsent(false);
  }, [policy.captureDays, policy.version]);
  async function save(event: FormEvent) {
    event.preventDefault();
    if (busy || !consent) return;
    setBusy(true);
    setMessage("");
    try {
      await command("browser.retention", {
        expectedVersion: policy.version,
        body: JSON.stringify({ captureDays: days }),
        decision: "apply-capture-retention",
      });
      await onSaved();
      setConsent(false);
      setMessage("Browser capture retention saved. Expired captures are removed in batches while the host is running.");
    } catch (e) {
      setMessage((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <details className="co-browser-retention">
      <summary>Saved browser captures</summary>
      <p>
        {policy.captureDays
          ? `Captures are deleted ${policy.captureDays} days after confirmed resource cleanup.`
          : "Captures are kept until an owner deletes them."}
      </p>
      <p>
        Retention removes saved page text, links, screenshots and capture diagnostics. Selected task results and their
        source details, approved itineraries, usage and audit history remain. Downloads, backups and database recovery
        files are not erased. Running or unresolved jobs are never deleted by this policy.
      </p>
      {message && <p role="status">{message}</p>}
      {owner ? (
        <form onSubmit={(event) => void save(event)}>
          <fieldset disabled={busy}>
            <label>
              Keep browser captures
              <select
                value={days}
                onChange={(e) => {
                  setDays(Number(e.target.value));
                  setConsent(false);
                }}
              >
                <option value={0}>Until I delete them</option>
                <option value={7}>7 days after cleanup</option>
                <option value={30}>30 days after cleanup</option>
                <option value={90}>90 days after cleanup</option>
              </select>
            </label>
            <p>
              {days
                ? "Saving starts deletion of captures already past this age. Retention removes up to 20 jobs per minute while the host is running."
                : "Turning expiry off does not restore deleted captures."}
            </p>
            <label className="co-check">
              <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} />I understand
              which records remain and approve this capture retention policy.
            </label>
            <button disabled={!consent || days === policy.captureDays}>Save capture retention</button>
          </fieldset>
        </form>
      ) : (
        <p>Only an owner can change capture retention.</p>
      )}
    </details>
  );
}

const runtimeMessages: Record<string, string> = {
  "not-configured": "An isolated script runtime has not been configured.",
  "operator-disabled": "New scripts are disabled by the operator.",
  checking: "The operator’s script runtime is being checked.",
  "qualification-expired":
    "The runtime’s qualification has expired. An operator must qualify it again before new scripts can run.",
  "qualification-required": "The runtime needs operator qualification before new scripts can run.",
  "engine-unavailable": "The script runtime cannot be reached. New scripts are unavailable.",
  "image-unavailable": "The qualified script worker is unavailable. An operator must restore it.",
  ready: "The qualified script runtime is ready. Each script still needs your exact approval.",
  fixture: "Fictional test runtime: scripts are not actually executed.",
};

export function Compute({ role, onOpenWork }: { role?: string; onOpenWork: (id: string) => Promise<void> }) {
  const [data, setData] = useState<View | null>(null),
    [error, setError] = useState(""),
    [notice, setNotice] = useState("");
  const [limits, setLimits] = useState({ jobs: 20, seconds: 1800, paused: true }),
    [version, setVersion] = useState(0);
  const [busy, setBusy] = useState(false),
    [consent, setConsent] = useState(false);
  async function load(resetForm = true) {
    const next = await api<View>("/company/compute");
    setData(next);
    if (resetForm) {
      setLimits(next.usage.policy.limits);
      setVersion(next.usage.policy.version);
      setConsent(false);
    }
  }
  useEffect(() => {
    void load().catch((e) => setError(e.message));
  }, []);
  useEffect(() => {
    if (data?.runtime.status !== "checking") return;
    const timer = window.setTimeout(() => void load(false).catch((e) => setError(e.message)), 2000);
    return () => window.clearTimeout(timer);
  }, [data]);
  async function save(event: FormEvent) {
    event.preventDefault();
    if (!consent || busy) return;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      await command("compute.configure", { expectedVersion: version, body: JSON.stringify(limits) });
      await load();
      setNotice("Compute limits saved. Each script or browser itinerary still needs its own exact approval.");
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <section className="co-sandbox co-compute" aria-label="Compute usage and limits">
      <h3>Isolated compute usage</h3>
      {error && (
        <p role="alert" className="co-error">
          {error}
        </p>
      )}
      {notice && <p role="status">{notice}</p>}
      {data ? (
        <>
          {(data.runtime.status !== "not-configured" ||
            !data.browserRuntime ||
            data.browserRuntime.status === "not-configured") && (
            <p role="status">{runtimeMessages[data.runtime.status] || "New scripts are unavailable."}</p>
          )}
          {data.browserRuntime && data.browserRuntime.status !== "not-configured" && (
            <p role="status">{browserRuntimeMessage(data.browserRuntime.status)}</p>
          )}
          <button disabled={busy} onClick={() => void load(false).catch((e) => setError(e.message))}>
            Refresh runtime status
          </button>
          {!data.available && !data.browserAvailable && (
            <p>
              Saved records and limits remain available.
              {data.recoveryAvailable || data.browserRecoveryAvailable
                ? " Unfinished jobs can still be checked and recovered."
                : " Recovery requires the original runtime configuration."}
            </p>
          )}
          {data.available && data.runtime.qualifiedUntil && (
            <p>Qualified until {new Date(data.runtime.qualifiedUntil).toLocaleString()}.</p>
          )}
          <ComputeAllowance usage={data.usage} />
          <p>
            Current month: {data.usage.period} UTC. Scripts and browsers share this allowance. Released work uses its
            approved allocation; browser reservations also include 12 seconds for startup and cleanup. These are safety
            limits, not measured CPU time or dollar costs.
          </p>
          <details className="co-compute-settings">
            <summary>Monthly compute limits</summary>
            {!role || role === "owner" ? (
              <form onSubmit={(event) => void save(event)}>
                <fieldset disabled={busy}>
                  <label>
                    Compute jobs per UTC month
                    <input
                      type="number"
                      required
                      min={0}
                      max={1000}
                      value={limits.jobs}
                      onChange={(e) => {
                        setLimits({ ...limits, jobs: Number(e.target.value) });
                        setConsent(false);
                      }}
                    />
                  </label>
                  <label>
                    Allocated compute seconds per UTC month
                    <input
                      type="number"
                      required
                      min={0}
                      max={120000}
                      value={limits.seconds}
                      onChange={(e) => {
                        setLimits({ ...limits, seconds: Number(e.target.value) });
                        setConsent(false);
                      }}
                    />
                  </label>
                  <label className="co-check">
                    <input
                      type="checkbox"
                      checked={limits.paused}
                      onChange={(e) => {
                        setLimits({ ...limits, paused: e.target.checked });
                        setConsent(false);
                      }}
                    />
                    Pause compute execution, including running work
                  </label>
                  <p>Pausing revokes ongoing execution authority. Resource holds remain until cleanup is confirmed.</p>
                  <label className="co-check">
                    <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} />I reviewed
                    these compute limits.
                  </label>
                  <button type="submit" className="co-primary" disabled={!consent || busy}>
                    Save compute limits
                  </button>
                </fieldset>
              </form>
            ) : (
              <p>Only an owner can change compute limits.</p>
            )}
          </details>
          {data.browserRetention && (
            <BrowserRetentionControls
              policy={data.browserRetention}
              owner={!role || role === "owner"}
              onSaved={() => load(false)}
            />
          )}
          <h4>Recent compute work</h4>
          {data.jobs.length === 0 ? (
            <p>
              No compute proposals or executions yet. Script tools appear inside an employee task when a qualified
              runtime is available.
            </p>
          ) : (
            <ol className="co-usage-entries">
              {data.jobs.map((job) => (
                <li key={job.id}>
                  <strong>{job.name}</strong>
                  <p>
                    {job.kind === "browser" ? "Browser" : job.tier ? "Extended script" : "Script"} ·{" "}
                    {job.state.replaceAll("-", " ")} · {job.period || "Not dispatched"} ·{" "}
                    {job.heldSeconds > 0
                      ? `${job.heldSeconds} seconds held`
                      : `${job.allocatedSeconds ?? 0} seconds allocated`}
                  </p>
                  {job.wallMilliseconds !== null && (
                    <small>
                      Observed adapter elapsed time: {(job.wallMilliseconds / 1000).toFixed(2)} s. This is not CPU usage
                      or a bill.
                    </small>
                  )}
                  <button onClick={() => void onOpenWork(job.taskId).catch((e) => setError(e.message))}>
                    Open compute work
                  </button>
                </li>
              ))}
            </ol>
          )}
          <p>Shows the 50 most recently updated compute records.</p>
        </>
      ) : (
        !error && <p>Loading compute usage…</p>
      )}
      <button
        disabled={busy}
        onClick={() =>
          void load()
            .then(() => setError(""))
            .catch((e) => setError(e.message))
        }
      >
        Refresh compute usage
      </button>
    </section>
  );
}
