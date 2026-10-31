import { useState } from "react";
import type { Event, Snapshot, Work } from "./api";
import { workGroups, workState } from "./Cockpit";
import { elapsed, useNow } from "./time";

// What happened to one piece of work (UX round 10; owner: "full transparency"): its saved events in order, who took
// each step and when. The summary is always shown; the steps open on demand, and receipts and costs follow below.
export function WorkTrail({ snapshot, task, events }: { snapshot: Snapshot; task: Work; events: Event[] }) {
  const d = snapshot.data;
  const running = (workGroups.progress as readonly string[]).includes(workState(snapshot, task));
  const [open, setOpen] = useState(running);
  const now = useNow(running ? 5_000 : 60_000);
  // A task's events are recorded against the task, its measured attempts and its results.
  const ids = new Set([
    task.id,
    ...(d.measured ?? []).filter((m) => m.taskId === task.id).map((m) => m.id),
    ...d.artifacts.filter((a) => a.taskId === task.id).map((a) => a.id),
  ]);
  const steps = events.filter((e) => ids.has(e.id));
  const who = (e: Event) =>
    !e.actor || e.actor === "system"
      ? (d.employees.find((x) => x.id === e.employeeId)?.name ?? "HireZero")
      : e.actor.split("@")[0];
  const first = steps[0] ? Date.parse(steps[0].at) : NaN;
  const offset = (iso: string) => {
    const seconds = Math.round((Date.parse(iso) - first) / 1000);
    if (!(seconds > 0)) return "";
    return "+" + (seconds < 60 ? seconds + " s" : Math.floor(seconds / 60) + " min " + (seconds % 60) + " s");
  };
  const started = d.taskTimes?.[task.id]?.started ?? steps[0]?.at;
  const last = steps.at(-1);
  const summary = running
    ? "Working · " + elapsed(started, now)
    : last
      ? steps.length + (steps.length === 1 ? " step" : " steps") + " · " + last.text
      : "Steps from before this session are in the audit log";
  return (
    <details className="co-trail" open={open} onToggle={(e) => setOpen(e.currentTarget.open)}>
      <summary>
        <span className={"co-trail-dot" + (running ? " live" : "")} aria-hidden="true" />
        <b>What happened</b>
        <span className="co-trail-summary">{summary}</span>
      </summary>
      {steps.length > 0 ? (
        <ol>
          {steps.map((e) => (
            <li key={e.sequence}>
              <time dateTime={e.at} title={new Date(e.at).toLocaleString()}>
                {new Date(e.at).toLocaleTimeString(undefined, {
                  hour: "numeric",
                  minute: "2-digit",
                  second: "2-digit",
                })}
              </time>
              <span>
                <b title={e.actor}>{who(e)}</b> {e.text}
              </span>
              <small>{offset(e.at)}</small>
            </li>
          ))}
        </ol>
      ) : (
        <p className="co-muted">Earlier steps are kept in Settings → Audit log.</p>
      )}
      <p className="co-trail-foot">Receipts, model usage and costs for this work are below.</p>
    </details>
  );
}
