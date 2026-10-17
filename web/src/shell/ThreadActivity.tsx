import { useEffect, useRef, useState } from "react";
import { CheckCircle2, ChevronDown, ChevronRight, LoaderCircle } from "lucide-react";
import { ApiError, api } from "./api";

type Step = { at: string; text: string; taskId: string | null; detail?: string | null };
type Stream = { label: string; state: string; startedAt: string; steps: Step[] };
// The durable record of decisions (who allowed, approved, started or stopped what). Messages are already in the chat, so they stay out.
type Decision = { id: string; at: string; actor: string; actorKind: string; action: string; summary: string };
const runningStates = new Set(["active", "running", "queued"]);
const clock = (at: string) =>
  new Date(at).toLocaleTimeString([], { hour: "numeric", minute: "2-digit", second: "2-digit" });
const when = (at: string) =>
  new Date(at).toDateString() === new Date().toDateString()
    ? clock(at)
    : new Date(at).toLocaleString([], { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" });
const who = (item: Decision) =>
  item.actorKind === "owner"
    ? "You"
    : item.actorKind === "agent"
      ? "Agent"
      : item.actorKind === "role"
        ? item.actor.charAt(0).toUpperCase() + item.actor.slice(1)
        : item.actor + " (" + item.actorKind + ")";
const since = (at: string) => {
  const seconds = Math.max(0, Math.round((Date.now() - Date.parse(at)) / 1000));
  return seconds < 60 ? seconds + "s" : Math.floor(seconds / 60) + "m " + (seconds % 60) + "s";
};

/** A quiet per-conversation log: one line saying what's happening now, and the steps behind it on request. */
export function ThreadActivity({ requestId, onForbidden }: { requestId: string; onForbidden: () => void }) {
  const [streams, setStreams] = useState<Stream[]>([]),
    [record, setRecord] = useState<Decision[]>([]),
    [running, setRunning] = useState(false),
    [open, setOpen] = useState(false),
    [, tick] = useState(0);
  const forbidden = useRef(onForbidden);
  forbidden.current = onForbidden;
  useEffect(() => {
    let active = true,
      timer = 0;
    async function load() {
      try {
        const value = await api<{ running: boolean; streams: Stream[]; record?: Decision[] }>(
          "/requests/" + requestId + "/activity",
        );
        if (!active) return;
        setStreams(value.streams);
        setRecord(
          (value.record ?? []).filter((item) => item.action !== "message.posted" && item.action !== "request.created"),
        );
        setRunning(value.running);
        timer = window.setTimeout(load, value.running ? 3000 : 15000);
      } catch (cause) {
        if (!active) return;
        if (cause instanceof ApiError && cause.status === 403) {
          forbidden.current();
          return;
        }
        timer = window.setTimeout(load, 15000);
      }
    }
    void load();
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [requestId]);
  // Keeps the "running for" time moving between polls.
  useEffect(() => {
    if (!running) return;
    const timer = setInterval(() => tick((value) => value + 1), 1000);
    return () => clearInterval(timer);
  }, [running]);
  if (!streams.length && !record.length) return null;
  // With no agent work yet, the bar shows the latest decision instead.
  const decided: Stream | undefined = record[0] && {
    label: "Decisions",
    state: "recorded",
    startedAt: record[0].at,
    steps: [{ at: record[0].at, text: who(record[0]) + ": " + record[0].summary, taskId: null }],
  };
  const current = streams.find((stream) => runningStates.has(stream.state)) ?? streams[0] ?? decided!,
    latest = current.steps[0];
  const attention = current.state === "needs you",
    finished = !running && ["pushed", "done", "completed", "published", "merged"].includes(current.state);
  return (
    <section
      className={
        "dev-activity" + (running ? " live" : "") + (attention ? " attention" : "") + (finished ? " done" : "")
      }
      aria-label="What the agent is doing"
    >
      <button className="dev-activity-bar" aria-expanded={open} onClick={() => setOpen((value) => !value)}>
        {open ? <ChevronDown size={14} /> : <ChevronRight size={14} />}
        {running ? (
          <LoaderCircle size={14} className="fe-spin" aria-hidden="true" />
        ) : finished ? (
          <CheckCircle2 size={15} className="dev-activity-check" aria-hidden="true" />
        ) : null}
        <strong>{current.label}</strong>
        <span
          className={attention ? "dev-activity-state attention" : finished ? "dev-activity-state done" : "fe-muted"}
        >
          {running
            ? current.state === "queued"
              ? "waiting its turn"
              : "running " + since(current.startedAt)
            : current.state}
        </span>
        {/* A failed step mid-run is normal (Sol tries, fixes, retries); say so rather than looking like the run failed. */}
        <span className="dev-activity-now">
          {latest ? latest.text : running ? "Starting…" : "No steps recorded."}
          {running && latest?.text.startsWith("✗") && <em className="fe-muted"> · a step failed, still working</em>}
        </span>
        {latest && <time className="fe-muted">{when(latest.at)}</time>}
      </button>
      {open && (
        <div className="dev-activity-log" role="log" aria-live="off">
          {streams.map((stream) => (
            <div key={stream.label + stream.startedAt}>
              <h5>
                {stream.label} · {runningStates.has(stream.state) ? "running" : stream.state} · started{" "}
                {clock(stream.startedAt)}
              </h5>
              {stream.steps.length ? (
                <ol>
                  {stream.steps.map((step, index) => (
                    <li
                      key={step.at + index}
                      className={step.detail === "warning" || step.text.startsWith("Stopped") ? "warn" : undefined}
                    >
                      <time>{clock(step.at)}</time>
                      <span>
                        {step.text}
                        {step.detail && step.detail !== "warning" && <code>{step.detail}</code>}
                      </span>
                    </li>
                  ))}
                </ol>
              ) : (
                <p className="fe-muted">
                  No steps recorded
                  {runningStates.has(stream.state) ? " yet" : " (steps from before a restart aren’t kept)"}.
                </p>
              )}
            </div>
          ))}
          {record.length > 0 && (
            <div>
              <h5>Decisions</h5>
              <ol>
                {record.map((item) => (
                  <li key={item.id}>
                    <time>{when(item.at)}</time>
                    <span>
                      {who(item)}: {item.summary}
                    </span>
                  </li>
                ))}
              </ol>
            </div>
          )}
        </div>
      )}
    </section>
  );
}
