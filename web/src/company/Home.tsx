import { useEffect, useRef, useState, type CSSProperties } from "react";
import { Check, MessageSquareText } from "lucide-react";
import type { Employee, Snapshot, Work } from "./api";
import { workGroups, workState, workTitle, type Need } from "./Cockpit";
import { artifactState, employeeState, roleName, taskState } from "./labels";
import { ago, useNow } from "./time";
import { dollars } from "./money";
import { approvedBusinessProfile, businessSetup, homeCards } from "../roles/company";
import { FirstAssignment } from "./FirstAssignment";
import { useMemberAccess } from "./MemberAccess";
import { WorkContext } from "./WorkContext";
import { GoalStart } from "./GoalStart";
import { optionalDraftReview, readyDraft, shiftRound } from "./readyDrafts";
import { isDraftDayLabel } from "./draftCopySections";
import { OutcomeBox, TeamNote } from "./TeamNote";

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
  const lines = body
    .split(/\r?\n/)
    .map((l) => l.trim())
    .filter(Boolean);
  const line = (lines.find((l) => !/^#{1,6}\s|^```|^[-*_]{3,}$/.test(l) && !isDraftDayLabel(l)) ?? lines[0] ?? "")
    .replace(/^(?:#{1,6}\s+|>\s*|[-*+]\s+|\d+[.)]\s+)/, "")
    .replace(/\[([^\]]+)\]\([^)]*\)/g, "$1")
    .replace(/\*\*|__|`/g, "");
  return line.length > max ? line.slice(0, max - 1) + "…" : line;
}

// A steady colour per name, so each employee is recognisable at a glance (UX round 10).
export function avatarStyle(name: string) {
  let hue = 0;
  for (const c of name) hue = (hue * 31 + c.charCodeAt(0)) % 360;
  return { "--hz-hue": String(hue) } as CSSProperties;
}

// One line on what an employee is doing now (UX round 2), shared with the sidebar since round 10.
export function teamStatus(snapshot: Snapshot, needs: Need[], e: Employee, canWork: boolean) {
  if (e.state !== "active") return employeeState(e.state);
  const d = snapshot.data;
  const mine = d.tasks.filter((t) => t.employeeId === e.id);
  const review = needs.filter(
    (n) => n.employeeId === e.id && n.kind === "review" && !optionalDraftReview(snapshot, n),
  ).length;
  const drafts = mine.filter((task) => readyDraft(snapshot, task)).length;
  const completion = needs.filter((n) => n.employeeId === e.id && n.kind === "completion").length;
  const authorizing = mine.some((t) => workState(snapshot, t) === "awaiting_authorization");
  const running = mine.find((t) => (workGroups.progress as readonly string[]).includes(workState(snapshot, t)));
  const queued = mine.filter((t) => (workGroups.next as readonly string[]).includes(t.state)).length;
  if (running) return "Working on " + workTitle(snapshot, running);
  if (authorizing) return canWork ? "Waiting for your authorization" : "Waiting for authorization";
  if (review)
    return review + (review === 1 ? " task waits" : " tasks wait") + (canWork ? " for your review" : " for review");
  if (completion) return completion + (completion === 1 ? " task is" : " tasks are") + " ready to complete";
  if (queued) return queued + " queued";
  if (drafts) return drafts + (drafts === 1 ? " draft ready" : " drafts ready");
  return "Idle";
}

export function isWorking(snapshot: Snapshot, e: Employee) {
  return snapshot.data.tasks.some(
    (t) => t.employeeId === e.id && (workGroups.progress as readonly string[]).includes(workState(snapshot, t)),
  );
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
  onBusiness,
  onResumeHire,
  needsError = "",
  needsLoading = false,
  onRetryNeeds,
  refresh,
  onOpenConversation,
  canPrepare = true,
  outcomeRequest = 0,
  onDesk,
}: {
  snapshot: Snapshot;
  needs: Need[];
  onOpenWork: (task: Work) => void;
  onOpenNeed: (need: Need) => void;
  onChat: (employee: Employee) => void;
  onWork: (employee: Employee) => void;
  onHire: () => void;
  onBusiness?: () => void;
  onResumeHire?: (employee: Employee) => void;
  needsError?: string;
  needsLoading?: boolean;
  onRetryNeeds?: () => void;
  refresh: () => Promise<void>;
  onOpenConversation: (employee: string, thread: string) => void;
  canPrepare?: boolean;
  outcomeRequest?: number;
  // Back to the conversation view of Home (the desk), when there is one.
  onDesk?: () => void;
}) {
  const { canManage, canWork } = useMemberAccess();
  const goalStart = canManage && !!onBusiness;
  const d = snapshot.data;
  const attentionNeeds = needs.filter((need) => !optionalDraftReview(snapshot, need));
  const ready = d.tasks.filter((task) => readyDraft(snapshot, task));
  const work = d.tasks.filter((t) => t.kind !== "conversation");
  const count = (group: readonly string[]) => work.filter((t) => group.includes(workState(snapshot, t))).length;
  const tiles = [
    ["Needs you", needsError ? "?" : needsLoading ? "…" : attentionNeeds.length, attentionNeeds.length > 0],
    ["Drafts ready", ready.length, false],
    ["In progress", count(workGroups.progress), false],
    ["Up next", count(workGroups.next), false],
    ["Done", count(workGroups.done), false],
  ] as const;
  const status = (e: Employee) => teamStatus(snapshot, needs, e, canWork);
  const [allResults, setAllResults] = useState(false);
  const [resultSearch, setResultSearch] = useState("");
  const [allRequests, setAllRequests] = useState(false);
  const [nextGoal, setNextGoal] = useState<{ text: string; request: number }>();
  const outcome = useRef<HTMLDivElement>(null);
  // The cockpit's New outcome brings the owner here, to the one box that starts something new.
  useEffect(() => {
    if (!outcomeRequest) return;
    outcome.current?.scrollIntoView({ block: "center" });
    outcome.current?.querySelector("input")?.focus();
  }, [outcomeRequest]);
  const results = [...d.artifacts]
    .reverse()
    .filter((a) => !["superseded", "revoked"].includes(a.state))
    .map((a) => ({ artifact: a, task: d.tasks.find((t) => t.id === a.taskId) }))
    .filter(
      (r): r is { artifact: (typeof d.artifacts)[number]; task: Work } =>
        !!r.task &&
        !["canceled", "superseded"].includes(r.task.state) &&
        !(r.task.scheduleRunId && shiftRound(snapshot, r.task)),
    )
    .filter((r, index, all) => all.findIndex((other) => other.task.id === r.task.id) === index);
  const nameOf = (id: string) => d.employees.find((e) => e.id === id)?.name ?? "Former employee";
  const matchingResults = results.filter(({ artifact, task }) =>
    [workTitle(snapshot, task), nameOf(task.employeeId), task.input, artifact.body]
      .join(" ")
      .toLocaleLowerCase()
      .includes(resultSearch.trim().toLocaleLowerCase()),
  );
  // A closed tab is not a closed request. Keep unfinished work reachable without browser recovery state.
  const openRequests = [...work]
    .reverse()
    .filter(
      (task) =>
        !["completed", "canceled", "cancelled", "superseded"].includes(task.state) &&
        !d.artifacts.some((a) => a.taskId === task.id && !["superseded", "revoked"].includes(a.state)),
    );
  // Getting started (UX round 6): the path from an empty company to a first approved result, shown until it is done.
  const active = d.employees.find((e) => e.state === "active");
  const onboarding = d.employees.find((e) => e.state === "onboarding");
  const resume = (employee: Employee) => (onResumeHire ? onResumeHire(employee) : onHire());
  const profile = approvedBusinessProfile(snapshot);
  const starter = d.employees
    .filter((employee) => employee.state === "active")
    .map((employee) => ({
      employee,
      setup: businessSetup(snapshot, employee.roleId),
      thread: d.threads.find((t) => t.employeeId === employee.id && (t.scope ?? "employee") === "employee"),
    }))
    .find((candidate) => candidate.setup && candidate.thread);
  // Chat and setup drafts are introductions, not delivered assignments. The checklist keeps its promises modest.
  const assignments = work.filter((t) => (t.kind ?? "work") === "work");
  const finished = assignments.some(
    (t) => t.state === "completed" && d.artifacts.some((a) => a.taskId === t.id && a.state === "approved"),
  );
  const waiting =
    assignments.find(
      (t) =>
        !["completed", "canceled", "superseded"].includes(t.state) &&
        d.artifacts.some((a) => a.taskId === t.id && a.state === "needs_review"),
    ) ??
    assignments.find((t) => t.state === "awaiting_review") ??
    assignments.find((t) => !["completed", "failed", "canceled", "superseded"].includes(t.state)) ??
    assignments.at(-1);
  const steps = [
    ...(onBusiness && (d.employees.length === 0 || d.business)
      ? [
          {
            label: "Confirm your business brief",
            detail: "Review the facts your employees will share.",
            done: !!profile,
            action: [d.business || !canManage ? "Open business brief" : "Set up my business", onBusiness] as const,
          },
        ]
      : []),
    {
      label: "Hire an employee",
      detail: "Choose a role and what they are responsible for.",
      done: d.employees.some((e) => e.state !== "retired"),
      action: canManage ? (["Hire your first employee", onHire] as const) : null,
    },
    {
      label: "Finish onboarding",
      detail: onboarding
        ? `${onboarding.name} needs the shared facts their department works from.`
        : "Write and approve the shared facts their department works from.",
      done: !!active,
      action: canManage ? (["Continue onboarding", () => (onboarding ? resume(onboarding) : onHire())] as const) : null,
    },
    {
      label: "Prepare a first assignment",
      detail: starter
        ? "Review the suggested request below. Preparation does not run a model."
        : "Choose a small piece of work for an active employee.",
      done: assignments.length > 0,
      action: canWork && active && !starter ? (["Ask for work", () => onChat(active)] as const) : null,
    },
    {
      label: "Review and finish a result",
      detail:
        waiting?.state === "awaiting_review"
          ? "Approve the result, then mark the assignment complete."
          : "Open your assignment to authorize it, follow its progress or resolve what is holding it up.",
      done: finished,
      action: waiting
        ? ([
            waiting.state === "awaiting_review" ? "Review first result" : "Open first assignment",
            () => onOpenWork(waiting),
          ] as const)
        : null,
    },
  ];
  const next = steps.findIndex((step) => !step.done);
  const spend = d.spend;
  // The status summary is the employee's note (TeamNote); counts live in the cockpit.
  const now = useNow(60_000);
  const times = d.taskTimes ?? {};
  const hour = new Date(now).getHours();
  return (
    <div className="co-home">
      <header className="co-home-hello">
        <p className="co-home-date">
          {new Date(now).toLocaleDateString(undefined, { weekday: "long", month: "long", day: "numeric" })}
        </p>
        <div className="co-home-hello-row">
          <h2>{hour < 12 ? "Good morning" : hour < 18 ? "Good afternoon" : "Good evening"}</h2>
          {onDesk && (
            <button className="co-primary" onClick={onDesk}>
              <MessageSquareText size={15} aria-hidden="true" /> Back to chat
            </button>
          )}
        </div>
        {/* A first visit says what HireZero does and what happens next (dogfood, 2026-10-30). */}
        {d.employees.length === 0 && d.tasks.length === 0 && (
          <p className="co-home-intro">
            A sentence about your business and one goal is enough. We’ll set up the right employee and bring back a
            private draft you can use.
          </p>
        )}
        {d.employees.length > 0 && (
          <>
            <TeamNote
              snapshot={snapshot}
              needs={needsError ? [] : attentionNeeds}
              canWork={canWork}
              onOpenWork={onOpenWork}
              onOpenNeed={onOpenNeed}
            />
            {goalStart && work.length > 0 && (
              <div ref={outcome}>
                <OutcomeBox
                  onSubmit={(text) => setNextGoal((prior) => ({ text, request: (prior?.request ?? 0) + 1 }))}
                />
              </div>
            )}
          </>
        )}
      </header>
      {homeCards(d.roles, "home-lead").map(({ roleId, Card }) => (
        <Card key={roleId} snapshot={snapshot} refresh={refresh} openWork={onOpenWork} openChat={onChat} />
      ))}
      {results.length > 0 && (
        <section className="co-home-section" aria-label="Recent results">
          <div className="co-home-section-head">
            <h2>Your results</h2>
            {results.length > 3 && (
              <button
                aria-expanded={allResults}
                onClick={() => {
                  setAllResults(!allResults);
                  setResultSearch("");
                }}
              >
                {allResults ? "Show recent results" : `Find all ${results.length} results`}
              </button>
            )}
          </div>
          {allResults && (
            <label className="co-home-search">
              Find saved work
              <input
                type="search"
                value={resultSearch}
                placeholder="Search requests, employees or draft text"
                onChange={(event) => setResultSearch(event.target.value)}
              />
            </label>
          )}
          {allResults && matchingResults.length === 0 && <p>No matching results. Try a different word.</p>}
          <ul className="co-home-list">
            {(allResults ? matchingResults : results.slice(0, 3)).map(({ artifact, task }) => (
              <li key={artifact.id}>
                <button className="co-home-row" onClick={() => onOpenWork(task)}>
                  <span>
                    <b>
                      {workTitle(snapshot, task)} · {nameOf(task.employeeId)}
                    </b>
                    <WorkContext snapshot={snapshot} taskId={task.id} />
                    <small>{firstLine(artifact.body)}</small>
                    {times[task.id]?.updated && (
                      <small>
                        Updated{" "}
                        <time
                          dateTime={times[task.id].updated}
                          title={new Date(times[task.id].updated).toLocaleString()}
                        >
                          {ago(times[task.id].updated, now)}
                        </time>
                      </small>
                    )}
                  </span>
                  <em className={"co-chip state-" + artifact.state}>
                    {["completed", "canceled", "superseded"].includes(task.state) && artifact.state === "needs_review"
                      ? "Earlier draft"
                      : readyDraft(snapshot, task)
                        ? "Draft ready"
                        : artifactState(artifact.state)}
                  </em>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
      {openRequests.length > 0 && (
        <section className="co-home-section" aria-label="Open requests">
          <div className="co-home-section-head">
            <h2>Pick up a request</h2>
            {openRequests.length > 3 && (
              <button aria-expanded={allRequests} onClick={() => setAllRequests(!allRequests)}>
                {allRequests ? "Show recent requests" : `Show all ${openRequests.length} requests`}
              </button>
            )}
          </div>
          <ul className="co-home-list">
            {(allRequests ? openRequests : openRequests.slice(0, 3)).map((task) => (
              <li key={task.id}>
                <button className="co-home-row" onClick={() => onOpenWork(task)}>
                  <span>
                    <b>
                      {workTitle(snapshot, task)} · {nameOf(task.employeeId)}
                    </b>
                    <WorkContext snapshot={snapshot} taskId={task.id} />
                    <small>
                      {task.reason === "revision-awaiting-approval"
                        ? "Revision saved — review before running"
                        : taskState(workState(snapshot, task))}
                    </small>
                  </span>
                  <em className="co-chip">Open request</em>
                </button>
              </li>
            ))}
          </ul>
        </section>
      )}
      {goalStart && (
        <GoalStart
          snapshot={snapshot}
          refresh={refresh}
          openWork={onOpenWork}
          onBusiness={onBusiness}
          onResumeHire={onResumeHire}
          nextGoal={nextGoal}
        />
      )}
      {homeCards(d.roles).map(({ roleId, Card }) => (
        <Card key={roleId} snapshot={snapshot} refresh={refresh} openWork={onOpenWork} openChat={onChat} />
      ))}
      {needsError && (
        <div className="co-needs-error" role="status">
          <p>
            {needsError}{" "}
            {needs.length
              ? "Open the cockpit to see the last checked items."
              : "Retry to check whether anything is waiting for you."}
          </p>
          <button disabled={needsLoading} onClick={onRetryNeeds}>
            {needsLoading ? "Checking needs…" : "Retry needs"}
          </button>
        </div>
      )}
      {!goalStart &&
        (d.employees.length === 0 ? (
          <section className="co-home-welcome" aria-label="Welcome to your company">
            <p className="co-eyebrow">Your company starts here</p>
            <h2>
              {onBusiness
                ? "Start with your business. Then build your team."
                : "Build your team, one employee at a time."}
            </h2>
            <p>
              {!canManage ? (
                "A company owner sets up the business brief and hires the first employee. You can follow the team's progress here."
              ) : (
                <>
                  Confirm your business and review the facts your employees will share. Then choose a role and a small
                  first assignment. You review each result and decide what happens next.
                </>
              )}
            </p>
          </section>
        ) : null)}
      {!goalStart && onBusiness && d.employees.length > 0 && (
        <section className="co-home-welcome" aria-label="Business knowledge">
          <h2>
            {profile ? "Your team has an approved business brief." : "Start with what makes your business yours."}
          </h2>
          <p>
            {canManage
              ? "Confirm your business and review the facts your employees will share. Add sources, correct details, and leave uncertain information unknown."
              : "Read the shared business brief here. A company owner manages its facts and sources."}
          </p>
          <button onClick={onBusiness}>
            {d.business || !canManage ? "Open business brief" : "Set up my business"}
          </button>
        </section>
      )}
      {d.employees.length > 0 && (
        <details className="co-home-section">
          <summary>Workspace activity and costs</summary>
          <section className="co-home-tiles" aria-label="At a glance">
            {tiles.map(([label, value, attention]) =>
              // What needs you is listed in the cockpit; the tile opens the first item.
              label === "Needs you" && attentionNeeds.length > 0 ? (
                <button key={label} className="co-tile attention" onClick={() => onOpenNeed(attentionNeeds[0]!)}>
                  <strong>{value}</strong>
                  <span>{label}</span>
                </button>
              ) : label === "Drafts ready" && ready.length > 0 ? (
                <button key={label} className="co-tile" onClick={() => onOpenWork(ready.at(-1)!)}>
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
              <strong>
                {!spend
                  ? "Unavailable"
                  : spend.unpricedJobs > 0 && spend.pricedJobs === 0
                    ? "Not priced"
                    : dollars(spend.usd)}
              </strong>
              <span>Model costs &amp; estimates</span>
              {spend && spend.unpricedJobs > 0 && (
                <small>
                  {spend.unpricedJobs} {spend.unpricedJobs === 1 ? "job has" : "jobs have"} no dollar price
                </small>
              )}
            </div>
          </section>
        </details>
      )}

      {next >= 0 && !goalStart && (
        <section className="co-home-section" aria-label="Getting started">
          <h2>Getting started</h2>
          {!canManage && (
            <p className="co-muted">
              An owner manages the business brief, hiring and onboarding.
              {canWork
                ? " You can prepare and review work once setup is ready."
                : " An owner or approver prepares work and reviews results."}
            </p>
          )}
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
          {steps[next]?.label === "Prepare a first assignment" &&
            starter?.setup &&
            starter.thread &&
            (canPrepare ? (
              <FirstAssignment
                key={starter.employee.id}
                snapshot={snapshot}
                employeeId={starter.employee.id}
                threadId={starter.thread.id}
                setup={starter.setup}
                refresh={refresh}
                open={onOpenConversation}
                openWork={onOpenWork}
              />
            ) : (
              <p>An owner or approver can prepare the first assignment.</p>
            ))}
        </section>
      )}

      {d.employees.length > 0 && (
        <section className="co-home-section" aria-label="Team">
          <h2>Team</h2>
          <div className="co-team-grid">
            {d.employees.map((e) => (
              <article className="co-team-card" key={e.id}>
                <header>
                  <span className="co-avatar" aria-hidden="true" style={avatarStyle(e.name)}>
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
                  {e.state === "onboarding" && canManage ? (
                    <button
                      className="co-primary"
                      onClick={() => resume(e)}
                      aria-label={"Continue onboarding " + e.name}
                    >
                      Continue onboarding
                    </button>
                  ) : (
                    <button onClick={() => onChat(e)} aria-label={"Chat with " + e.name}>
                      Chat
                    </button>
                  )}
                  <button onClick={() => onWork(e)} aria-label={e.name + "’s work"}>
                    Work
                  </button>
                </div>
              </article>
            ))}
          </div>
        </section>
      )}
    </div>
  );
}
