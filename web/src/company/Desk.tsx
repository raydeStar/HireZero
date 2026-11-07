import { customerView } from "./audience";
import { useEffect, useRef, useState } from "react";
import { ChevronsDownUp, ChevronsUpDown, LayoutDashboard, Plus } from "lucide-react";
import { api } from "../lib/api";
import type { Employee, Event, Snapshot, ThreadDetail, Work } from "./api";
import { CompanyConversation, type ConversationDraft } from "./CompanyConversation";
import type { Need } from "./Cockpit";
import { avatarStyle, initials, teamStatus } from "./Home";
import { roleName } from "./labels";
import { homeActions, homeCards } from "../roles/company";
import { useMemberAccess } from "./MemberAccess";
import { foldAll } from "./Fold";
import { leadEmployee, TeamNote } from "./TeamNote";
import { startedWork } from "./GoalStart";
import { CatchUp, catchUpNews, useLastSeen } from "./CatchUp";

// The conversation the desk opens: the employee's private thread with the most recent hands-on work (a goal's
// thread, say), else their first private conversation. Scheduled rounds lead through their own card instead.
export function deskThread(snapshot: Snapshot, employee: Employee): string | undefined {
  const d = snapshot.data;
  const mine = d.threads.filter((t) => t.employeeId === employee.id && (t.scope ?? "employee") === "employee");
  if (!mine.length) return undefined;
  const times = d.taskTimes ?? {};
  const updated = (t: Work) => Date.parse(times[t.id]?.updated ?? "") || 0;
  const recent = d.tasks
    .filter((t) => t.employeeId === employee.id && !t.scheduleRunId && mine.some((m) => m.id === t.threadId))
    .sort((a, b) => updated(b) - updated(a))[0];
  return recent?.threadId ?? mine[0]!.id;
}

// The desk needs an active employee with a private conversation, and some work begun.
export function deskReady(snapshot: Snapshot) {
  const lead = leadEmployee(snapshot);
  return !!lead && !!deskThread(snapshot, lead) && snapshot.data.tasks.some(startedWork);
}

