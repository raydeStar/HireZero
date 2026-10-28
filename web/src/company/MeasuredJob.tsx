import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api";
import { command, type MeasuredEntry, type Work } from "./api";
import { MonthlyAllowance, type UsageAllowance } from "./Usage";
import { useMemberAccess } from "./MemberAccess";
import { ModelChoice, providerName, type ModelOption } from "./ModelChoice";

type Packet = {
  usage?: UsageAllowance;
  attempt: { id: string; measured: { digest: string } };
  call: {
    instructions: string;
    context: {
      id: string;
      employeeId: string;
      roleId: string;
      threadId: string;
      scope?: string;
      responsibility: string;
      input: string;
      companyKnowledge: string;
      departmentKnowledge: string;
      notes: string;
      history: unknown[];
      evidence: unknown[];
      scheduledOccurrence?: {
        scheduledFor: string;
        timeZone: string;
        cadence: string;
        runNumber: number;
        totalRuns: number;
      };
      revision?: { request: { feedback: string }; originalDraft: string; priorFeedback?: { feedback: string }[] };
      memory?: {
        notice: string;
        items: {
          memoryId: string;
          version: number;
          title: string;
          statement: string;
          rationale: string;
          approvedAt: string;
          reviewOn?: string;
          origin?: { quote: string; artifactId: string; version: number };
        }[];
      };
    };
  };
};
function Contents({ body }: { body: string }) {
  let fields: Record<string, unknown> | null = null;
  try {
    const value = JSON.parse(body);
    if (value && typeof value === "object" && !Array.isArray(value)) fields = value;
  } catch {
    /* Plain text is shown verbatim. */
  }
  return fields ? (
    <dl className="co-packet-fields">
      {Object.entries(fields).map(([key, value]) => (
        <div key={key}>
          <dt>{key.replace(/([A-Z])/g, " $1")}</dt>
          <dd>{typeof value === "string" ? value : JSON.stringify(value)}</dd>
        </div>
      ))}
    </dl>
  ) : (
    <pre>{body || "None"}</pre>
  );
}
const states: Record<string, string> = {
  proposed: "Ready for your authorization",
  dispatched: "Running this one job",
  completed: "Draft saved for review",
  unknown: "Stopped: reconciliation required",
  withheld: "Output withheld",
  expired: "Proposal expired",
  canceled: "Canceled",
};
export function MeasuredJob({
  task,
  entry,
  available,
  busy,
  prepare,
  refresh,
  conversational = false,
  cents: defaultCents = false,
  modelChoices = [],
}: {
  task: Work;
  entry?: MeasuredEntry;
  available: boolean;
  busy: boolean;
  prepare: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  refresh: () => Promise<void>;
  conversational?: boolean;
  // An API model enforces the allowance as a cap in US cents before sending (docs/adr/0036-api-model-provider.md).
  cents?: boolean;
  modelChoices?: ModelOption[];
}) {
  const { canWork } = useMemberAccess();
  const authorizing = useRef(false),
    reviewRequest = useRef(0);
  const [allowance, setAllowance] = useState(""),
    [packet, setPacket] = useState<Packet | null>(null),
    [ack, setAck] = useState(false),
    [reviewing, setReviewing] = useState(false),
    [running, setRunning] = useState(false),
    [error, setError] = useState("");
  const [modelRoute, setModelRoute] = useState(task.requestedModelRoute ?? "");
  const formCents =
    (modelChoices.find((option) => option.route === modelRoute)?.allowanceUnit ??
      (defaultCents ? "usd-cents" : "credits")) === "usd-cents";
  const cents = entry?.job.terms ? entry.job.terms.allowanceUnit === "usd-cents" : defaultCents;
  useEffect(() => {
    // A fresh envelope needs fresh eyes. Never carry consent or an old context preview into its replacement.
    reviewRequest.current++;
    setPacket(null);
    setAck(false);
    setError("");
    setReviewing(false);
  }, [entry?.id, entry?.job.digest]);
  async function review() {
    if (!entry) return;
    const request = ++reviewRequest.current;
    setError("");
    setAck(false);
    setPacket(null);
    setReviewing(true);
    try {
      const next = await api<Packet>("/company/measured/" + entry.id);
      if (next.attempt.id !== entry.id || next.attempt.measured.digest !== entry.job.digest)
        throw new Error("The saved proposal changed. Refresh before reviewing it.");
      if (request === reviewRequest.current) setPacket(next);
    } catch (e) {
      if (request === reviewRequest.current) setError((e as Error).message);
    } finally {
      if (request === reviewRequest.current) setReviewing(false);
    }
  }
  async function authorize() {
    if (!canWork) return;
    if (
      authorizing.current ||
      !entry ||
      !ack ||
      !packet ||
      packet.attempt.id !== entry.id ||
      packet.attempt.measured.digest !== entry.job.digest ||
      running ||
      !available ||
      entry.job.state !== "proposed" ||
      Date.parse(entry.job.expiresAt) <= Date.now()
    )
      return;
    authorizing.current = true;
    setRunning(true);
    setError("");
    try {
      await command("measured.run", {
        id: entry.id,
        expectedVersion: entry.version,
        digest: entry.job.digest,
        decision: "accept-soft-limits",
      });
    } catch (e) {
      setError((e as Error).message);
    } finally {
      await refresh().catch((e) => setError(e.message));
      authorizing.current = false;
      setRunning(false);
    }
  }
  return (
    <section className="co-measured">
      {!conversational && <h3>{entry?.job.state === "completed" ? "Execution receipt" : "Run one internal draft"}</h3>}
      {conversational && running && (
        <p role="status" aria-live="polite">
          Reply pending. Waiting for complete text and measured usage.
        </p>
      )}
      {!available && <p className="co-muted">A configured model connection is required for manual jobs.</p>}
      {error && <p role="alert">{error}</p>}
      {entry?.job.terms && (
        <p>
          Account: {providerName(entry.job.terms.provider)} · {entry.job.route}
        </p>
      )}
      {entry?.job.state === "expired" && (
        <p>The review expired or its model connection changed. Nothing was sent. Prepare a fresh review below.</p>
      )}
      {!canWork && (
        <p className="co-muted">
          An approver or owner can prepare and authorize work. Saved status and context remain available.
        </p>
      )}
      {canWork &&
        (!entry || entry.job.state === "expired") &&
        available &&
        ["queued", "blocked"].includes(task.state) && (
          <form
            onSubmit={(e) => {
              e.preventDefault();
              void prepare("measured.prepare", {
                id: task.id,
                expectedVersion: task.version,
                creditAllowance: Number(allowance),
                modelRoute,
              });
            }}
          >
            <ModelChoice
              options={modelChoices}
              value={modelRoute}
              disabled={busy}
              onChange={(route) => {
                setModelRoute(route);
                setAllowance("");
              }}
            />
            <label>
              {formCents ? "Spend cap in US cents" : "Intended existing-credit allowance"}
              <input
                type="number"
                min="0.01"
                max="1000"
                step="0.01"
                required
                value={allowance}
                onChange={(e) => setAllowance(e.target.value)}
              />
            </label>
            <p className="co-muted">
              {formCents
                ? "Nothing is sent if the worst case (the whole context and the full reply limit at the model's listed price) could cost more than this."
                : "This records your intended allowance. It cannot stop an in-flight request at that credit amount. Included usage must be available first. No purchases, recharge or API-key fallback."}
            </p>
            <button disabled={busy} type="submit">
              Prepare job for review
            </button>
          </form>
        )}
      {entry && (
        <>
          <p>
            <b>
              {conversational
                ? ((
                    {
                      proposed: "Reply prepared for your review",
                      dispatched: "Reply pending",
                      completed: "Reply saved",
                    } as Record<string, string>
                  )[entry.job.state] ??
                  states[entry.job.state] ??
                  entry.job.state)
                : entry.job.state === "completed" && task.state === "completed"
                  ? "Result approved and task complete"
                  : states[entry.job.state] || entry.job.state}
            </b>{" "}
            · {entry.job.route}
          </p>
          {entry.job.state === "proposed" && (
            <>
              {/* What you are authorizing, in one line; the safety details are a click away (owner walkthrough). */}
              <p className="co-job-summary">
                {conversational ? "Sends one reply" : "Runs one job"} ·{" "}
                {cents
                  ? `spend cap ${entry.job.creditAllowance} US cents`
                  : `intended allowance ${entry.job.creditAllowance} ${entry.job.creditAllowance === 1 ? "credit" : "credits"}`}{" "}
                · expires {new Date(entry.job.expiresAt).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })}
              </p>
              <button disabled={busy || running || reviewing} aria-busy={reviewing} onClick={() => void review()}>
                {reviewing
                  ? "Loading review…"
                  : conversational
                    ? "Review reply and context"
                    : "Review task and context"}
              </button>
              {packet && (
                <div className="co-packet">
                  <h4>Context for this request</h4>
                  <p>
                    {packet.call.context.roleId} · {packet.call.context.scope ?? "employee"} context
                  </p>
                  <details>
                    <summary>Request identifiers</summary>
                    <small>
                      Employee {packet.call.context.employeeId} · conversation {packet.call.context.threadId}
                    </small>
                  </details>
                  <h4>Responsibility included</h4>
                  <Contents body={packet.call.context.responsibility} />
                  <h4>Requested work</h4>
                  <Contents body={packet.call.context.input} />
                  {packet.call.context.revision && (
                    <section aria-label="Revision context">
                      {!!packet.call.context.revision.priorFeedback?.length && (
                        <section aria-label="Earlier requested changes">
                          <h4>Earlier requested changes retained</h4>
                          <ol>
                            {packet.call.context.revision.priorFeedback.map((item, index) => (
                              <li key={index}>
                                <Contents body={item.feedback} />
                              </li>
                            ))}
                          </ol>
                          <p>
                            Oldest first. The latest feedback only replaces earlier directions where it explicitly
                            changes them.
                          </p>
                        </section>
                      )}
                      <h4>Requested changes</h4>
                      <Contents body={packet.call.context.revision.request.feedback} />
                      <details>
                        <summary>Previous unapproved draft included for revision</summary>
                        <Contents body={packet.call.context.revision.originalDraft} />
                      </details>
                      <p>The previous draft is reference material, not approved company knowledge.</p>
                    </section>
                  )}
                  <h4>Approved department facts</h4>
                  <Contents body={packet.call.context.departmentKnowledge} />
                  {packet.call.context.companyKnowledge && (
                    <>
                      <h4>Approved company facts</h4>
                      <Contents body={packet.call.context.companyKnowledge} />
                    </>
                  )}
                  {packet.usage && <MonthlyAllowance usage={packet.usage} />}
                  <h4>Private notes included</h4>
                  <Contents body={packet.call.context.notes} />
                  <h4>Conversation history included</h4>
                  <Entries items={packet.call.context.history} />
                  <h4>Selected evidence included</h4>
                  <Entries items={packet.call.context.evidence} />
                  {packet.call.context.memory && (
                    <>
                      <h4>Approved organizational memory included</h4>
                      <p>{packet.call.context.memory.notice}</p>
                      {packet.call.context.memory.items.map((item) => (
                        <article key={item.memoryId}>
                          <h5>
                            {item.title} · version {item.version}
                          </h5>
                          <p>{item.statement}</p>
                          {item.rationale && <p>{item.rationale}</p>}
                          <small>
                            Approved {new Date(item.approvedAt).toLocaleString()}
                            {item.reviewOn ? ` · Review on ${item.reviewOn} (UTC)` : ""}
                          </small>
                          {item.origin && (
                            <blockquote>
                              {item.origin.quote}
                              <small>
                                Approved result {item.origin.artifactId} · version {item.origin.version}
                              </small>
                            </blockquote>
                          )}
                        </article>
                      ))}
                    </>
                  )}
                  <details>
                    <summary>Complete sealed application context</summary>
                    <pre>{JSON.stringify(packet.call.context, null, 2)}</pre>
                  </details>
                  <details>
                    <summary>Employee instructions</summary>
                    <pre>{packet.call.instructions}</pre>
                  </details>
                </div>
              )}
              <details className="co-safety">
                <summary>Safety details</summary>
                <p>
                  {conversational
                    ? "Send authorizes one text-only native turn: no autonomous tasks, plans, tools or follow-up work. The reply uses its attached message and the saved context; later messages wait for a new turn. A consumed turn cannot be replayed."
                    : "Nothing starts automatically. Authorizing runs one job on the saved task and context; a consumed request cannot be replayed."}
                </p>
                <p>
                  One request is allowed; the runtime may retry internally. The complete context is{" "}
                  {entry.job.applicationBytes.toLocaleString()} of {entry.job.policy.applicationBytes.toLocaleString()}{" "}
                  bytes.
                </p>
                <p>
                  This job reserves {entry.job.policy.planningTokens.toLocaleString()} estimated provider tokens across
                  task, employee, department and company budgets. This unused task's planning allowance increases to
                  that amount.
                </p>
                <p>
                  Reasoning effort: {cents ? "low" : (entry.job.policy.effort ?? "low")}. Reasoning tokens count as
                  output tokens.
                </p>
                <p>
                  Soft stops: {entry.job.policy.localDeadlineSeconds} seconds,{" "}
                  {entry.job.policy.visibleCharacterStop.toLocaleString()} visible characters,{" "}
                  {entry.job.policy.outputTarget.toLocaleString()} observed output tokens and{" "}
                  {entry.job.policy.planningTokens.toLocaleString()} observed total tokens. These are not upstream token
                  caps.
                </p>
                <p>
                  {cents
                    ? `Spend cap: ${entry.job.creditAllowance} US cents, checked against the worst case before sending.`
                    : `Intended existing-credit allowance: ${entry.job.creditAllowance}. There is no provider-enforced per-job credit cap; this request may exceed that amount.`}
                </p>
                <small>
                  Expires {new Date(entry.job.expiresAt).toLocaleString()}. Policy {entry.job.policy.version}. The
                  server binds your authorization to this exact saved task and context.
                </small>
              </details>
              {canWork && (
                <>
                  <label className="co-check">
                    <input
                      type="checkbox"
                      checked={ack}
                      disabled={!packet}
                      onChange={(e) => setAck(e.target.checked)}
                    />
                    {cents
                      ? `I reviewed the ${conversational ? "message, recipient" : "work"}, context, selected API account and spend allowance, and I authorize one ${conversational ? "reply" : "job"}.`
                      : conversational
                        ? "I reviewed the message, recipient and context, and I authorize one reply. Token, time and credit limits are soft and can be exceeded upstream."
                        : "I reviewed the work and context, and I authorize one job. Token, time and credit limits are soft and can be exceeded upstream."}
                  </label>
                  {!packet && (
                    <p className="co-muted co-locked-hint" role="note">
                      Review {conversational ? "the reply" : "the task"} and context first: you authorize only what you
                      have seen.
                    </p>
                  )}
                  <button
                    disabled={
                      !available || busy || running || !ack || !packet || Date.parse(entry.job.expiresAt) <= Date.now()
                    }
                    onClick={() => void authorize()}
                  >
                    {conversational
                      ? running
                        ? "Waiting for this reply."
                        : "Send & authorize one reply"
                      : running
                        ? "Waiting for this job."
                        : "Authorize and run one job"}
                  </button>
                </>
              )}
            </>
          )}
          {task.scheduleRunId && entry.job.state !== "proposed" && (
            <section aria-label="Scheduled date in this request">
              <button disabled={reviewing} aria-busy={reviewing} onClick={() => void review()}>
                {reviewing ? "Loading saved date…" : "View scheduled request date"}
              </button>
              {packet &&
                (packet.call.context.scheduledOccurrence ? (
                  <p>
                    {new Date(packet.call.context.scheduledOccurrence.scheduledFor).toLocaleString(undefined, {
                      timeZone: packet.call.context.scheduledOccurrence.timeZone,
                    })}{" "}
                    ({packet.call.context.scheduledOccurrence.timeZone}) · Run{" "}
                    {packet.call.context.scheduledOccurrence.runNumber} of{" "}
                    {packet.call.context.scheduledOccurrence.totalRuns} ·{" "}
                    {packet.call.context.scheduledOccurrence.cadence}. This planned date anchors the work; it does not
                    establish when sources were last checked.
                  </p>
                ) : (
                  <p>This request did not include a scheduled date.</p>
                ))}
            </section>
          )}
          {entry.usage && (
            <p>
              {entry.reportedUsage === null
                ? "Partial observed usage; final total is unknown."
                : "Reported provider usage:"}{" "}
              input {entry.usage.inputTokens.toLocaleString()}, output {entry.usage.outputTokens.toLocaleString()},
              reasoning {entry.usage.reasoningTokens?.toLocaleString() ?? "unknown"} (included in output), total{" "}
              {entry.reportedUsage?.toLocaleString() ?? "unknown"}. Runtime input is included.
            </p>
          )}
          {entry.error && (
            <p className="co-muted">
              {entry.error}. Output is withheld where the outcome or accounting needs reconciliation.
            </p>
          )}
        </>
      )}
    </section>
  );
}

// Conversation history or evidence in a sealed context, as a readable list rather than raw JSON.
function Entries({ items }: { items: unknown[] }) {
  if (!items.length) return <p className="co-muted">None</p>;
  return (
    <ol className="co-entries">
      {items.map((item, i) => {
        const value = item as Record<string, unknown> | string;
        if (typeof value === "string") return <li key={i}>{value}</li>;
        const who = value.author ?? value.role ?? value.employee ?? value.id;
        const text = value.body ?? value.text ?? value.content;
        return (
          <li key={i}>
            {who !== undefined && <b>{String(who)}: </b>}
            {text !== undefined ? String(text) : JSON.stringify(value)}
          </li>
        );
      })}
    </ol>
  );
}
