import type { ReactNode } from "react";
import type { Employee, Snapshot, Work } from "./api";
import { workGroups, workState, workTitle, type Need } from "./Cockpit";
import { roleName } from "./labels";
import { readyDraft } from "./readyDrafts";
import { noteLines } from "../roles/company";
import { ago, elapsed, useNow } from "./time";
import { avatarStyle, initials } from "./Home";

type Line = { key: string; text: ReactNode; action?: [string, () => void] };

// The employee with the most recent work leads Home; the note is one voice, not a roll call.
export function leadEmployee(snapshot: Snapshot): Employee | undefined {
  const d = snapshot.data;
  const times = d.taskTimes ?? {};
  const work = d.tasks.filter((t) => t.kind !== "conversation");
  const latest = (e: Employee) =>
    Math.max(0, ...work.filter((t) => t.employeeId === e.id).map((t) => Date.parse(times[t.id]?.updated ?? "") || 0));
  return [...d.employees.filter((e) => e.state === "active")].sort((a, b) => latest(b) - latest(a))[0];
}

// The employee speaks first (usefulness round, 2026-11-06): on arrival the owner reads a short note in the
// employee's own voice — what they just finished, what they are doing, what comes next and what needs a go-ahead —
// instead of working out where to look. Every line opens the place to act; nothing here approves anything.
export function TeamNote({
  snapshot,
  needs,
  canWork,
  onOpenWork,
  onOpenNeed,
  employee,
}: {
  snapshot: Snapshot;
  needs: Need[];
  canWork: boolean;
  // Whose note: the desk's chosen employee, else whoever has the most recent work.
  employee?: Employee;
  onOpenWork: (task: Work) => void;
  onOpenNeed: (need: Need) => void;
}) {
  const now = useNow(5_000);
  const d = snapshot.data;
  const times = d.taskTimes ?? {};
  const work = d.tasks.filter((t) => t.kind !== "conversation");
  const lead = employee ?? leadEmployee(snapshot);
  if (!lead) return null;
  // Work is named by what the owner asked for (the goal's conversation title), not by the kind of job.
  const named = (t: Work) => {
    const thread = d.threads.find((h) => h.id === t.threadId);
    // Scheduled work keeps its kind ("Morning round"); its thread is named after the schedule, not a goal.
    return !t.scheduleRunId &&
      thread &&
      (thread.scope ?? "employee") === "employee" &&
      !/^Private conversation/.test(thread.title)
      ? thread.title
      : workTitle(snapshot, t);
  };
  const mine = work.filter((t) => t.employeeId === lead.id);
  const inGroup = (group: readonly string[]) => mine.filter((t) => group.includes(workState(snapshot, t)));
  const running = inGroup(workGroups.progress)[0];
  const queued = inGroup(workGroups.next).filter(
    (t) => !["handoff-awaiting-approval", "revision-awaiting-approval"].includes(t.reason ?? ""),
  );
  const ready = mine.filter((t) => readyDraft(snapshot, t));
  const lastReady = ready.at(-1);
  const finished = inGroup(workGroups.done)
    .filter((t) => t.state === "completed")
    .at(-1);
  const waiting = needs.filter((n) => n.employeeId === lead.id || !n.employeeId);
  const lines: Line[] = [];
  if (lastReady)
    lines.push({
      key: "ready",
      text: (
        <>
          I finished <b>{named(lastReady)}</b>
          {times[lastReady.id]?.updated ? " " + ago(times[lastReady.id].updated, now) : ""}. It’s ready for you to read
          {ready.length > 1 ? `, with ${ready.length - 1} more draft${ready.length > 2 ? "s" : ""} behind it` : ""}.
        </>
      ),
      action: ["Read it", () => onOpenWork(lastReady)],
    });
  else if (finished)
    lines.push({
      key: "done",
      text: (
        <>
          Last thing I finished: <b>{named(finished)}</b>
          {times[finished.id]?.updated ? " " + ago(times[finished.id].updated, now) : ""}.
        </>
      ),
    });
  if (running)
    lines.push({
      key: "running",
      text: (
        <>
          Right now I’m working on <b>{named(running)}</b>
          {times[running.id]?.started ? " · " + elapsed(times[running.id].started, now) : ""}.
        </>
      ),
      action: ["Watch", () => onOpenWork(running)],
    });
  if (waiting.length)
    lines.push({
      key: "needs",
      text: (
        <>
          {waiting.length === 1 ? "One thing needs" : `${waiting.length} things need`}{" "}
          {canWork ? "your go-ahead" : "an owner’s go-ahead"}: <b>{waiting[0]!.title}</b>.
        </>
      ),
      action: ["Show me", () => onOpenNeed(waiting[0]!)],
    });
  if (queued.length)
    lines.push({
      key: "next",
      text: (
        <>
          {running ? "After that" : "Next up"}: <b>{named(queued[0]!)}</b>
          {queued.length > 1 ? ` and ${queued.length - 1} more` : ""}.
        </>
      ),
      action: ["Open", () => onOpenWork(queued[0]!)],
    });
  if (!running && !queued.length)
    lines.push({
      key: "free",
      text: mine.length
        ? "I’m free. Tell me the outcome you want next and I’ll bring back a first draft."
        : "I’m ready for my first assignment. Tell me the outcome you want and I’ll bring back a first draft.",
    });
  return (
    <section className="co-team-note" aria-label={lead.name + "’s note"}>
      <span className="co-avatar co-team-note-avatar" aria-hidden="true" style={avatarStyle(lead.name)}>
        {initials(lead.name)}
        <i className={"co-presence " + (running ? "working" : lead.state)} />
      </span>
      <div className="co-team-note-body">
        <p className="co-team-note-who">
          <b>{lead.name}</b> · {roleName(d.roles, lead.roleId)}
          {running && <span className="co-team-note-live">working</span>}
        </p>
        <ul>
          {lines
            .filter((line) => line.key !== "free")
            .map((line) => (
              <li key={line.key} className={"co-team-note-line " + line.key}>
                <span>{line.text}</span>
                {line.action && (
                  <button className={line.key === "needs" ? "co-primary" : ""} onClick={line.action[1]}>
                    {line.action[0]}
                  </button>
                )}
              </li>
            ))}
          {/* What the employee's role adds in their own words, such as today's focus from a shift's round. A role line
              marked co-note-onshift replaces "I'm free". */}
          {noteLines(d.roles).map(({ roleId, Line }) => (
            <Line key={roleId} snapshot={snapshot} employee={lead} />
          ))}
          {lines
            .filter((line) => line.key === "free")
            .map((line) => (
              <li key={line.key} className="co-team-note-line free">
                <span>{line.text}</span>
              </li>
            ))}
        </ul>
      </div>
    </section>
  );
}

