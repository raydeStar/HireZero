import { useEffect, useState } from "react";
import { Check, CircleAlert, PanelRightClose, X } from "lucide-react";
import { api } from "../lib/api";
import type { Snapshot, Work } from "./api";
import { taskState, workName } from "./labels";
import { ago, elapsed, useNow } from "./time";
import { dollars } from "./money";

export type Need = {
  kind: string;
  title: string;
  detail: string;
  employeeId?: string;
  taskId?: string;
  // The conversation an item lives in (a Connect card).
  threadId?: string | null;
  key: string;
  priority: string;
};

// How work is grouped in the cockpit and on the Work board (docs/adr/0040-the-muse-layout.md).
export const workGroups = {
  review: ["awaiting_review", "unknown"],
  progress: ["running", "gathering"],
  next: ["queued", "blocked"],
  done: ["completed", "failed", "cancelled", "canceled", "withheld"],
} as const;
const cancellable = ["queued", "blocked", "running"];

export function useNeeds(version: number) {
  const [needs, setNeeds] = useState<Need[]>([]);
  useEffect(() => {
    let current = true;
    api<{ needsYou: Need[] }>("/company/today")
      .then((today) => current && setNeeds(today.needsYou))
      .catch(() => current && setNeeds([]));
    return () => {
      current = false;
    };
  }, [version]);
  return needs;
}

export function workTitle(snapshot: Snapshot, task: Work) {
  // A context draft runs as one reply, but it is drafting context, not a conversation (owner feedback, 2026-10-20).
  if (task.kind === "context-draft") return task.contextScope === "company" ? "Company facts draft" : "Context draft";
  const d = snapshot.data,
    employee = d.employees.find((e) => e.id === task.employeeId);
  return workName(
    d.roles.find((r) => r.id === employee?.roleId),
    task.skill,
  );
}

