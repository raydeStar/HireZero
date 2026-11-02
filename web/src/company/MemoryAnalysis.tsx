import { memberStorage as sessionStorage } from "../lib/api";
import { useEffect, useRef, useState } from "react";
import { defaultAllowance, SpendLimit, spendText } from "./allowance";
import { api, company } from "../lib/api";
import {
  command,
  reconcileCompanyOperation,
  UnconfirmedCompanyOperation,
  type MeasuredEntry,
  type Snapshot,
} from "./api";

type Source = {
  id: string;
  state: string;
  version: number;
  analysisEligible?: boolean;
  definition: { title: string; statement: string; reviewOn?: string | null };
};
type Summary = {
  id: string;
  state: string;
  version: number;
  updatedAt: string;
  sourceCount: number;
  sourcesCurrent: boolean;
};
type Report = {
  id: string;
  state: string;
  version: number;
  updatedAt: string;
  sources: Source[];
  job: MeasuredEntry["job"];
  call: { instructions: string; context: { input: string } };
  findings:
    | {
        kind: string;
        explanation: string;
        citations: { memoryId: string; quote: string }[];
        revision?: { memoryId: string; statement: string; rationale: string } | null;
      }[]
    | null;
  error: string | null;
  reportedUsage: number | null;
  usage: { inputTokens: number; outputTokens: number } | null;
  providerReceipt: string | null;
};
type View = { analysis: Report; sourcesCurrent: boolean; findingDigests: string[] };
type Pending = { id: string; action: string };
const states: { [state: string]: string } = {
  proposed: "Ready for your authorization",
  running: "Checking selected memory…",
  completed: "Memory report ready",
  unknown: "Provider outcome needs confirmation",
  withheld: "Report withheld",
  invalid: "Response failed evidence checks",
  reconciled: "Provider usage reconciled",
};
const kinds: { [kind: string]: string } = {
  "possible-conflict": "Possible conflict",
  "possible-duplicate": "Possible duplicate",
  "needs-review": "Worth reviewing",
};

