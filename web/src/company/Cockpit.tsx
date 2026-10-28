import { useEffect, useState } from "react";
import { Check, CircleAlert, PanelRightClose, X } from "lucide-react";
import { api } from "../lib/api";
import type { Snapshot, Work } from "./api";
import { taskState, workName } from "./labels";
import { ago, elapsed, useNow } from "./time";
import { dollars } from "./money";
import { useMemberAccess } from "./MemberAccess";
import { WorkContext } from "./WorkContext";
import { optionalDraftReview } from "./readyDrafts";

export type Need = {
  kind: string;
  title: string;
  detail: string;
  employeeId?: string;
  taskId?: string;
  targetId?: string;
  // The conversation an item lives in (a Connect card).
  threadId?: string | null;
  key: string;
  priority: string;
};

// How work is grouped in the cockpit and on the Work board (docs/adr/0040-the-muse-layout.md).
export const workGroups = {
  review: ["awaiting_review", "awaiting_authorization", "unknown"],
  progress: ["running", "gathering"],
  next: ["queued", "blocked"],
  done: ["completed", "failed", "cancelled", "canceled", "withheld"],
} as const;
const cancellable = ["queued", "blocked", "running"];

// A claimed task has not necessarily started: the sealed proposal still waits for the owner's starting bell.
export function workState(snapshot: Snapshot, task: Work) {
  if (
    ["queued", "blocked", "running", "awaiting_review"].includes(task.state) &&
    snapshot.data.browserRunningTaskIds?.includes(task.id)
  )
    return "running";
  return task.state === "running" &&
    snapshot.data.measured?.some((a) => a.taskId === task.id && a.job.state === "proposed")
    ? "awaiting_authorization"
    : task.state;
}

export function useNeeds(version: number) {
  const [needs, setNeeds] = useState<Need[]>([]);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [attempt, setAttempt] = useState(0);
  useEffect(() => {
    let current = true;
    setLoading(true);
    api<{ needsYou: Need[] }>("/company/today")
      .then((today) => {
        if (!current) return;
        setNeeds(today.needsYou);
        setError("");
      })
      .catch(() => current && setError("Couldn’t update items needing attention."))
      .finally(() => current && setLoading(false));
    return () => {
      current = false;
    };
  }, [version, attempt]);
  return { needs, error, loading, retry: () => setAttempt((value) => value + 1) };
}