// The cockpit (docs/adr/0040-the-muse-layout.md): what needs you, what is running, what is next and what just finished,
// pinned beside every conversation. It arranges existing views and commands; approvals stay on the job card.
export function Cockpit({
  snapshot,
  needs,
  scope,
  busy,
  run,
  onOpenWork,
  onOpenNeed,
  onClose,
}: {
  snapshot: Snapshot;
  needs: Need[];
  scope?: string;
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  onOpenWork: (task: Work) => void;
  onOpenNeed: (need: Need) => void;
  onClose: () => void;
}) {
  const d = snapshot.data;
  const [everyone, setEveryone] = useState(false);
  const now = useNow(15_000);
  const scoped = scope && !everyone ? d.employees.find((e) => e.id === scope) : undefined;
  const mine = (employeeId?: string) => !scoped || employeeId === scoped.id;
  const tasks = d.tasks.filter((t) => mine(t.employeeId));
  const inGroup = (group: readonly string[]) => tasks.filter((t) => group.includes(t.state));
  const shownNeeds = needs.filter((n) => mine(n.employeeId));
  const working = inGroup(workGroups.progress);
  const next = inGroup(workGroups.next).filter((t) => t.reason !== "handoff-awaiting-approval");
  const done = inGroup(workGroups.done).slice(-5).reverse();
  const nameOf = (id: string) => d.employees.find((e) => e.id === id)?.name ?? "Former employee";
  const title = (t: Work) => workTitle(snapshot, t);
  const cancel = (t: Work) =>
    cancellable.includes(t.state) && (
      <button
        className="fe-icon-button"
        aria-label={"Cancel " + title(t) + " for " + nameOf(t.employeeId)}
        title="Cancel"
        disabled={busy}
        onClick={() => void run("task.cancel", { id: t.id, expectedVersion: t.version })}
      >
        <X size={13} />
      </button>
    );
  // Live detail (UX round 8): running work shows how long it has run; other rows show when they last changed.
  const when = (t: Work) => {
    const times = d.taskTimes?.[t.id];
    if (!times) return "";
    return (workGroups.progress as readonly string[]).includes(t.state)
      ? " · " + elapsed(times.started, now)
      : " · " + ago(times.updated, now);
  };
  const row = (t: Work) => (
    <li key={t.id}>
      <button className="fe-up-next-title" onClick={() => onOpenWork(t)} title={title(t)}>
        <span>{title(t)}</span>
        <small>{(scoped ? taskState(t.state) : nameOf(t.employeeId) + " · " + taskState(t.state)) + when(t)}</small>
      </button>
      {cancel(t)}
    </li>
  );
  const spend = d.spend;
  return (
    <aside className="fe-cockpit co-cockpit" aria-label="Cockpit">
      <header className="fe-cockpit-head">
        <h2>Cockpit</h2>
        <button className="fe-icon-button" aria-label="Hide cockpit" onClick={onClose}>
          <PanelRightClose size={17} />
        </button>
      </header>
      <div className="fe-cockpit-body">
        {scope && (
          <div className="co-cockpit-scope" role="group" aria-label="Cockpit scope">
            <button aria-pressed={!everyone} onClick={() => setEveryone(false)}>
              {nameOf(scope)}
            </button>
            <button aria-pressed={everyone} onClick={() => setEveryone(true)}>
              Everyone
            </button>
          </div>
        )}
        <section className="fe-cockpit-section co-needs" aria-label="Needs you">
          <h3>
            Needs you <span className={"fe-count" + (shownNeeds.length ? " attn" : "")}>{shownNeeds.length}</span>
          </h3>
          {shownNeeds.length ? (
            <ol className="fe-up-next">
              {shownNeeds.map((n) => (
                <li key={n.key} className={"co-need priority-" + n.priority}>
                  <button className="fe-up-next-title" onClick={() => onOpenNeed(n)} title={n.detail}>
                    <span>{n.title}</span>
                    <small>{n.detail}</small>
                  </button>
                </li>
              ))}
            </ol>
          ) : (
            <p className="fe-cockpit-clear">
              <Check size={14} /> Nothing needs you.
            </p>
          )}
        </section>
        <section
          className={"fe-cockpit-section fe-working-on" + (working.length ? " active" : "")}
          aria-label="Working on"
        >
          <h3>
            Working on <span className="fe-count">{working.length}</span>
          </h3>
          {working.length ? (
            <ol className="fe-up-next">{working.map(row)}</ol>
          ) : (
            <p className="fe-cockpit-clear">Nothing is running.</p>
          )}
        </section>
        <section className="fe-cockpit-section" aria-label="Up next">
          <h3>
            Up next <span className="fe-count">{next.length}</span>
          </h3>
          {next.length ? (
            <ol className="fe-up-next">{next.slice(0, 10).map(row)}</ol>
          ) : (
            <p className="fe-cockpit-clear">The queue is empty.</p>
          )}
        </section>
        {done.length > 0 && (
          <section className="fe-cockpit-section" aria-label="Just done">
            <h3>Just done</h3>
            <ol className="fe-up-next done">
              {done.map((t) => (
                <li key={t.id}>
                  <button
                    className={"fe-up-next-title" + (t.state === "completed" ? "" : " failed")}
                    onClick={() => onOpenWork(t)}
                    title={title(t)}
                  >
                    {t.state === "completed" ? (
                      <Check size={13} className="fe-done-check" />
                    ) : (
                      <CircleAlert size={13} className="dev-task-failed" />
                    )}
                    <span className="fe-done-text">
                      <span>{title(t)}</span>
                      <small className="fe-muted">
                        {scoped ? taskState(t.state) : nameOf(t.employeeId) + " · " + taskState(t.state)}
                      </small>
                    </span>
                  </button>
                </li>
              ))}
            </ol>
          </section>
        )}
        {spend && spend.pricedJobs + spend.unpricedJobs > 0 && (
          <p className="co-cockpit-foot">
            Spent to date: {dollars(spend.usd)}
            {spend.unpricedJobs > 0 && ` · ${spend.unpricedJobs} unpriced`}
          </p>
        )}
      </div>
    </aside>
  );
}