export function MemoryAnalysis({
  snapshot,
  records,
  refresh,
  onReview,
  onRevision,
  revisionUnavailable,
}: {
  snapshot: Snapshot;
  records: Source[];
  refresh: () => Promise<void>;
  onReview: (id: string) => void;
  onRevision: (fields: { id: string; expectedVersion: number; digest: string; body: string }) => void;
  revisionUnavailable: boolean;
}) {
  const storage = `hirezero-memory-analysis-v1:${company() || "local-company"}`;
  const [pending, setPending] = useState<Pending | null>(() => {
    try {
      return JSON.parse(sessionStorage.getItem(storage) || "null");
    } catch {
      return null;
    }
  });
  const [selected, setSelected] = useState<string[]>([]),
    [allowance, setAllowance] = useState("");
  const [jobs, setJobs] = useState<Summary[]>([]),
    [view, setView] = useState<View | null>(null);
  const [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [loaded, setLoaded] = useState(false);
  const [reviewPacket, setReviewPacket] = useState(false),
    [ack, setAck] = useState(false);
  const [receipt, setReceipt] = useState(""),
    [input, setInput] = useState(""),
    [output, setOutput] = useState("");
  const [evidence, setEvidence] = useState(""),
    [stopped, setStopped] = useState(false);
  const lock = useRef(false),
    epoch = useRef(0),
    selectedReport = useRef("");
  const report = view?.analysis,
    available = snapshot.capabilities.measuredOneJob;
  const cents = snapshot.capabilities.allowanceUnit === "usd-cents";
  // Included usage has no per-job limit, so its planning default is sent unasked (docs/adr/0177-plain-spend-limits.md).
  const sentAllowance = cents ? allowance || defaultAllowance(true) : defaultAllowance(false);
  const approved = records.filter((r) => r.analysisEligible);
  const selectedCurrent = selected.filter((id) => approved.some((r) => r.id === id));
  const disabled = busy || !!pending;
  function remember(value: Pending | null) {
    // Persist before dispatch: the butler remembers the receipt, never repeats the purchase.
    if (value) sessionStorage.setItem(storage, JSON.stringify(value));
    else {
      try {
        sessionStorage.removeItem(storage);
      } catch {
        /* The server receipt remains authoritative. */
      }
    }
    setPending(value);
  }
  async function load(id?: string) {
    const generation = ++epoch.current;
    if (id) selectedReport.current = id;
    const saved = await api<Summary[]>("/company/memory/analyses");
    const chosen = selectedReport.current || saved[0]?.id;
    const next = chosen ? await api<View>(`/company/memory/analyses/${encodeURIComponent(chosen)}`) : null;
    if (generation !== epoch.current) return;
    setJobs(saved);
    setView(next);
    setLoaded(true);
    selectedReport.current = chosen || "";
  }
  useEffect(() => {
    void load().catch((e) => {
      setError((e as Error).message);
      setLoaded(true);
    });
    return () => {
      epoch.current++;
    };
  }, [records]);
  useEffect(() => {
    setAck(false);
    setReviewPacket(false);
    setReceipt("");
    setInput("");
    setOutput("");
    setEvidence("");
    setStopped(false);
  }, [report?.id, report?.version]);
  async function perform(action?: string, fields: Parameters<typeof command>[1] = {}) {
    if (lock.current) return;
    lock.current = true;
    setBusy(true);
    setError("");
    setAck(false);
    const saved = pending;
    try {
      const result = (
        saved
          ? await reconcileCompanyOperation(saved.id)
          : await command(action!, fields, (id) => remember({ id, action: action! }))
      ) as Report;
      remember(null);
      await refresh();
      await load(result.id);
    } catch (e) {
      if (!(e instanceof UnconfirmedCompanyOperation) && !saved) remember(null);
      setError(
        e instanceof UnconfirmedCompanyOperation
          ? "The request may have completed. Check the saved memory request to recover its outcome."
          : (e as Error).message,
      );
    } finally {
      lock.current = false;
      setBusy(false);
    }
  }
  async function reload(id?: string) {
    if (lock.current) return;
    lock.current = true;
    setBusy(true);
    setError("");
    setAck(false);
    setReviewPacket(false);
    try {
      await load(id);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      lock.current = false;
      setBusy(false);
    }
  }
  return (
    <section className="co-memory-analysis" aria-label="Check memory consistency">
      <h3>Do these records still agree?</h3>
      <p>
        Compare two to eight records you previously approved, including overdue reviews. HireZero can flag possible
        conflicts, duplicates or guidance worth revisiting, with exact quotes for your review. Your saved memory stays
        unchanged.
      </p>
      {error && (
        <p role="alert" className="co-error">
          {error}
        </p>
      )}
      {pending && (
        <div role="status">
          <p>
            {busy
              ? "Waiting for the saved request…"
              : "A request receipt needs checking. Check its saved outcome before starting another."}
          </p>
          <button disabled={busy} onClick={() => void perform()}>
            Check saved memory request
          </button>
        </div>
      )}
      <div className="co-memory-controls">
        <button disabled={busy} onClick={() => void reload()}>
          Refresh memory reports
        </button>
        {jobs.length > 0 && (
          <label>
            Saved memory report
            <select value={report?.id || ""} disabled={disabled} onChange={(e) => void reload(e.target.value)}>
              {jobs.map((j) => (
                <option key={j.id} value={j.id}>
                  {new Date(j.updatedAt).toLocaleString()} · {states[j.state] || j.state}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>
      {!loaded && <p role="status">Loading saved reports…</p>}
      <details className="co-memory-analysis-prepare" open={loaded && !report}>
        <summary>Prepare a memory check</summary>
        {!available && (
          <p>
            A measured model connection is needed.{" "}
            <a href="?view=departments&section=integrations">Open integrations</a>.
          </p>
        )}
        {approved.length < 2 && <p>Approve at least two memory records to compare them.</p>}
        <fieldset disabled={disabled || !available}>
          <legend>Records to compare</legend>
          {approved.map((r) => (
            <label className="co-memory-check" key={r.id}>
              <input
                type="checkbox"
                checked={selectedCurrent.includes(r.id)}
                disabled={!selectedCurrent.includes(r.id) && selectedCurrent.length >= 8}
                onChange={(e) =>
                  setSelected(
                    e.target.checked ? [...selectedCurrent, r.id] : selectedCurrent.filter((id) => id !== r.id),
                  )
                }
              />
              <span>
                {r.definition.title}
                <small>
                  Revision {r.version}
                  {r.definition.reviewOn ? ` · review date ${r.definition.reviewOn}` : ""}
                </small>
              </span>
            </label>
          ))}
          <SpendLimit cents={cents} value={sentAllowance} onChange={setAllowance} />
          <p className="co-muted">
            {cents
              ? "Nothing is sent if the most this could cost is over your spend limit."
              : "Uses your plan's included usage, which has no per-job limit. Its token limits are safeguards, not hard caps."}{" "}
            Preparing a check sends nothing to the model.
          </p>
          <button
            disabled={
              selectedCurrent.length < 2 ||
              !Number.isFinite(Number(sentAllowance)) ||
              Number(sentAllowance) <= 0 ||
              Number(sentAllowance) > 1000
            }
            onClick={() =>
              void perform("memory.analysis.prepare", {
                evidence: selectedCurrent,
                creditAllowance: Number(sentAllowance),
              })
            }
          >
            Prepare memory check
          </button>
        </fieldset>
      </details>
      {report && (
        <article className="co-memory-analysis-report">
          <p role="status">
            <strong>{states[report.state] || report.state}</strong>
          </p>
          <p>
            {report.sources.length} selected records · {new Date(report.updatedAt).toLocaleString()}
          </p>
          {!view.sourcesCurrent && (
            <p role="alert">
              Selected memory has changed or lost approval. This report describes earlier revisions; review the current
              records before acting.
            </p>
          )}
          {report.error && <p role="alert">{report.error}</p>}
          {report.usage && (
            <details>
              <summary>
                Usage receipt · {(report.usage.inputTokens + report.usage.outputTokens).toLocaleString()} tokens
              </summary>
              <p>
                Recorded usage: {report.usage.inputTokens.toLocaleString()} input +{" "}
                {report.usage.outputTokens.toLocaleString()} output tokens
                {report.providerReceipt ? ` · ${report.providerReceipt}` : ""}.
              </p>
            </details>
          )}
          {report.state === "proposed" && (
            <>
              <p>
                {spendText(
                  report.job.terms ? report.job.terms.allowanceUnit === "usd-cents" : cents,
                  report.job.creditAllowance,
                )}{" "}
                · {report.job.policy.localDeadlineSeconds}
                -second local deadline · {report.job.policy.planningTokens.toLocaleString()} planning tokens.
                Authorization expires {new Date(report.job.expiresAt).toLocaleTimeString()}.
              </p>
              <button
                disabled={disabled}
                onClick={() => {
                  setReviewPacket(true);
                  setAck(false);
                }}
              >
                Review memory request
              </button>
              {reviewPacket && (
                <>
                  <details>
                    <summary>Exact selected records and instructions</summary>
                    <pre>{report.call.context.input}</pre>
                    <pre>{report.call.instructions}</pre>
                  </details>
                  <p>Model: {report.job.route}. Only these selected records are sent in this request.</p>
                  <label className="co-memory-check">
                    <input
                      type="checkbox"
                      checked={ack}
                      disabled={disabled}
                      onChange={(e) => setAck(e.target.checked)}
                    />
                    <span>
                      I authorize one check of these records
                      {cents ? " within the shown spend limit." : ", acknowledging the soft limits."}
                    </span>
                  </label>
                  <button
                    className="co-primary"
                    disabled={
                      disabled ||
                      !ack ||
                      !available ||
                      !view.sourcesCurrent ||
                      Date.parse(report.job.expiresAt) <= Date.now()
                    }
                    onClick={() =>
                      void perform("memory.analysis.run", {
                        id: report.id,
                        expectedVersion: report.version,
                        digest: report.job.digest,
                        decision: "accept-soft-limits",
                      })
                    }
                  >
                    Run memory check once
                  </button>
                </>
              )}
              {Date.parse(report.job.expiresAt) <= Date.now() && (
                <p>This authorization expired. Prepare a new check above.</p>
              )}
            </>
          )}
          {report.state === "running" && (
            <p>The request was dispatched once. Refresh to check its result; it will not be sent again.</p>
          )}
          {report.state === "completed" && (
            <>
              <p>
                These are suggestions to investigate. Revise and approve a memory record separately if a change is
                needed.
              </p>
              {report.findings?.length === 0 && (
                <p>
                  No supported issues were reported among these selected records. This is not a guarantee that company
                  memory is complete or consistent.
                </p>
              )}
              {report.findings?.map((finding, i) => (
                <section className="co-memory-finding" key={i}>
                  <h4>{kinds[finding.kind] || finding.kind}</h4>
                  <p>{finding.explanation}</p>
                  {finding.citations.map((citation, j) => {
                    const source = report.sources.find((s) => s.id === citation.memoryId)!;
                    return (
                      <div key={j}>
                        <blockquote>{citation.quote}</blockquote>
                        <p>
                          {source.definition.title} · revision {source.version}
                        </p>
                        <button disabled={busy} onClick={() => onReview(source.id)}>
                          Review {source.definition.title}
                        </button>
                      </div>
                    );
                  })}
                  {finding.revision && (
                    <div className="co-memory-suggestion">
                      <h4>
                        Suggested wording for{" "}
                        {report.sources.find((s) => s.id === finding.revision!.memoryId)?.definition.title}
                      </h4>
                      <p className="co-memory-statement">{finding.revision.statement}</p>
                      <p>{finding.revision.rationale}</p>
                      <p>
                        Open an editable draft with the same audience and citations. The current record stays active
                        until you approve the replacement.
                      </p>
                      {revisionUnavailable && (
                        <p>Save or discard your unfinished memory draft before opening this suggestion.</p>
                      )}
                      <button
                        disabled={disabled || revisionUnavailable || !view.sourcesCurrent || !view.findingDigests[i]}
                        onClick={() =>
                          onRevision({
                            id: report.id,
                            expectedVersion: report.version,
                            digest: view.findingDigests[i],
                            body: JSON.stringify({ finding: i }),
                          })
                        }
                      >
                        Review suggested revision
                      </button>
                    </div>
                  )}
                </section>
              ))}
            </>
          )}
          {report.state === "unknown" && (
            <details>
              <summary>Reconcile provider usage</summary>
              <p>
                The allowance is held until actual usage and provider termination are confirmed. Reconciliation records
                cost; it cannot rerun the check or import a late report.
              </p>
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  void perform("memory.analysis.reconcile", {
                    id: report.id,
                    expectedVersion: report.version,
                    digest: report.job.digest,
                    decision: "confirmed-usage-and-termination",
                    body: JSON.stringify({
                      receipt,
                      usage: { inputTokens: Number(input), outputTokens: Number(output) },
                      evidence,
                      providerStopped: stopped,
                    }),
                  });
                }}
              >
                <fieldset disabled={disabled}>
                  <label>
                    Provider receipt
                    <input required maxLength={160} value={receipt} onChange={(e) => setReceipt(e.target.value)} />
                  </label>
                  <label>
                    Confirmed input tokens
                    <input
                      required
                      type="number"
                      min="0"
                      max="1000000000"
                      step="1"
                      value={input}
                      onChange={(e) => setInput(e.target.value)}
                    />
                  </label>
                  <label>
                    Confirmed output tokens
                    <input
                      required
                      type="number"
                      min="0"
                      max="1000000000"
                      step="1"
                      value={output}
                      onChange={(e) => setOutput(e.target.value)}
                    />
                  </label>
                  <label>
                    How was this verified?
                    <textarea required maxLength={500} value={evidence} onChange={(e) => setEvidence(e.target.value)} />
                  </label>
                  <label className="co-memory-check">
                    <input type="checkbox" checked={stopped} onChange={(e) => setStopped(e.target.checked)} />
                    <span>I verified the provider stopped and this is its complete usage.</span>
                  </label>
                  <button type="submit" disabled={!stopped}>
                    Record memory check usage
                  </button>
                </fieldset>
              </form>
            </details>
          )}
        </article>
      )}
    </section>
  );
}
