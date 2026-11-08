import { customerView } from "./audience";
import { useEffect, useRef, useState } from "react";
import { api, company, memberStorage } from "../lib/api";
import { command, reconcileCompanyOperation, UnconfirmedCompanyOperation } from "./api";
import "./decisions.css";

type Summary = { id: string; state: string; model: string; updatedAt: string };
type Listing = {
  connection: { available: boolean; model: string | null; endpoint: string; status: string };
  items: Summary[];
};
type Decision = {
  id: string;
  state: string;
  version: number;
  digest: string;
  updatedAt: string;
  expiresAt: string;
  allowanceCents: number;
  planningTokens: number;
  error: string | null;
  packet: { model: string; json: string; choices: string[] };
  result: { choice: string; confidence: number; probabilities: Record<string, number>; resolvedModel: string } | null;
  reportedUsage: number | null;
  reportedCostUsd: number | null;
  providerReceipt: string | null;
  usage: { inputTokens: number; outputTokens: number; billedUsd: number | null } | null;
};
type View = { decision: Decision; current: boolean };
const labels: Record<string, string> = {
  prepared: "Ready for your approval",
  running: "Decision in progress",
  completed: "Recommendation ready",
  unknown: "Usage needs confirmation",
  withheld: "Recommendation withheld",
  not_sent: "No provider work processed",
  canceled: "Canceled",
  reconciled: "Usage reconciled",
};