export function workTitle(snapshot: Snapshot, task: Work) {
  if (task.scheduleRunId && snapshot.data.browserTaskIds?.includes(task.id)) return "Scheduled website review";
  if (task.scheduleRunId && snapshot.data.sandboxTaskIds?.includes(task.id)) return "Scheduled script report";
  // A context draft runs as one reply, but it is drafting context, not a conversation (owner feedback, 2026-10-20).
  if (task.kind === "context-draft") return task.contextScope === "company" ? "Company facts draft" : "Context draft";
  if (task.planId && task.kind === "work" && task.skill === "chat") {
    const title = task.input.trim().split(/\r?\n/)[0];
    return title && !/^[\[{]/.test(title) ? title.slice(0, 90) + (title.length > 90 ? "…" : "") : "Plan assignment";
  }
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
  needsError = "",
  needsLoading = false,
  onRetryNeeds,
}: {
  snapshot: Snapshot;
  needs: Need[];
  scope?: string;
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  onOpenWork: (task: Work) => void;
  onOpenNeed: (need: Need) => void;
  onClose: () => void;
  needsError?: string;
  needsLoading?: boolean;
  onRetryNeeds?: () => void;
}) {
  const { canWork } = useMemberAccess();
  const d = snapshot.data;
  const [everyone, setEveryone] = useState(false);
  const now = useNow(15_000);
  const scoped = scope && !everyone ? d.employees.find((e) => e.id === scope) : undefined;
  const mine = (employeeId?: string) => !scoped || employeeId === scoped.id;
  const tasks = d.tasks.filter((t) => mine(t.employeeId));
  const inGroup = (group: readonly string[]) => tasks.filter((t) => group.includes(workState(snapshot, t)));
  const ready = needs.filter((n) => mine(n.employeeId) && optionalDraftReview(snapshot, n));
  const shownNeeds = needs.filter((n) => mine(n.employeeId) && !optionalDraftReview(snapshot, n));
  const working = inGroup(workGroups.progress);
  const next = inGroup(workGroups.next).filter(
    (t) => !["handoff-awaiting-approval", "revision-awaiting-approval"].includes(t.reason ?? ""),
  );
  const done = inGroup(workGroups.done)
    .filter((task) => !(task.meetingId && task.state === "completed"))
    .slice(-5)
    .reverse();
  const nameOf = (id: string) => d.employees.find((e) => e.id === id)?.name ?? "Former employee";
  const title = (t: Work) => workTitle(snapshot, t);
  const cancel = (t: Work) =>
    canWork &&
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
    return (workGroups.progress as readonly string[]).includes(workState(snapshot, t))
      ? " · " + elapsed(times.started, now)
      : " · " + ago(times.updated, now);
  };
  const row = (t: Work) => (
    <li key={t.id}>
      <button className="fe-up-next-title" onClick={() => onOpenWork(t)} title={title(t)}>
        <span>{title(t)}</span>
        <WorkContext snapshot={snapshot} taskId={t.id} />
        <small>
          {(scoped
            ? taskState(workState(snapshot, t))
            : nameOf(t.employeeId) + " · " + taskState(workState(snapshot, t))) + when(t)}
        </small>
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
        <section className="fe-cockpit-section co-needs" aria-label={canWork ? "Needs you" : "Needs attention"}>
          <h3>
            {canWork ? "Needs you" : "Needs attention"}{" "}
            <span className={"fe-count" + (shownNeeds.length ? " attn" : "")}>
              {needsError ? "?" : needsLoading ? "…" : shownNeeds.length}
            </span>
          </h3>
          {needsError ? (
            <div className="co-needs-error" role="status">
              <p>
                {needsError} {shownNeeds.length > 0 && "Showing the last checked items."}
              </p>
              <button disabled={needsLoading} onClick={onRetryNeeds}>
                {needsLoading ? "Checking needs…" : "Retry needs"}
              </button>
            </div>
          ) : (
            needsLoading && <p role="status">Checking items needing attention…</p>
          )}
          {shownNeeds.length ? (
            <ol className="fe-up-next">
              {shownNeeds.map((n) => (
                <li key={n.key} className={"co-need priority-" + n.priority}>
                  <button
                    className="fe-up-next-title"
                    onClick={() => onOpenNeed(n)}
                    title={canWork ? n.detail : n.detail.replace(/\byour /g, "")}
                  >
                    <span>{n.title}</span>
                    <WorkContext snapshot={snapshot} taskId={n.taskId} />
                    <small>{canWork ? n.detail : n.detail.replace(/\byour /g, "")}</small>
                  </button>
                </li>
              ))}
            </ol>
          ) : (
            !needsError &&
            !needsLoading && (
              <p className="fe-cockpit-clear">
                <Check size={14} /> {canWork ? "Nothing needs you." : "Nothing needs attention."}
              </p>
            )
          )}
        </section>
        {ready.length > 0 && (
          <section className="fe-cockpit-section" aria-label="Drafts ready">
            <h3>
              Drafts ready <span className="fe-count">{ready.length}</span>
            </h3>
            <p className="fe-muted">
              Open, copy or refine. Sharing with the team, handoffs and schedules still need approval.
            </p>
            <ol className="fe-up-next">
              {ready.map((need) => (
                <li key={need.key}>
                  <button className="fe-up-next-title" onClick={() => onOpenNeed(need)}>
                    <span>{need.title}</span>
                    <WorkContext snapshot={snapshot} taskId={need.taskId} />
                    <small>Draft ready</small>
                  </button>
                </li>
              ))}
            </ol>
          </section>
        )}
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
                      <WorkContext snapshot={snapshot} taskId={t.id} />
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
            Model costs &amp; estimates: {spend.pricedJobs === 0 ? "Not priced" : dollars(spend.usd)}
            {spend.unpricedJobs > 0 && ` · ${spend.unpricedJobs} unpriced`}
          </p>
        )}
      </div>
    </aside>
  );
}