// Home is the conversation once work has begun (owner feedback, 2026-11-06): the lead employee's chat fills the page,
// with their note, today's round and the shift control around it. The overview keeps everything Home showed before.
export function Desk({
  snapshot,
  events,
  needs,
  needsError,
  busy,
  memberRole,
  drafts,
  updateDraft,
  run,
  refresh,
  onOpenWork,
  onOpenNeed,
  onOpenConversation,
  onOverview,
  onNewOutcome,
  onOpenPlans,
  onRememberResult,
  onRepeatWork,
  onOpenSetup,
}: {
  snapshot: Snapshot;
  events: Event[];
  needs: Need[];
  needsError: string;
  busy: boolean;
  memberRole?: string;
  drafts: Map<string, ConversationDraft>;
  updateDraft: (scope: string, update: (current: ConversationDraft | undefined) => ConversationDraft) => void;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  refresh: () => Promise<void>;
  onOpenWork: (task: Work) => void;
  onOpenNeed: (need: Need) => void;
  onOpenConversation: (employeeId: string, threadId: string) => void;
  onOverview: () => void;
  onNewOutcome?: () => void;
  onOpenPlans: () => void;
  onRememberResult?: (artifactId: string) => void;
  onRepeatWork?: (taskId: string) => void;
  onOpenSetup: () => void;
}) {
  const { canWork } = useMemberAccess();
  const d = snapshot.data;
  const active = d.employees.filter((e) => e.state === "active");
  const [who, setWho] = useState("");
  const employee = active.find((e) => e.id === who) ?? leadEmployee(snapshot)!;
  // The desk follows the newest work until the owner picks a conversation themselves.
  const [chosen, setChosen] = useState<{ employee: string; thread: string }>();
  const thread =
    chosen?.employee === employee.id && d.threads.some((t) => t.id === chosen.thread)
      ? chosen.thread
      : (deskThread(snapshot, employee) ?? "");
  const [detail, setDetail] = useState<ThreadDetail | null>(null);
  const [loadError, setLoadError] = useState("");
  const request = useRef(0);
  useEffect(() => {
    const id = ++request.current;
    if (!thread) return;
    api<ThreadDetail>("/company/employees/" + employee.id + "/threads/" + thread)
      .then((next) => {
        if (id !== request.current) return;
        setDetail(next);
        setLoadError("");
      })
      .catch((e) => id === request.current && setLoadError((e as Error).message));
  }, [employee.id, thread, d.cursor]);
  const root = useRef<HTMLDivElement>(null);
  const [folded, setFolded] = useState(false);
  const cardProps = {
    snapshot,
    refresh,
    openWork: onOpenWork,
    openChat: (e: Employee) => setWho(e.id),
    openOverview: onOverview,
    placement: "chat" as const,
    threadId: thread,
    // Answering the employee's question starts a message that quotes it, ready to finish and send.
    onAnswer: (question: string) => {
      updateDraft(employee.id + ":" + thread, (previous) => ({
        ...(previous ?? { body: "", skill: "chat", brief: {}, evidence: [], recipient: "", delivery: "send" }),
        body: `About your question ("${question}"): `,
      }));
      requestAnimationFrame(() => {
        const box = root.current?.querySelector<HTMLTextAreaElement>(".fe-composer textarea");
        box?.focus();
        box?.setSelectionRange(box.value.length, box.value.length);
      });
    },
  };
  const attention = needsError ? [] : needs;
  const [since, arrived] = useLastSeen(employee.id);
  const news = catchUpNews(snapshot, employee, since, arrived);
  return (
    <div className="co-desk" ref={root}>
      <header className="co-desk-head">
        <span className="co-avatar" aria-hidden="true" style={avatarStyle(employee.name)}>
          {initials(employee.name)}
        </span>
        <span className="co-desk-who">
          {active.length > 1 ? (
            <select
              aria-label="Talk with"
              value={employee.id}
              onChange={(e) => {
                setWho(e.target.value);
                setChosen(undefined);
              }}
            >
              {active.map((e) => (
                <option key={e.id} value={e.id}>
                  {e.name}
                </option>
              ))}
            </select>
          ) : (
            <b>{employee.name}</b>
          )}
          <small>
            {roleName(d.roles, employee.roleId)} · {teamStatus(snapshot, attention, employee, canWork)}
          </small>
        </span>
        <span className="co-desk-spacer" />
        {homeActions(d.roles).map(({ roleId, Action }) => (
          <Action key={roleId} {...cardProps} />
        ))}
        {onNewOutcome && (
          <button onClick={onNewOutcome} title="Start a new goal with its own plan" aria-label="New outcome">
            <Plus size={15} aria-hidden="true" /> <span className="co-desk-label">New outcome</span>
          </button>
        )}
        <button
          onClick={onOverview}
          title={customerView() ? "Results, goal ladder and team" : "Results, goal ladder, team and costs"}
          aria-label="Overview"
        >
          <LayoutDashboard size={15} aria-hidden="true" /> <span className="co-desk-label">Overview</span>
        </button>
      </header>
      {loadError && (
        <p className="co-error" role="status">
          This conversation could not refresh. {loadError} Your saved work is kept.
        </p>
      )}
      <CompanyConversation
        key={employee.id + ":" + thread}
        memberRole={memberRole}
        snapshot={snapshot}
        company={false}
        events={events}
        employee={employee}
        thread={thread}
        detail={detail}
        busy={busy}
        drafts={drafts}
        updateDraft={updateDraft}
        run={run}
        refresh={refresh}
        open={(employeeId, threadId) =>
          employeeId === employee.id
            ? setChosen({ employee: employeeId, thread: threadId })
            : onOpenConversation(employeeId, threadId)
        }
        onShowWork={onOpenWork}
        onOpenPlans={onOpenPlans}
        onRememberResult={onRememberResult}
        onRepeatWork={onRepeatWork}
        onOpenSetup={onOpenSetup}
        createThread={async () => {
          const result = (await run("thread.create", {
            employeeId: employee.id,
            name: "Private conversation",
          })) as { id: string } | undefined;
          if (result) setChosen({ employee: employee.id, thread: result.id });
        }}
        toolbar={
          <button
            className="co-ghost co-desk-fold-all"
            aria-label={folded ? "Expand all" : "Collapse all"}
            title={folded ? "Expand all" : "Collapse all"}
            onClick={() => {
              foldAll(folded, root.current?.querySelector(".fe-thread") ?? null);
              setFolded(!folded);
            }}
          >
            {folded ? <ChevronsUpDown size={14} aria-hidden="true" /> : <ChevronsDownUp size={14} aria-hidden="true" />}
            <span className="co-toolbar-text">{folded ? "Expand all" : "Collapse all"}</span>
          </button>
        }
        trailing={
          <>
            <div className="co-desk-lead">
              {homeCards(d.roles, "home-lead").map(({ roleId, Card }) => (
                <Card key={roleId} {...cardProps} />
              ))}
              {homeCards(d.roles).map(({ roleId, Card }) => (
                <Card key={"card:" + roleId} {...cardProps} />
              ))}
            </div>
            {news && (
              <CatchUp
                snapshot={snapshot}
                employee={employee}
                news={news}
                needs={attention}
                onOpenWork={onOpenWork}
                onOpenNeed={onOpenNeed}
              />
            )}
            <div className="co-desk-note">
              <TeamNote
                quiet={!!news}
                snapshot={snapshot}
                needs={attention}
                canWork={canWork}
                onOpenWork={onOpenWork}
                onOpenNeed={onOpenNeed}
                employee={employee}
              />
            </div>
          </>
        }
      />
    </div>
  );
}