const examples = ["I really need to market my new book", "Get more people in the door this week", "Promote our event"];

// One box for a new outcome. It fills the goal below and opens it; the run's model, cost and consent stay there.
export function OutcomeBox({ onSubmit, disabled }: { onSubmit: (text: string) => void; disabled?: boolean }) {
  return (
    <form
      className="co-outcome"
      aria-label="New outcome"
      onSubmit={(event) => {
        event.preventDefault();
        const field = event.currentTarget.elements.namedItem("outcome") as HTMLInputElement;
        const text = field.value.trim();
        if (!text) return field.focus();
        onSubmit(text);
        field.value = "";
      }}
    >
      <label htmlFor="co-outcome-input">What do you want to happen next?</label>
      <div className="co-outcome-row">
        <input
          id="co-outcome-input"
          name="outcome"
          autoComplete="off"
          maxLength={600}
          placeholder={examples[0]}
          disabled={disabled}
        />
        <button className="co-primary" disabled={disabled}>
          Plan it
        </button>
      </div>
      <div className="co-outcome-examples" aria-label="Examples">
        {examples.slice(1).map((example) => (
          <button key={example} type="button" disabled={disabled} onClick={() => onSubmit(example)}>
            {example}
          </button>
        ))}
      </div>
      <small>You’ll see exactly what runs and the most it can cost before anything starts.</small>
    </form>
  );
}
