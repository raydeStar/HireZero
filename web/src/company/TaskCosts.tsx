import { memberStorage as sessionStorage } from "../lib/api";
import { useRef, useState } from "react";
import { api, company } from "../lib/api";
import { command, reconcileCompanyOperation, UnconfirmedCompanyOperation, type Work } from "./api";
import "./task-costs.css";

const categories = {
  compute: "Compute",
  tools: "Tools and external services",
  retrieval: "Retrieval",
  storage: "Storage",
  operations: "Operating overhead",
};
type Form = { costs: Record<string, string>; basis: string };
type Review = {
  id: string;
  taskId: string;
  sequence: number;
  costs: { category: string; usd: number | null }[];
  basis: string;
  evidenceDigest: string;
  digest: string;
  state: string;
  version: number;
  recordedAt: string | null;
  kind: string;
};
type Data = {
  taskId: string;
  taskState: string;
  evidenceDigest: string;
  models: {
    id: string;
    state: string;
    outcome: string;
    reportedUsd: number | null;
    estimatedUsd: number | null;
    funding: string;
  }[];
  compute: {
    id: string;
    kind: string;
    state: string;
    allocatedSeconds: number | null;
    heldSeconds: number;
    wallMilliseconds: number | null;
    released: boolean;
  }[];
  unmeteredAttempts: number;
  unpricedModels: number;
  heldModels: number;
  reportedModelUsd: number;
  estimatedModelUsd: number;
  companyKeyModelUsd: number;
  reviews: Review[];
  reconciliationCurrent: boolean;
  operatingUsd: number | null;
  reconciledTotalUsd: number | null;
  includesModelEstimates: boolean;
};
const money = (value: number) =>
  new Intl.NumberFormat(undefined, {
    style: "currency",
    currency: "USD",
    minimumFractionDigits: 2,
    maximumFractionDigits: 6,
  }).format(value);
const empty = (): Form => ({ costs: Object.fromEntries(Object.keys(categories).map((k) => [k, ""])), basis: "" });
const from = (review?: Review): Form =>
  review
    ? {
        costs: Object.fromEntries(review.costs.map((c) => [c.category, c.usd === null ? "" : String(c.usd)])),
        basis: review.basis,
      }
    : empty();