export function Decisions({
  refresh,
  connectionRevision,
}: {
  refresh: () => Promise<void>;
  connectionRevision: number;
}) {
  const storage = `hirezero-decision-request-v1:${company() || "local-company"}`;
  const [recovery] = useState(() => {
    try {
      return { id: memberStorage.getItem(storage), error: "" };
    } catch {
      return { id: null, error: "Browser storage is unavailable. Restore it and reload before starting a decision." };
    }
  });
  const [pending, setPending] = useState(recovery.id),
    [error, setError] = useState(recovery.error);
  const [listing, setListing] = useState<Listing | null>(null),
    [view, setView] = useState<View | null>(null);
  const [busy, setBusy] = useState(false),
    [model, setModel] = useState("");
  const [question, setQuestion] = useState(""),
    [situation, setSituation] = useState("");
  const [options, setOptions] = useState(["", ""]),
    [allowance, setAllowance] = useState("1");
  const [reviewed, setReviewed] = useState(false),
    [softLimits, setSoftLimits] = useState(false);
  const [receipt, setReceipt] = useState(""),
    [input, setInput] = useState(""),
    [output, setOutput] = useState("");
  const [cost, setCost] = useState(""),
    [evidence, setEvidence] = useState(""),
    [stopped, setStopped] = useState(false);
  const lock = useRef(false),
    generation = useRef(0),
    selected = useRef("");
  const d = view?.decision;
  const vultr = listing?.connection.model?.startsWith("vultr/") === true;
  const reviewedVultr = d?.packet.model.startsWith("vultr/") === true;
  const disabled = busy || !!pending || !!recovery.error;
  function remember(id: string | null) {
    if (id) {
      memberStorage.setItem(storage, id);
      if (memberStorage.getItem(storage) !== id)
        throw new Error("The request receipt could not be saved. Nothing was sent.");
    } else memberStorage.removeItem(storage);
    setPending(id);
  }
  async function load(id?: string) {
    const turn = ++generation.current;
    const next = await api<Listing>("/company/decisions");
    const chosen = id || selected.current || next.items[0]?.id;
    const result = chosen ? await api<View>(`/company/decisions/${encodeURIComponent(chosen)}`) : null;
    if (turn !== generation.current) return;
    selected.current = chosen || "";
    setListing(next);
    setView(result);
    setModel((previous) => previous || next.connection.model || "");
  }
  useEffect(() => {
    void load().catch((e) => setError((e as Error).message));
    return () => {
      generation.current++;
    };
  }, [connectionRevision]);
  useEffect(() => {
    setReviewed(false);
    setSoftLimits(false);
    setStopped(false);
    setEvidence("");
    setReceipt(d?.providerReceipt || "");
    setInput(d?.usage ? String(d.usage.inputTokens) : "");
    setOutput(d?.usage ? String(d.usage.outputTokens) : "");
    setCost(d?.reportedCostUsd != null ? String(d.reportedCostUsd) : "");
  }, [d?.id, d?.version]);
  async function reload(id?: string) {
    if (lock.current) return;
    lock.current = true;
    setBusy(true);
    setError(recovery.error);
    try {
      await load(id);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      lock.current = false;
      setBusy(false);
    }
  }
  async function perform(action?: string, fields: Record<string, unknown> = {}) {
    if (lock.current || recovery.error) return;
    lock.current = true;
    setBusy(true);
    setError("");
    setReviewed(false);
    setSoftLimits(false);
    const saved = pending;
    try {
      const result = (
        saved ? await reconcileCompanyOperation(saved) : await command(action!, fields, remember)
      ) as Decision;
      remember(null);
      await load(result.id);
      await refresh();
    } catch (e) {
      if (!(e instanceof UnconfirmedCompanyOperation) && !saved) {
        try {
          remember(null);
        } catch {
          /* Keep the receipt visible until storage recovers. */
        }
      }
      setError(
        e instanceof UnconfirmedCompanyOperation
          ? "The response was lost. Check the saved request; HireZero will not send it again."
          : (e as Error).message,
      );
    } finally {
      lock.current = false;
      setBusy(false);
    }
  }
  const exact = d ? { id: d.id, expectedVersion: d.version, digest: d.digest } : {};
  const packet = d ? JSON.parse(d.packet.json) : null;
  const criteria: Record<string, string> = packet?.questions?.answer.criteria || packet?.criteria || {};
  const state = packet?.state ?? (packet?.stateJson ? JSON.parse(packet.stateJson) : null);
  const context =
    typeof state === "string"
      ? state
      : state && Object.keys(state).length === 1 && typeof state.context === "string"
        ? state.context
        : JSON.stringify(state, null, 2);
  const numeric =
    [input, output, cost].every((n) => n.trim() !== "" && Number.isFinite(Number(n)) && Number(n) >= 0) &&
    Number.isSafeInteger(Number(input)) &&
    Number.isSafeInteger(Number(output));
  return (
    <section
      className="co-decisions"
      id="company-jev-decisions"
      tabIndex={-1}
      aria-label={vultr ? "Decisions" : "Jev decisions"}
    >
      <h3>{vultr ? "Decisions" : "Jev decisions"}</h3>
      <p>
        Compare a few concrete options using the context you provide. The model returns advice for your review; choosing
        an option here does not start that work.
      </p>
      <p className="co-muted">
        {vultr
          ? "Uses the configured Vultr model and spending cap."
          : "Uses the saved OpenRouter account. Your text jobs keep their selected model and account."}
      </p>
      {error && (
        <p role="alert" className="co-error">
          {error}
        </p>
      )}
      {pending && (
        <div role="status">
          <p>A saved request needs checking before another can start.</p>
          <button disabled={busy} onClick={() => void perform()}>
            Check saved decision request
          </button>
        </div>
      )}
      <div className="co-decision-controls">
        <button disabled={busy} onClick={() => void reload()}>
          Refresh decisions
        </button>
        {!!listing?.items.length && (
          <label>
            Saved decision
            <select value={d?.id || ""} disabled={disabled} onChange={(e) => void reload(e.target.value)}>
              {listing.items.map((item) => (
                <option key={item.id} value={item.id}>
                  {new Date(item.updatedAt).toLocaleString()} · {labels[item.state] || item.state}
                </option>
              ))}
            </select>
          </label>
        )}
      </div>
      {!listing && !error && <p role="status">Loading decisions…</p>}
      {listing && (
        <details open={!d}>
          <summary>Prepare a decision</summary>
          <p>{listing.connection.status}</p>
          <fieldset disabled={disabled || !listing.connection.available}>
            <label>
              {vultr ? "Model" : "Versioned Jev model"}
              <input
                value={model}
                readOnly={vultr}
                maxLength={192}
                placeholder="typesafe/jev-<major>.<minor>"
                onChange={(e) => setModel(e.target.value)}
              />
            </label>
            <p className="co-muted">
              {vultr
                ? "Each request uses the configured Vultr model."
                : "Use a versioned decision model available on your OpenRouter account. Router models are not supported."}
            </p>
            <label>
              What needs deciding?
              <textarea
                value={question}
                maxLength={1000}
                onChange={(e) => setQuestion(e.target.value)}
                placeholder="Which next step best addresses this client’s actual request?"
              />
            </label>
            <label>
              Situation and relevant facts
              <textarea
                value={situation}
                maxLength={7000}
                onChange={(e) => setSituation(e.target.value)}
                placeholder="Include the facts, constraints and tradeoffs the model needs. Only this text and your options will be sent."
              />
            </label>
            {options.map((option, index) => (
              <label key={index}>
                Option {index + 1}
                <input
                  value={option}
                  maxLength={300}
                  onChange={(e) => setOptions(options.map((v, i) => (i === index ? e.target.value : v)))}
                />
              </label>
            ))}
            <div className="co-decision-controls">
              <button disabled={options.length >= 8} onClick={() => setOptions([...options, ""])}>
                Add option
              </button>
              {options.length > 2 && (
                <button onClick={() => setOptions(options.slice(0, -1))}>Remove last option</button>
              )}
            </div>
            <label>
              {vultr ? "Spend cap (US cents)" : "Intended allowance (US cents)"}
              <input
                type="number"
                min="0.01"
                max="100"
                step="0.01"
                value={allowance}
                onChange={(e) => setAllowance(e.target.value)}
              />
            </label>
            <button
              disabled={
                !model.trim() ||
                !question.trim() ||
                !situation.trim() ||
                options.some((v) => !v.trim()) ||
                !(Number(allowance) >= 0.01 && Number(allowance) <= 100)
              }
              onClick={() =>
                void perform("decision.prepare", {
                  creditAllowance: Number(allowance),
                  body: JSON.stringify({
                    model: model.trim(),
                    instructions: question.trim(),
                    stateJson: JSON.stringify({ context: situation }),
                    criteria: Object.fromEntries(options.map((value, index) => [`option-${index + 1}`, value])),
                  }),
                })
              }
            >
              Review decision request
            </button>
            <p className="co-muted">Preparing a review does not call a model or spend an allowance.</p>
          </fieldset>
        </details>
      )}
      {d && (
        <article aria-label="Saved decision review">
          <h4>{labels[d.state] || d.state}</h4>
          <p>
            <strong>{d.packet.model}</strong>
            {reviewedVultr ? " through Vultr" : " through OpenRouter \u00b7 Company API account"}
          </p>
          <p>
            {reviewedVultr ? "Spend cap" : "Intended allowance"}: {d.allowanceCents} US cents. Planning reservation:{" "}
            {d.planningTokens.toLocaleString()} tokens. Local deadline: {reviewedVultr ? 360 : 30} seconds.
          </p>
          {d.error && <p role="status">{d.error}</p>}
          <div className="co-decision-context" aria-label="Decision inputs">
            <p>
              <strong>Question</strong>
              <br />
              {packet.questions?.answer.instructions ?? packet.instructions}
            </p>
            <p>
              <strong>Context</strong>
            </p>
            <p className="co-decision-situation">{context}</p>
            <p>
              <strong>Options</strong>
            </p>
            <ol>
              {Object.entries(criteria).map(([key, value]) => (
                <li key={key}>{value}</li>
              ))}
            </ol>
          </div>
          <details>
            <summary>{reviewedVultr ? "Reviewed decision data" : "Exact data sent to OpenRouter"}</summary>
            <p>{listing?.connection.endpoint}</p>
            <pre>{JSON.stringify(JSON.parse(d.packet.json), null, 2)}</pre>
          </details>
          {d.state === "prepared" && (
            <>
              <p>
                Review expires {new Date(d.expiresAt).toLocaleString()}.{" "}
                {!view?.current && "The review or connection is no longer current. Prepare a fresh request."}
              </p>
              <label className="co-decision-check">
                <input
                  type="checkbox"
                  checked={reviewed}
                  disabled={disabled}
                  onChange={(e) => setReviewed(e.target.checked)}
                />
                I reviewed the context, question and options sent to the model.
              </label>
              <label className="co-decision-check">
                <input
                  type="checkbox"
                  checked={softLimits}
                  disabled={disabled}
                  onChange={(e) => setSoftLimits(e.target.checked)}
                />
                {reviewedVultr
                  ? "I authorize one request within this spend cap. An uncertain request will not be retried."
                  : "I authorize one request. The allowance, token reservation and local deadline are soft limits; the provider may continue or charge more. An uncertain request will not be retried."}
              </label>
              <div className="co-decision-controls">
                <button
                  disabled={disabled || !view?.current || !reviewed || !softLimits}
                  onClick={() => void perform("decision.run", { ...exact, decision: "accept-soft-limits" })}
                >
                  Run this decision once
                </button>
                <button disabled={disabled} onClick={() => void perform("decision.cancel", exact)}>
                  Cancel unstarted decision
                </button>
              </div>
            </>
          )}
          {d.result && (
            <>
              <p>
                <strong>Recommendation: {criteria[d.result.choice] || d.result.choice}</strong>
              </p>
              <p>
                Provider confidence: {(d.result.confidence * 100).toFixed(1)}%. This is a model estimate, not a measured
                chance of success.
              </p>
              <ul>
                {Object.entries(d.result.probabilities).map(([key, value]) => (
                  <li key={key}>
                    {criteria[key] || key}: {(value * 100).toFixed(1)}%
                  </li>
                ))}
              </ul>
              <p>No downstream task or approval was created.</p>
            </>
          )}
          {d.state !== "prepared" && d.state !== "canceled" && (
            <p>
              Reported usage: {d.reportedUsage == null ? "unknown" : `${d.reportedUsage.toLocaleString()} tokens`}
              {!customerView() &&
                ` · Reported charge: ${d.reportedCostUsd == null ? "unknown" : `$${d.reportedCostUsd.toFixed(6)}`}`}
              {d.providerReceipt && ` · Receipt: ${d.providerReceipt}`}
            </p>
          )}
          {d.state === "unknown" && (
            <details>
              <summary>Reconcile provider usage</summary>
              <p>
                Check the provider’s records first. Enter complete usage and confirm the original request has stopped.
                Reconciliation records the charge and releases the reservation; it does not rerun the request or accept
                advice.
              </p>
              <fieldset disabled={disabled}>
                <label>
                  Provider receipt
                  <input value={receipt} maxLength={240} onChange={(e) => setReceipt(e.target.value)} />
                </label>
                <label>
                  Input tokens
                  <input type="number" min="0" step="1" value={input} onChange={(e) => setInput(e.target.value)} />
                </label>
                <label>
                  Output tokens
                  <input type="number" min="0" step="1" value={output} onChange={(e) => setOutput(e.target.value)} />
                </label>
                <label>
                  Actual charge (USD)
                  <input type="number" min="0" step="any" value={cost} onChange={(e) => setCost(e.target.value)} />
                </label>
                <label>
                  Evidence checked
                  <textarea value={evidence} maxLength={500} onChange={(e) => setEvidence(e.target.value)} />
                </label>
                <label className="co-decision-check">
                  <input type="checkbox" checked={stopped} onChange={(e) => setStopped(e.target.checked)} />I confirmed
                  the provider stopped and these receipt-bound usage figures are complete.
                </label>
                <button
                  disabled={!stopped || !numeric || !receipt.trim() || !evidence.trim()}
                  onClick={() =>
                    void perform("decision.reconcile", {
                      ...exact,
                      decision: "confirmed-usage-and-termination",
                      body: JSON.stringify({
                        receipt: receipt.trim(),
                        usage: { inputTokens: Number(input), outputTokens: Number(output), billedUsd: Number(cost) },
                        providerStopped: stopped,
                        evidence: evidence.trim(),
                      }),
                    })
                  }
                >
                  Record confirmed usage
                </button>
              </fieldset>
            </details>
          )}
        </article>
      )}
    </section>
  );
}
