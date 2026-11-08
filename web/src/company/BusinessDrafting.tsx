import { customerView } from "./audience";
import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api";
import { providerName } from "./ModelChoice";
import { defaultAllowance, SpendLimit, spendText } from "./allowance";
import {
  command,
  type BusinessIdentity,
  type BusinessSource,
  type BusinessDraftJob,
  type BusinessProfile,
  type Snapshot,
} from "./api";

type Packet = BusinessDraftJob & {
  call: { instructions: string; context: { input: string; evidence: { id: string; body: string }[] } };
};
const status: Record<string, string> = {
  proposed: "Prepared for authorization",
  running: "Drafting your brief…",
  completed: "Your generated brief is ready",
  invalid: "The response did not pass evidence checks",
  withheld: "Response withheld",
  unknown: "Provider receipt needs reconciliation",
  reconciled: "Provider receipt reconciled",
};
const draftStatus = (job: BusinessDraftJob) =>
  job.state === "reconciled" && job.localRefusalReceipt ? "Local refusal recorded" : status[job.state] || job.state;

export function BusinessDrafting({
  snapshot,
  identity,
  sources,
  refresh,
  load,
}: {
  snapshot: Snapshot;
  identity: BusinessIdentity;
  sources: BusinessSource[];
  refresh: () => Promise<void>;
  load: (profile: BusinessProfile) => void;
}) {
  const [selected, setSelected] = useState(sources.slice(-3).map((s) => s.id));
  const [allowance, setAllowance] = useState(""),
    [selectedJob, setSelectedJob] = useState("");
  const [packet, setPacket] = useState<Packet | null>(null),
    [ack, setAck] = useState(false);
  const [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const [receipt, setReceipt] = useState(""),
    [receiptKind, setReceiptKind] = useState("provider"),
    [input, setInput] = useState(""),
    [output, setOutput] = useState(""),
    [evidence, setEvidence] = useState(""),
    [stopped, setStopped] = useState(false);
  const locked = useRef(false);
  const jobs = (snapshot.data.businessDrafts || []).filter((d) => d.businessVersion === identity.version);
  const job = jobs.find((d) => d.id === selectedJob) || jobs.at(-1);
  useEffect(() => {
    setReceiptKind("provider");
    setReceipt("");
    setInput("");
    setOutput("");
    setEvidence("");
    setStopped(false);
  }, [job?.id]);
  const result = snapshot.data.businessProfiles?.find((p) => p.id === job?.profileId);
  const cents = snapshot.capabilities.allowanceUnit === "usd-cents";
  // Included usage has no per-job limit, so its planning default is sent unasked (docs/adr/0177-plain-spend-limits.md).
  const sentAllowance = cents ? allowance || defaultAllowance(true) : defaultAllowance(false);
  // modelExecution is automatic work; ADR 0036 leaves it off for every real provider.
  // This form prepares one request; the exact packet and allowance still require explicit authorization.
  const available = snapshot.capabilities.measuredOneJob;
  async function perform(work: () => Promise<void>) {
    if (locked.current) return;
    locked.current = true;
    setBusy(true);
    setError("");
    try {
      await work();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      await refresh().catch(() =>
        setError("The overview could not refresh. Check saved draft status before submitting again."),
      );
      locked.current = false;
      setBusy(false);
    }
  }
  return (
    <section className="co-business-drafting" aria-label="Draft from evidence">
      <h3>Let HireZero draft the brief</h3>
      <p>
        Choose up to three saved sources. One model request proposes the facts and supporting quotes. Existing owner
        corrections are kept; you review everything before approval.
      </p>
      {!available && (
        <p>
          A measured model connection is needed. <a href="?view=departments&section=integrations">Open integrations</a>,
          or keep writing the brief below.
        </p>
      )}
      {error && (
        <p role="alert" className="co-error">
          {error}
        </p>
      )}
      <details open={!job}>
        <summary>Prepare a draft request</summary>
        <fieldset disabled={busy || !available}>
          <legend>Sources for this draft</legend>
          {sources.map((source) => (
            <label className="co-business-check" key={source.id}>
              <input
                type="checkbox"
                checked={selected.includes(source.id)}
                disabled={!selected.includes(source.id) && selected.length >= 3}
                onChange={(e) => {
                  setSelected(e.target.checked ? [...selected, source.id] : selected.filter((id) => id !== source.id));
                  setAck(false);
                }}
              />
              <span>
                {source.title || source.url}
                <small>{source.url}</small>
              </span>
            </label>
          ))}
          <SpendLimit cents={cents} value={sentAllowance} onChange={setAllowance} />
          <p className="co-muted">
            {customerView()
              ? "Included in your plan."
              : cents
                ? "Nothing is sent if the most this could cost is over your spend limit."
                : "Uses your plan's included usage, which has no per-job limit. Nothing is bought or recharged."}
          </p>
          <button
            disabled={selected.length === 0 || !Number.isFinite(Number(sentAllowance)) || Number(sentAllowance) <= 0}
            onClick={() =>
              perform(async () => {
                const prepared = (await command(
                  "business.draft.prepare",
                  {
                    expectedVersion: identity.version,
                    digest: identity.digest,
                    evidence: selected,
                    creditAllowance: Number(sentAllowance),
                  },
                  () => {},
                )) as BusinessDraftJob;
                setSelectedJob(prepared.id);
                setPacket(null);
                setAck(false);
              })
            }
          >
            Prepare brief draft
          </button>
        </fieldset>
      </details>
      {job && (
        <div className="co-business-draft-status">
          {jobs.length > 1 && (
            <label>
              Draft request
              <select
                value={job.id}
                disabled={busy}
                onChange={(e) => {
                  setSelectedJob(e.target.value);
                  setPacket(null);
                  setAck(false);
                }}
              >
                {jobs.map((j) => (
                  <option key={j.id} value={j.id}>
                    {new Date(j.updatedAt).toLocaleString()} · {draftStatus(j)}
                  </option>
                ))}
              </select>
            </label>
          )}
          <p role="status">
            <b>{draftStatus(job)}</b>
          </p>
          {job.error && <p role="alert">{job.error}</p>}
          {job.localRefusalReceipt && (
            <p>Local diagnostic receipt: {job.localRefusalReceipt}. No provider receipt was claimed.</p>
          )}
          {job.usage && (
            <p className="co-muted">
              Recorded usage: {job.usage.inputTokens.toLocaleString()} input + {job.usage.outputTokens.toLocaleString()}{" "}
              output tokens{job.providerReceipt ? " · " + job.providerReceipt : ""}.
            </p>
          )}
          {job.state === "proposed" && (
            <>
              <p>
                {job.sourceIds.length} source(s) ·{" "}
                {spendText(
                  job.job.terms ? job.job.terms.allowanceUnit === "usd-cents" : cents,
                  job.job.creditAllowance,
                )}{" "}
                · {job.job.policy.localDeadlineSeconds}-second local deadline.
              </p>
              <button
                disabled={busy}
                onClick={() =>
                  perform(async () => {
                    const saved = await api<Packet>("/company/business/drafts/" + encodeURIComponent(job.id));
                    if (saved.job.digest !== job.job.digest || saved.state !== "proposed")
                      throw new Error("The request changed. Refresh before reviewing it.");
                    setPacket(saved);
                    setAck(false);
                  })
                }
              >
                Review draft request
              </button>
              {packet?.id === job.id && packet.job.digest === job.job.digest && (
                <>
                  <div className="co-business-draft-terms" aria-label="Brief request model and limits">
                    <p>
                      <b>Model:</b> {packet.job.route} · {providerName(packet.job.terms?.provider ?? "host")}
                    </p>
                    <p>
                      Reasoning effort:{" "}
                      {packet.job.terms?.allowanceUnit === "usd-cents" ? "low" : (packet.job.policy.effort ?? "low")}.{" "}
                      Output target: {packet.job.policy.outputTarget.toLocaleString()} tokens. Reasoning tokens count as
                      output.
                    </p>
                    <p>
                      One model request. No automatic retry. The local deadline and output target are soft limits.
                      {customerView()
                        ? " It's included in your plan."
                        : packet.job.terms?.allowanceUnit !== "usd-cents" &&
                          " It uses your plan's included usage, which has no per-job limit; nothing is bought."}
                    </p>
                  </div>
                  <details className="co-business-excerpt">
                    <summary>Exact business context and sources</summary>
                    <pre>{packet.call.context.input}</pre>
                    {packet.call.context.evidence.map((s) => (
                      <pre key={s.id}>{s.body}</pre>
                    ))}
                    <pre>{packet.call.instructions}</pre>
                  </details>
                  <label className="co-business-check">
                    <input type="checkbox" checked={ack} onChange={(e) => setAck(e.target.checked)} />
                    <span>
                      I authorize one draft from this context
                      {cents && !customerView() ? " within the shown spend limit." : ", acknowledging the soft limits."}
                    </span>
                  </label>
                  <button
                    className="co-primary"
                    disabled={busy || !ack || !available || Date.parse(job.job.expiresAt) <= Date.now()}
                    onClick={() =>
                      perform(async () => {
                        setAck(false);
                        await command(
                          "business.draft.run",
                          {
                            id: job.id,
                            expectedVersion: job.version,
                            digest: job.job.digest,
                            decision: "accept-soft-limits",
                          },
                          () => {},
                        );
                      })
                    }
                  >
                    Authorize one brief draft
                  </button>
                </>
              )}
              {Date.parse(job.job.expiresAt) <= Date.now() && (
                <p>This request expired. Prepare a new request to review.</p>
              )}
            </>
          )}
          {job.state === "completed" && result && (
            <>
              <p>
                The generated facts are saved as a draft. Loading them keeps any unsaved field edits you have made
                below.
              </p>
              <button className="co-primary" disabled={busy} onClick={() => load(result)}>
                Review generated brief
              </button>
            </>
          )}
          {job.state === "unknown" && (
            <details>
              <summary>Reconcile request outcome</summary>
              {receiptKind === "provider" && (
                <p>
                  Use the provider's actual receipt and confirm its request has stopped. This accounts for usage; it
                  does not rerun the request or import its response.
                </p>
              )}
              <form
                onSubmit={(e) => {
                  e.preventDefault();
                  void perform(async () => {
                    setStopped(false);
                    await command(
                      "business.draft.reconcile",
                      {
                        id: job.id,
                        expectedVersion: job.version,
                        digest: job.job.digest,
                        decision:
                          receiptKind === "local-refusal"
                            ? "confirmed-local-refusal"
                            : "confirmed-usage-and-termination",
                        body: JSON.stringify({
                          receipt,
                          kind: receiptKind,
                          usage: {
                            inputTokens: receiptKind === "local-refusal" ? 0 : Number(input),
                            outputTokens: receiptKind === "local-refusal" ? 0 : Number(output),
                          },
                          evidence,
                          providerStopped: stopped,
                        }),
                      },
                      () => {},
                    );
                  });
                }}
              >
                <fieldset disabled={busy}>
                  <label>
                    Evidence type
                    <select
                      value={receiptKind}
                      onChange={(e) => {
                        setReceiptKind(e.target.value);
                        setStopped(false);
                        setReceipt("");
                        setEvidence("");
                      }}
                    >
                      <option value="provider">Provider receipt</option>
                      <option
                        value="local-refusal"
                        disabled={
                          !!job.providerReceipt ||
                          (job.reportedUsage ?? 0) > 0 ||
                          (job.usage?.inputTokens ?? 0) > 0 ||
                          (job.usage?.outputTokens ?? 0) > 0 ||
                          (job.usage?.billedUsd ?? 0) > 0
                        }
                      >
                        Local refusal before dispatch
                      </option>
                    </select>
                  </label>
                  {receiptKind === "local-refusal" && (
                    <p>
                      Use local logs that establish no model request was sent. A timeout or interrupted request does not
                      establish this. This records zero usage with a separate diagnostic receipt; it never reruns the
                      request.
                    </p>
                  )}
                  <label>
                    {receiptKind === "local-refusal" ? "Local diagnostic receipt" : "Provider receipt"}
                    <input required maxLength={160} value={receipt} onChange={(e) => setReceipt(e.target.value)} />
                  </label>
                  {receiptKind === "provider" && (
                    <>
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
                    </>
                  )}
                  <label>
                    How was this verified?
                    <textarea required maxLength={500} value={evidence} onChange={(e) => setEvidence(e.target.value)} />
                  </label>
                  <label className="co-business-check">
                    <input type="checkbox" checked={stopped} onChange={(e) => setStopped(e.target.checked)} />
                    <span>
                      {receiptKind === "local-refusal"
                        ? "I verified from local evidence that no model request was sent."
                        : "I verified that the provider request has stopped and this is its complete usage."}
                    </span>
                  </label>
                  <button disabled={!stopped} type="submit">
                    {receiptKind === "local-refusal" ? "Record local refusal" : "Record reconciled usage"}
                  </button>
                </fieldset>
              </form>
            </details>
          )}
          {job.state === "invalid" && job.rejectedResponse && (
            <details>
              <summary>Inspect rejected model response</summary>
              <p>
                This response failed the brief checks. It is not approved knowledge. You can use the source evidence to
                correct the brief below; another model request is optional.
              </p>
              <pre>{job.rejectedResponse}</pre>
            </details>
          )}
          <button disabled={busy} onClick={() => perform(async () => {})}>
            Refresh draft status
          </button>
        </div>
      )}
    </section>
  );
}
