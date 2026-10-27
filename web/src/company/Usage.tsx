import { memberStorage as sessionStorage } from "../lib/api";
import { useEffect, useRef, useState } from "react";
import { api, company } from "../lib/api";
import { command, reconcileCompanyOperation, UnconfirmedCompanyOperation } from "./api";
import { dollars } from "./money";
import "./usage.css";
import { Compute } from "./Compute";

type Limits = { jobs: number; tokens: number; paused: boolean };
type Policy = { limits: Limits; version: number };
export type UsageSummary = {
  period: string;
  resetsAt: string;
  jobs: number;
  tokens: number;
  heldJobs: number;
  heldTokens: number;
  carriedJobs: number;
  carriedTokens: number;
  reportedUsd: number;
  estimatedUsd: number;
  unpricedJobs: number;
  provisionalCosts: number;
  companyKeyReportedUsd?: number;
  companyKeyEstimatedUsd?: number;
};
export type UsageAllowance = {
  policy: Policy;
  current: UsageSummary;
  subscriptionRequired?: boolean;
  subscriptionAllowance?: { jobs: number; tokens: number } | null;
};
type Entry = {
  id: string;
  kind: string;
  taskId: string | null;
  employeeId: string | null;
  route: string;
  startedAt: string | null;
  state: string;
  outcome: string;
  actualTokens: number | null;
  observedTokens: number | null;
  planningTokens: number;
  reportedUsd: number | null;
  estimatedUsd: number | null;
  settledAt: string | null;
  receipt: string | null;
  funding?: { source: string; provider: string } | null;
};
type Data = UsageAllowance & {
  selected: UsageSummary;
  entries: Entry[];
  nextBefore: string | null;
  periods: string[];
  earlierJobs: number;
};
const number = (value: number) => value.toLocaleString();
export function MonthlyAllowance({ usage }: { usage: UsageAllowance }) {
  const { current: s, policy: p } = usage;
  const jobs = Math.min(
    p.limits.jobs,
    usage.subscriptionRequired ? (usage.subscriptionAllowance?.jobs ?? 0) : Infinity,
  );
  const tokens = Math.min(
    p.limits.tokens,
    usage.subscriptionRequired ? (usage.subscriptionAllowance?.tokens ?? 0) : Infinity,
  );
  return (
    <div className="co-usage-allowance">
      <strong>{p.limits.paused ? "New AI jobs are paused" : "Monthly allowance"}</strong>
      <p>
        {number(Math.max(0, jobs - s.jobs - s.carriedJobs))} jobs and{" "}
        {number(Math.max(0, tokens - s.tokens - s.heldTokens - s.carriedTokens))} planning tokens remain. Resets{" "}
        {new Date(s.resetsAt).toLocaleDateString(undefined, { timeZone: "UTC" })} at 00:00 UTC.
      </p>
      {usage.subscriptionRequired && (
        <p>
          {usage.subscriptionAllowance
            ? "The lower of your purchased allowance and safety limits applies."
            : "A verified subscription allowance is required for new AI work. Open Billing to review payment status."}
        </p>
      )}
      {s.carriedJobs > 0 && (
        <p>
          {s.carriedJobs} earlier {s.carriedJobs === 1 ? "job still holds" : "jobs still hold"}{" "}
          {number(s.carriedTokens)} tokens. A new month does not clear unresolved work.
        </p>
      )}
    </div>
  );
}
export function Usage({
  role,
  onBilling,
  employees,
  onOpenWork,
}: {
  role?: string;
  onBilling?: () => void;
  employees: { id: string; name: string }[];
  onOpenWork: (id: string) => Promise<void>;
}) {
  const receiptKey = `hirezero-usage-receipt-v1:${company() || "local-company"}`;
  const [pending, setPending] = useState(() => {
    try {
      return sessionStorage.getItem(receiptKey) || "";
    } catch {
      return "";
    }
  });
  const [data, setData] = useState<Data | null>(null),
    [period, setPeriod] = useState(""),
    [before, setBefore] = useState<string | null>(null);
  const [limits, setLimits] = useState<Limits | null>(null),
    [version, setVersion] = useState(0),
    [dirty, setDirty] = useState(false),
    [consent, setConsent] = useState(false);
  const [error, setError] = useState(""),
    [notice, setNotice] = useState(""),
    [busy, setBusy] = useState(false);
  const generation = useRef(0),
    lock = useRef(false),
    dirtyRef = useRef(false);
  const owner = !role || role === "owner";
  function remember(id: string) {
    setPending(id);
    try {
      id ? sessionStorage.setItem(receiptKey, id) : sessionStorage.removeItem(receiptKey);
    } catch {
      setError("This browser cannot retain the request receipt. Keep this page open until its result is confirmed.");
    }
  }
  async function load() {
    const request = ++generation.current;
    const query = new URLSearchParams();
    if (period) query.set("period", period);
    if (before) query.set("before", before);
    const next = await api<Data>("/company/usage?" + query);
    if (request !== generation.current) return;
    setData(next);
    if (!dirtyRef.current) {
      setLimits(next.policy.limits);
      setVersion(next.policy.version);
      setConsent(false);
    }
  }
  useEffect(() => {
    void load().catch((e) => setError((e as Error).message));
    return () => {
      generation.current++;
    };
  }, [period, before]);
  function edit(next: Limits) {
    setLimits(next);
    dirtyRef.current = true;
    setDirty(true);
    setConsent(false);
    setNotice("");
  }
  async function save() {
    if (lock.current || !limits || (!pending && !consent)) return;
    lock.current = true;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      pending
        ? await reconcileCompanyOperation(pending)
        : await command("usage.configure", { expectedVersion: version, body: JSON.stringify(limits) }, remember);
      remember("");
      dirtyRef.current = false;
      setDirty(false);
      setConsent(false);
      setNotice("Monthly limits saved. Existing usage and in-flight work remain recorded.");
      try {
        await load();
      } catch (e) {
        setError("Saved, but the usage view could not refresh. " + (e as Error).message);
      }
    } catch (e) {
      if (!(e instanceof UnconfirmedCompanyOperation) && !pending) remember("");
      setError((e as Error).message);
    } finally {
      lock.current = false;
      setBusy(false);
    }
  }
  const selected = data?.selected;
  return (
    <section className="co-usage" aria-label="Company usage">
      <header>
        <p className="co-eyebrow">Keep the work within reach</p>
        <h2>Usage and monthly limits</h2>
        <p>See what your AI jobs used, what is still reserved, and the costs supported by their receipts.</p>
      </header>
      {error && (
        <p className="co-error" role="alert">
          {error}
        </p>
      )}
      {notice && <p role="status">{notice}</p>}
      {pending && (
        <div role="status">
          <p>{busy ? "Saving limits…" : "The outcome needs confirmation before another change."}</p>
          <button disabled={busy} onClick={() => void save()}>
            Check usage receipt
          </button>
        </div>
      )}
      <button
        disabled={busy}
        onClick={() => {
          setError("");
          void load().catch((e) => setError((e as Error).message));
        }}
      >
        Refresh usage
      </button>
      {!data ? (
        <p role="status">Loading usage…</p>
      ) : (
        <>
          <MonthlyAllowance usage={data} />
          {data.subscriptionRequired && onBilling && <button onClick={onBilling}>Review billing</button>}
          {owner && limits && (
            <details className="co-usage-settings">
              <summary>Monthly safety limits</summary>
              <p>
                These limits apply to all new employee, recurring, developer and business-profile AI jobs in this
                company. Changing them does not purchase credits, authorize work or reset existing usage.
              </p>
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  void save();
                }}
              >
                <fieldset disabled={busy || !!pending}>
                  <label>
                    AI jobs per UTC month
                    <input
                      required
                      type="number"
                      min={0}
                      max={10000}
                      step={1}
                      value={limits.jobs}
                      onChange={(e) => edit({ ...limits, jobs: Number(e.target.value) })}
                    />
                  </label>
                  <label>
                    Planning tokens per UTC month
                    <input
                      required
                      type="number"
                      min={0}
                      max={1000000000}
                      step={1}
                      value={limits.tokens}
                      onChange={(e) => edit({ ...limits, tokens: Number(e.target.value) })}
                    />
                  </label>
                  <label className="co-usage-check">
                    <input
                      type="checkbox"
                      checked={limits.paused}
                      onChange={(e) => edit({ ...limits, paused: e.target.checked })}
                    />{" "}
                    Pause new AI jobs
                  </label>
                  <p>
                    Already authorized jobs can finish. Lowering a limit below usage prevents further jobs; it does not
                    remove receipts. Other task and cumulative safety limits still apply.
                  </p>
                  {dirty && data.policy.version !== version && (
                    <p role="alert">Another owner changed these limits. Reload the current limits before saving.</p>
                  )}
                  <label className="co-usage-check">
                    <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} /> I
                    reviewed these monthly limits and their effect on new work.
                  </label>
                  <div className="co-usage-actions">
                    <button className="co-primary" disabled={!consent || data.policy.version !== version} type="submit">
                      Save monthly limits
                    </button>
                    <button
                      type="button"
                      onClick={() => {
                        dirtyRef.current = false;
                        setDirty(false);
                        setLimits(data.policy.limits);
                        setVersion(data.policy.version);
                        setConsent(false);
                      }}
                    >
                      Reload current limits
                    </button>
                  </div>
                </fieldset>
              </form>
            </details>
          )}
          <label>
            Usage period
            <select
              value={period || data.current.period}
              onChange={(e) => {
                setPeriod(e.target.value);
                setBefore(null);
                setError("");
              }}
            >
              {data.periods.map((p) => (
                <option key={p} value={p}>
                  {p === "earlier" ? "Earlier jobs — date unavailable" : p}
                </option>
              ))}
            </select>
          </label>
          {data.earlierJobs > 0 && (
            <p>
              {data.earlierJobs} earlier {data.earlierJobs === 1 ? "job has" : "jobs have"} no reliable dispatch date.
              Their receipts remain in Earlier jobs; unresolved reservations still reduce this month's allowance.
            </p>
          )}
          {selected && (
            <>
              <div className="co-usage-cards">
                <article>
                  <span>Jobs authorized</span>
                  <strong>{number(selected.jobs)}</strong>
                  <small>Includes failed, canceled and unfinished requests after admission.</small>
                </article>
                <article>
                  <span>Settled tokens</span>
                  <strong>{number(selected.tokens)}</strong>
                  <small>
                    {number(selected.heldTokens)} held across {number(selected.heldJobs)} unfinished jobs.
                  </small>
                </article>
                <article>
                  <span>Provider-reported cost</span>
                  <strong>{dollars(selected.reportedUsd)}</strong>
                  <small>From settled provider receipts.</small>
                </article>
                <article>
                  <span>Additional estimated cost</span>
                  <strong>{dollars(selected.estimatedUsd)}</strong>
                  <small>From prices captured at each job's admission.</small>
                </article>
              </div>
              <p>
                {selected.unpricedJobs} settled {selected.unpricedJobs === 1 ? "job has" : "jobs have"} no known dollar
                cost.
                {selected.provisionalCosts > 0 &&
                  ` ${selected.provisionalCosts} unfinished jobs have provisional cost observations excluded from these totals.`}{" "}
                Costs shown cover AI provider usage. They exclude subscription fees, tools, storage and hosting. Company
                API keys account for {dollars(selected.companyKeyReportedUsd ?? 0)} reported and{" "}
                {dollars(selected.companyKeyEstimatedUsd ?? 0)} estimated within these totals. Those charges are paid
                directly to your provider.
              </p>
            </>
          )}
          {data.entries.length === 0 ? (
            <p>No AI jobs recorded for this period. Preparing a request uses no monthly allowance.</p>
          ) : (
            <ol className="co-usage-entries">
              {data.entries.map((entry) => (
                <li key={entry.id}>
                  <header>
                    <strong>
                      {entry.kind === "profile-draft"
                        ? "Business profile draft"
                        : entry.kind === "memory-analysis"
                          ? "Memory check"
                          : employees.find((e) => e.id === entry.employeeId)?.name || "Earlier employee job"}
                    </strong>
                    <span>{entry.state === "settled" ? "Settled" : "Reserved — outcome pending"}</span>
                  </header>
                  <p>
                    {entry.startedAt ? new Date(entry.startedAt).toLocaleString() : "Dispatch date unavailable"} ·{" "}
                    {entry.route}
                  </p>
                  <p>
                    {entry.funding?.source === "company-key"
                      ? `Company API key · ${entry.funding.provider} charges your account directly`
                      : entry.funding
                        ? "Host connection"
                        : "Funding source not recorded for this older job"}
                  </p>
                  <p>
                    {entry.state === "settled"
                      ? `${number(entry.actualTokens || 0)} tokens`
                      : `${number(Math.max(entry.planningTokens, entry.observedTokens || 0))} tokens held`}
                    {entry.reportedUsd !== null
                      ? ` · ${dollars(entry.reportedUsd)} provider-reported`
                      : entry.estimatedUsd !== null
                        ? ` · ${dollars(entry.estimatedUsd)} estimated`
                        : " · Dollar cost unknown"}
                  </p>
                  <details>
                    <summary>Usage receipt</summary>
                    <p>Outcome: {entry.outcome.replaceAll("_", " ")}</p>
                    <p>Receipt: {entry.receipt || "No provider receipt received"}</p>
                    <p>Settled: {entry.settledAt ? new Date(entry.settledAt).toLocaleString() : "Not recorded"}</p>
                  </details>
                  {entry.taskId && (
                    <button onClick={() => void onOpenWork(entry.taskId!).catch((e) => setError((e as Error).message))}>
                      Open work
                    </button>
                  )}
                </li>
              ))}
            </ol>
          )}
          <div className="co-usage-actions">
            {before && <button onClick={() => setBefore(null)}>Latest usage</button>}
            {data.nextBefore && <button onClick={() => setBefore(data.nextBefore)}>Older usage</button>}
          </div>
        </>
      )}
      <Compute role={role} onOpenWork={onOpenWork} />
    </section>
  );
}