export function TaskCosts({ task }: { task: Work }) {
  const key = `hirezero-task-cost-v1:${company() || "local-company"}:${task.id}`;
  const [cached] = useState(() => {
    try {
      return JSON.parse(sessionStorage.getItem(key) || "null") as {
        form: Form;
        target: { id: string; version: number } | null;
      } | null;
    } catch {
      return null;
    }
  });
  const [form, setForm] = useState<Form>(cached?.form || empty());
  const [target, setTarget] = useState<{ id: string; version: number } | null>(cached?.target || null);
  const [dirty, setDirty] = useState(!!cached);
  const [pending, setPending] = useState(() => {
    try {
      return sessionStorage.getItem(key + ":receipt") || "";
    } catch {
      return "";
    }
  });
  const [data, setData] = useState<Data | null>(null),
    [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [notice, setNotice] = useState(""),
    [consent, setConsent] = useState(false);
  const lock = useRef(false);
  function remember(id: string) {
    setPending(id);
    try {
      id ? sessionStorage.setItem(key + ":receipt", id) : sessionStorage.removeItem(key + ":receipt");
    } catch {}
  }
  function adopt(next: Data) {
    const review = next.reviews.find((r) => r.state === "draft"),
      recorded = next.reviews.find((r) => r.state === "recorded");
    setForm(from(review || recorded));
    setTarget(review ? { id: review.id, version: review.version } : null);
    setDirty(false);
    setConsent(false);
    try {
      sessionStorage.removeItem(key);
    } catch {}
  }
  async function load(discard = false) {
    const next = await api<Data>(`/company/tasks/${encodeURIComponent(task.id)}/costs`);
    setData(next);
    if (discard || !dirty) adopt(next);
    return next;
  }
  function edit(next: Form) {
    setForm(next);
    setDirty(true);
    setConsent(false);
    setNotice("");
    try {
      sessionStorage.setItem(key, JSON.stringify({ form: next, target }));
    } catch {}
  }
  async function perform(action?: string, fields: Record<string, unknown> = {}) {
    if (lock.current) return;
    lock.current = true;
    setBusy(true);
    setError("");
    setNotice("");
    let confirmed = false;
    try {
      const saved = (
        pending ? await reconcileCompanyOperation(pending) : await command(action!, fields, remember)
      ) as Review;
      if (saved.kind !== "task-cost-review" || saved.taskId !== task.id)
        throw new Error("The recovered receipt belongs to other work. Refresh this task before making a change.");
      confirmed = true;
      const next = await api<Data>(`/company/tasks/${encodeURIComponent(task.id)}/costs`);
      setData(next);
      adopt(next);
      remember("");
      setNotice(
        saved.state === "recorded"
          ? "Task costs recorded. No customer was charged."
          : saved.state === "discarded"
            ? "Cost draft discarded."
            : "Cost draft saved. Review it before recording.",
      );
    } catch (e) {
      if (e instanceof UnconfirmedCompanyOperation) remember(e.requestId);
      else if (!pending && !confirmed) remember("");
      setError((e as Error).message);
    } finally {
      lock.current = false;
      setBusy(false);
    }
  }
  async function refresh() {
    if (lock.current) return;
    lock.current = true;
    setBusy(true);
    setError("");
    try {
      await load();
      setConsent(false);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      lock.current = false;
      setBusy(false);
    }
  }
  const draft = data?.reviews.find((r) => r.state === "draft");
  const unknown = (data?.unmeteredAttempts || 0) + (data?.unpricedModels || 0);
  const held = (data?.heldModels || 0) + (data?.compute.filter((j) => !j.released).length || 0);
  const canRecord =
    data?.taskState === "completed" &&
    draft &&
    draft.evidenceDigest === data.evidenceDigest &&
    !dirty &&
    held === 0 &&
    draft.costs.every((c) => c.usd !== null) &&
    draft.basis.length >= 10;
  return (
    <details
      className="co-task-costs"
      onToggle={(e) => {
        if (e.currentTarget.open && !data && !busy) void refresh();
      }}
    >
      <summary>Task costs</summary>
      <p>
        All runs of this task, including retries. Record actual non-model expenses and their allocation basis here.
        These records do not create payments, change allowances or set customer prices.
      </p>
      {error && <p role="alert">{error}</p>}
      {notice && <p role="status">{notice}</p>}
      {pending && (
        <button disabled={busy} onClick={() => void perform()}>
          Check cost receipt
        </button>
      )}
      <button disabled={busy || !!pending} onClick={() => void refresh()}>
        Refresh task costs
      </button>
      {!data ? (
        <p role="status">{busy ? "Loading cost evidence…" : "Open the cost evidence to continue."}</p>
      ) : (
        <>
          <dl className="co-task-cost-totals">
            <div>
              <dt>Reported model charges</dt>
              <dd>{money(data.reportedModelUsd)}</dd>
            </div>
            <div>
              <dt>Estimated model costs</dt>
              <dd>{money(data.estimatedModelUsd)}</dd>
            </div>
            <div>
              <dt>Recorded operating expenses</dt>
              <dd>{data.operatingUsd === null ? "Not reconciled" : money(data.operatingUsd)}</dd>
            </div>
            <div>
              <dt>{data.includesModelEstimates ? "Reconciled task estimate" : "Reconciled task cost"}</dt>
              <dd>{data.reconciledTotalUsd === null ? "Not yet available" : money(data.reconciledTotalUsd)}</dd>
            </div>
          </dl>
          {data.companyKeyModelUsd > 0 && (
            <p>
              {money(data.companyKeyModelUsd)} of the reported or estimated model subtotal belongs to your company's API
              connection.
            </p>
          )}
          {unknown > 0 && (
            <p role="status">
              {unknown} model {unknown === 1 ? "run has" : "runs have"} no confirmed price. The total remains
              incomplete.
            </p>
          )}
          {held > 0 && (
            <p role="status">
              {held} provider or compute {held === 1 ? "outcome needs" : "outcomes need"} reconciliation before
              recording costs.
            </p>
          )}
          {data.reviews.some((r) => r.state === "recorded") && !data.reconciliationCurrent && (
            <p role="status">
              Usage changed after the last reconciliation. Its historical record is retained; save and confirm a new
              cost draft.
            </p>
          )}
          {data.compute.length > 0 && (
            <details>
              <summary>Compute evidence ({data.compute.length})</summary>
              <ul>
                {data.compute.map((j) => (
                  <li key={j.id}>
                    {j.kind} · {j.state} ·{" "}
                    {j.wallMilliseconds === null
                      ? "Observed duration unavailable"
                      : `${j.wallMilliseconds} ms observed`}{" "}
                    · {j.allocatedSeconds ?? j.heldSeconds}s {j.released ? "allocated" : "still held"}
                  </li>
                ))}
              </ul>
              <p>
                Allocated seconds are a safety allowance. Observed execution time does not include every hosting
                expense; use actual charge records below.
              </p>
            </details>
          )}
          {data.models.length > 0 && (
            <details>
              <summary>Model receipts ({data.models.length})</summary>
              <ul>
                {data.models.map((m) => (
                  <li key={m.id}>
                    {m.outcome} · {m.state} ·{" "}
                    {m.reportedUsd !== null
                      ? money(m.reportedUsd) + " reported"
                      : m.estimatedUsd !== null
                        ? money(m.estimatedUsd) + " estimated"
                        : "price unknown"}
                    {m.state !== "settled" ? " (not included in settled totals)" : ""}
                  </li>
                ))}
              </ul>
            </details>
          )}
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void perform("cost.save", {
                id: target?.id,
                expectedVersion: target?.version ?? 0,
                body: JSON.stringify({
                  taskId: task.id,
                  costs: Object.keys(categories).map((category) => ({
                    category,
                    usd: form.costs[category] === "" ? null : Number(form.costs[category]),
                  })),
                  basis: form.basis,
                }),
              });
            }}
          >
            <h4>
              {draft
                ? "Operating-cost draft"
                : data.reviews.some((r) => r.state === "recorded")
                  ? "Correct recorded expenses"
                  : "Reconcile operating expenses"}
            </h4>
            <p>
              Amounts are in USD. Leave unknown costs blank. Enter zero only when no cost applies. Include this task's
              share of actual receipts or shared expenses; exclude model charges already listed above.
            </p>
            <fieldset disabled={busy || !!pending}>
              <div className="co-task-cost-inputs">
                {Object.entries(categories).map(([id, label]) => (
                  <label key={id}>
                    {label}
                    <input
                      type="number"
                      min="0"
                      max="100000"
                      step="0.000001"
                      value={form.costs[id] ?? ""}
                      onChange={(e) => edit({ ...form, costs: { ...form.costs, [id]: e.target.value } })}
                    />
                  </label>
                ))}
              </div>
              <label>
                Receipts and allocation basis
                <textarea
                  maxLength={1000}
                  value={form.basis}
                  onChange={(e) => edit({ ...form, basis: e.target.value })}
                  placeholder="Describe the actual charges and how shared costs were assigned to this task."
                />
              </label>
              <button type="submit">Save cost draft</button>
              {dirty && (
                <button type="button" onClick={() => adopt(data)}>
                  Discard unsaved cost edits
                </button>
              )}
            </fieldset>
          </form>
          {draft && (
            <div className="co-task-cost-review">
              {draft.evidenceDigest !== data.evidenceDigest && (
                <p role="status">Usage changed since this draft was saved. Save the draft again before recording.</p>
              )}
              <label>
                <input
                  type="checkbox"
                  checked={consent}
                  disabled={busy || !!pending || !canRecord}
                  onChange={(e) => setConsent(e.target.checked)}
                />
                I confirm these are this task's actual non-model expenses or documented allocations, including any known
                zero costs.
              </label>
              <button
                disabled={busy || !!pending || !canRecord || !consent}
                onClick={() =>
                  void perform("cost.review", {
                    id: draft.id,
                    expectedVersion: draft.version,
                    digest: draft.digest,
                    decision: "record-costs",
                  })
                }
              >
                Record task costs
              </button>
              <button
                disabled={busy || !!pending || dirty}
                onClick={() =>
                  void perform("cost.review", {
                    id: draft.id,
                    expectedVersion: draft.version,
                    digest: draft.digest,
                    decision: "discard",
                  })
                }
              >
                Discard cost draft
              </button>
              {data.taskState !== "completed" && <p>Complete the task before recording its reconciled costs.</p>}
            </div>
          )}
          {data.reviews.some((r) => r.state === "recorded") && (
            <details>
              <summary>Recorded cost history</summary>
              {data.reviews
                .filter((r) => r.state === "recorded")
                .map((r) => (
                  <section key={r.id}>
                    <h4>
                      Record {r.sequence} · {r.recordedAt && new Date(r.recordedAt).toLocaleString()}
                    </h4>
                    <p>
                      {r.costs
                        .map((c) => `${categories[c.category as keyof typeof categories]}: ${money(c.usd!)}`)
                        .join(" · ")}
                    </p>
                    <p>{r.basis}</p>
                  </section>
                ))}
            </details>
          )}
          <button
            disabled={busy}
            onClick={() => {
              const href = URL.createObjectURL(new Blob([JSON.stringify(data, null, 2)], { type: "application/json" }));
              const link = document.createElement("a");
              link.href = href;
              link.download = "hirezero-task-costs.json";
              link.click();
              setTimeout(() => URL.revokeObjectURL(href), 1000);
              setNotice("Cost report downloaded, including your receipt notes.");
            }}
          >
            Download task cost report
          </button>
        </>
      )}
    </details>
  );
}
