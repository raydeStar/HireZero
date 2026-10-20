import { Check, UserPlus } from "lucide-react";
import type { Employee, Snapshot, Work } from "./api";
import { workGroups, workTitle, type Need } from "./Cockpit";
import { artifactState, employeeState, roleName } from "./labels";

const dollars = (usd: number) =>
  usd.toLocaleString(undefined, {
    style: "currency",
    currency: "USD",
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });

export function initials(name: string) {
  return (
    name
      .split(/\s+/)
      .filter(Boolean)
      .slice(0, 2)
      .map((part) => part[0]!.toUpperCase())
      .join("") || "?"
  );
}

// The first line worth reading in a result, for a one-line preview.
export function firstLine(body: string, max = 140) {
  const line =
    body
      .split(/\r?\n/)
      .map((l) => l.trim())
      .find((l) => l.length > 0) ?? "";
  return line.length > max ? line.slice(0, max - 1) + "…" : line;
}

// Home (UX round 2, docs/history/2026-10-20-ux-audit.md): the company at a glance. What needs you, what the team is
// doing, and what came back recently. Every item opens the place to act; nothing here approves anything.
export function Home({
  snapshot,
  needs,
  onOpenWork,
  onOpenNeed,
  onChat,
  onWork,
  onHire,
}: {
  snapshot: Snapshot;
  needs: Need[];
  onOpenWork: (task: Work) => void;
  onOpenNeed: (need: Need) => void;
  onChat: (employee: Employee) => void;
  onWork: (employee: Employee) => void;
  onHire: () => void;
}) {
  const d = snapshot.data;
  const work = d.tasks.filter((t) => t.kind !== "conversation");
  const count = (group: readonly string[]) => work.filter((t) => group.includes(t.state)).length;
  const tiles = [
    ["Needs you", needs.length, needs.length > 0],
    ["In progress", count(workGroups.progress), false],
    ["Up next", count(workGroups.next), false],
    ["Done", count(workGroups.done), false],
  ] as const;
  const status = (e: Employee) => {
    if (e.state !== "active") return employeeState(e.state);
    const mine = work.filter((t) => t.employeeId === e.id);
    // Results that still need a decision, not work whose results are already approved.
    const review = d.artifacts.filter((a) => a.employeeId === e.id && a.state === "needs_review").length;
    const running = mine.find((t) => (workGroups.progress as readonly string[]).includes(t.state));
    const queued = mine.filter((t) => (workGroups.next as readonly string[]).includes(t.state)).length;
    if (running) return "Working on " + workTitle(snapshot, running);
    if (review) return review + (review === 1 ? " result waits" : " results wait") + " for your review";
    if (queued) return queued + " queued";
    return "Idle";
  };
  const results = [...d.artifacts]
    .reverse()
    .filter((a) => a.state !== "superseded")
    .slice(0, 6)
    .map((a) => ({ artifact: a, task: d.tasks.find((t) => t.id === a.taskId) }))
    .filter((r): r is { artifact: (typeof d.artifacts)[number]; task: Work } => !!r.task);
  const nameOf = (id: string) => d.employees.find((e) => e.id === id)?.name ?? "Former employee";
  // Getting started (UX round 6): the path from an empty company to a first approved result, shown until it is done.
  const active = d.employees.find((e) => e.state === "active");
  const waiting = d.tasks.find((t) => d.artifacts.some((a) => a.taskId === t.id && a.state === "needs_review"));
  const steps = [
    {
      label: "Hire an employee",
      detail: "Choose a role and what they are responsible for.",
      done: d.employees.length > 0,
      action: ["Hire", onHire] as const,
    },
    {
      label: "Finish onboarding",
      detail: "Write and approve the shared facts their department works from.",
      done: !!active,
      action: ["Continue", onHire] as const,
    },
    {
      label: "Ask for work",
      detail: "Open a chat and ask a question or start an action.",
      done: d.tasks.length > 0,
      action: active ? (["Open chat", () => onChat(active)] as const) : null,
    },
    {
      label: "Approve a first result",
      detail: "Results wait for your review before anything uses them.",
      done: d.artifacts.some((a) => a.state === "approved"),
      action: waiting ? (["Review", () => onOpenWork(waiting)] as const) : null,
    },
  ];
  const next = steps.findIndex((step) => !step.done);
  const spend = d.spend;
  return (
    <div className="co-home">
      <section className="co-home-tiles" aria-label="At a glance">
        {tiles.map(([label, value, attention]) =>
          // What needs you is listed in the cockpit; the tile opens the first item.
          label === "Needs you" && needs.length > 0 ? (
            <button key={label} className="co-tile attention" onClick={() => onOpenNeed(needs[0]!)}>
              <strong>{value}</strong>
              <span>{label}</span>
            </button>
          ) : (
            <div key={label} className={"co-tile" + (attention ? " attention" : "")}>
              <strong>{value}</strong>
              <span>{label}</span>
            </div>
          ),
        )}
        <div className="co-tile">
          <strong>{spend ? dollars(spend.usd) : "$0.00"}</strong>
          <span>Spent to date</span>
        </div>
      </section>

      {next >= 0 && (
        <section className="co-home-section" aria-label="Getting started">
          <h2>Getting started</h2>
          <ol className="co-checklist">
            {steps.map((step, i) => (
              <li key={step.label} className={step.done ? "done" : i === next ? "next" : ""}>
                <span className="co-check-mark" aria-hidden="true">
                  {step.done ? <Check size={13} /> : i + 1}
                </span>
                <span>
                  <b>{step.label}</b>
                  <small>{step.detail}</small>
                </span>
                {!step.done && i === next && step.action && (
                  <button className="co-primary" onClick={step.action[1]}>
                    {step.action[0]}
                  </button>
                )}
                <span className="co-sr">{step.done ? "Done" : i === next ? "Next" : "Later"}</span>
              </li>
            ))}
          </ol>
        </section>
      )}

      <section className="co-home-section" aria-label="Team">
        <h2>Team</h2>
        {d.employees.length === 0 ? (
          <div className="co-home-empty">
            <p>Hire your first employee to start delegating work.</p>
            <button className="co-primary" onClick={onHire}>
              <UserPlus size={15} /> Hire your first employee
            </button>
          </div>
        ) : (
          <div className="co-team-grid">
            {d.employees.map((e) => (
              <article className="co-team-card" key={e.id}>
                <header>
                  <span className="co-avatar" aria-hidden="true">
                    {initials(e.name)}
                    <i className={"co-presence " + e.state} />
                  </span>
                  <span className="co-person-text">
                    <b>{e.name}</b>
                    <small>{roleName(d.roles, e.roleId)}</small>
                  </span>
                </header>
                <p className="co-home-status">{status(e)}</p>
                <div className="co-actions">
                  <button onClick={() => onChat(e)} aria-label={"Chat with " + e.name}>
                    Chat
                  </button>
                  <button onClick={() => onWork(e)} aria-label={e.name + "’s work"}>
                    Work
                  </button>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>

      {results.length > 0 && (
        <section className="co-home-section" aria-label="Recent results">
          <h2>Recent results</h2>
          <ul className="co-home-list">
            {results.map(({ artifact, task }) => (
              <li key={artifact.id}>
                <button className="co-home-row" onClick={() => onOpenWork(task)}>
                  <span>
                    <b>
                      {workTitle(snapshot, task)} · {nameOf(task.employeeId)}
                    </b>
                    <small>{firstLine(artifact.body)}</small>
                  </span>
                  <em className={"co-chip state-" + artifact.state}>{artifactState(artifact.state)}</em>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
    </div>
  );
}
