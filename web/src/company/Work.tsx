import { Maximize2, MessageSquareText, X } from "lucide-react";
import { useState } from "react";
import type { Snapshot, Work } from "./api";
import { ConversationJob } from "./ConversationJob";
import { workGroups, workState, workTitle } from "./Cockpit";
import { taskState } from "./labels";
import { ago, useNow } from "./time";
import { useMemberAccess } from "./MemberAccess";
import { WorkContext } from "./WorkContext";

const sections = [
  ["Needs review", workGroups.review],
  ["In progress", workGroups.progress],
  ["Up next", workGroups.next],
  ["Done", workGroups.done],
  ["Replaced", ["superseded"]],
] as const;

// The Work board (docs/adr/0040-the-muse-layout.md): a conversation's work by what it needs next.
export function WorkBoard({
  snapshot,
  tasks,
  everyone,
  open,
  onOpen,
}: {
  snapshot: Snapshot;
  tasks: Work[];
  everyone: boolean;
  open: string;
  onOpen: (task: Work) => void;
}) {
  const { canWork } = useMemberAccess();
  const d = snapshot.data;
  const nameOf = (id: string) => d.employees.find((e) => e.id === id)?.name ?? "Former employee";
  const work = tasks.filter((t) => t.kind !== "conversation");
  const now = useNow();
  const [doneLimit, setDoneLimit] = useState(12);
  // What was asked, in a line (UX round 8); structured action input is left to the work window.
  const asked = (t: Work) => {
    const line = t.input.trim().split(/\r?\n/)[0] ?? "";
    if (!line || line.startsWith("{") || line.startsWith("[") || t.kind === "context-draft") return "";
    return line.length > 90 ? line.slice(0, 90) + "…" : line;
  };
  return (
    <section className="co-work-board" aria-label="Work">
      {work.length === 0 && (
        <p className="fe-cockpit-clear">
          {canWork
            ? "No work yet. Ask for something in Chat, or start an action."
            : "No work has been saved in this conversation yet."}
        </p>
      )}
      {sections.map(([label, states]) => {
        const items = work.filter((t) => (states as readonly string[]).includes(workState(snapshot, t)));
        const shown = label === "Done" ? items.slice(-doneLimit).reverse() : items;
        return (
          items.length > 0 && (
            <div key={label} className="co-work-group" role="group" aria-label={label}>
              <h3>
                {label} <span className="fe-count">{items.length}</span>
              </h3>
              <ul>
                {shown.map((t) => (
                  <li key={t.id}>
                    <button
                      className="co-work-item"
                      aria-current={open === t.id ? "true" : undefined}
                      onClick={() => onOpen(t)}
                    >
                      <span>{workTitle(snapshot, t)}</span>
                      <WorkContext snapshot={snapshot} taskId={t.id} />
                      {asked(t) && <q className="co-work-asked">{asked(t)}</q>}
                      <small>
                        {(everyone ? nameOf(t.employeeId) + " · " : "") + taskState(workState(snapshot, t), canWork)}
                        {t.reason && t.state !== "completed" ? " · " + t.reason.replaceAll("-", " ") : ""}
                        {d.taskTimes?.[t.id] ? " · " + ago(d.taskTimes[t.id]!.updated, now) : ""}
                      </small>
                    </button>
                  </li>
                ))}
              </ul>
              {label === "Done" && shown.length < items.length && (
                <button className="co-work-older" onClick={() => setDoneLimit((limit) => limit + 12)}>
                  Show earlier work ({items.length - shown.length} more)
                </button>
              )}
            </div>
          )
        );
      })}
    </section>
  );
}

// The work window: the same job card the conversation shows, with its review, execution and publication steps.
export function WorkWindow({
  snapshot,
  task,
  busy,
  run,
  refresh,
  open,
  onChat,
  onExpand,
  onClose,
  onOpenResult,
  onOpenPlans,
  onRememberResult,
  onRepeatWork,
}: {
  snapshot: Snapshot;
  task: Work;
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  refresh: () => Promise<void>;
  open: (employeeId: string, threadId: string) => void;
  onChat?: () => void;
  onExpand?: () => void;
  onClose: () => void;
  onOpenResult?: (task: Work) => void;
  onOpenPlans?: () => void;
  onRememberResult?: (artifactId: string) => void;
  onRepeatWork?: (taskId: string) => void;
}) {
  const { canWork } = useMemberAccess();
  const employee = snapshot.data.employees.find((e) => e.id === task.employeeId);
  return (
    <section className="fe-window co-work-window" tabIndex={-1} aria-label={"Work: " + workTitle(snapshot, task)}>
      <header className="co-work-window-head">
        <div>
          <strong>{workTitle(snapshot, task)}</strong>
          <WorkContext snapshot={snapshot} taskId={task.id} />
          <small>
            {(employee?.name ?? "Former employee") +
              " · " +
              (task.state === "awaiting_review" &&
              snapshot.data.artifacts.some((artifact) => artifact.taskId === task.id)
                ? "Draft ready"
                : taskState(workState(snapshot, task), canWork))}
          </small>
        </div>
        {onExpand && (
          <button className="fe-icon-button" aria-label="Expand work" title="Expand work" onClick={onExpand}>
            <Maximize2 size={16} />
          </button>
        )}
        {onChat && (
          <button className="fe-icon-button" aria-label="Show in chat" title="Show in chat" onClick={onChat}>
            <MessageSquareText size={16} />
          </button>
        )}
        <button className="fe-icon-button" aria-label="Close work" title="Close" onClick={onClose}>
          <X size={16} />
        </button>
      </header>
      <div className="co-work-window-body">
        {task.kind === "conversation" ? (
          <p className="fe-muted">This is a reply in the conversation. Its review and send steps are in Chat.</p>
        ) : employee ? (
          <ConversationJob
            focused
            task={task}
            snapshot={snapshot}
            busy={busy}
            run={run}
            refresh={refresh}
            open={open}
            onOpenResult={onOpenResult}
            onOpenPlans={onOpenPlans}
            onRememberResult={onRememberResult}
            onRepeatWork={onRepeatWork}
          />
        ) : (
          <p className="fe-muted">This work belonged to an employee who is no longer here.</p>
        )}
      </div>
    </section>
  );
}
