import { useEffect, useRef, useState, type ReactNode } from "react";
import { Play, X } from "lucide-react";
import { api, company, memberStorage } from "../../lib/api";
import {
  command,
  reconcileCompanyOperation,
  UnconfirmedCompanyOperation,
  type Employee,
  type Snapshot,
  type Work,
} from "../../company/api";
import { valuesOf } from "../../company/contextDraft";
import { DraftMarkdown } from "../../company/DraftMarkdown";
import { advanceGoal, currentGoalWork, type GoalFlow, type GoalTerms } from "../../company/goalFlow";
import { useMemberAccess } from "../../company/MemberAccess";
import { providerName } from "../../company/ModelChoice";
import { dollars, spendText } from "../../company/allowance";
import { Modal } from "../../company/Modal";
import { Fold } from "../../company/Fold";
import { avatarStyle, firstLine, initials } from "../../company/Home";
import { EmployeeSays } from "../../company/EmployeeSays";
import type { HomeCardProps } from "../company";
import { GoalLadder } from "./GoalLadder";
import { ProposalCorrection, ProposalFeedback, useProposalFeedback } from "../../company/ProposalFeedback";
import "./ladder.css";
import { ladderSkill } from "./ladder";
import { goalTitle, morningSkill, nextRoundLabel, openQuestion, parseMorningReport, type ShiftJob } from "./morning";

type Shift = {
  id: string;
  version: number;
  digest: string;
  state: string;
  consumed: number;
  nextRunAt: string | null;
  threadId?: string | null;
  reviewCurrent?: boolean | null;
  reason?: string | null;
  definition: { employeeId?: string | null; skill?: string | null; occurrences: number; localTime: string };
};

// The shift's state for the Marketing employee: its morning-round schedules, refreshed with the company's events.
function useShift(props: HomeCardProps) {
  const d = props.snapshot.data;
  const role = d.roles.find((r) => r.uiSlots?.includes("home-card") && r.measuredSkills?.includes(morningSkill));
  const employee = d.employees.find((e) => e.roleId === role?.id && e.state === "active");
  const [shifts, setShifts] = useState<Shift[]>();
  const [reload, setReload] = useState(0);
  const [loadError, setLoadError] = useState(false);
  const [loading, setLoading] = useState(false);
  useEffect(() => {
    let active = true;
    if (employee) {
      setLoading(true);
      void api<Shift[]>("/company/schedules")
        .then((all) => {
          if (!active) return;
          setShifts(all.filter((s) => s.definition.skill === morningSkill && s.definition.employeeId === employee.id));
          setLoadError(false);
        })
        .catch(() => active && setLoadError(true))
        .finally(() => active && setLoading(false));
    }
    return () => {
      active = false;
    };
  }, [employee?.id, d.cursor, reload]);
  const current =
    shifts?.filter((s) => s.state === "active").at(-1) ??
    shifts?.filter((s) => ["paused", "blocked"].includes(s.state)).at(-1);
  // An ended shift keeps leading Home for a day and a half after its last round, so its jobs stay at hand.
  const ended = current ? undefined : shifts?.filter((s) => ["completed", "canceled"].includes(s.state)).at(-1);
  const last = ended && d.tasks.filter((t) => t.threadId === ended.threadId && t.skill === morningSkill).at(-1);
  const started = Date.parse((last && d.taskTimes?.[last.id]?.started) || "");
  const shift = current ?? (Date.now() - started < 36 * 3_600_000 ? ended : undefined);
  const changed = async () => {
    setReload((n) => n + 1);
    await props.refresh();
  };
  return {
    role,
    employee,
    shift,
    loaded: !!shifts && !loadError,
    loadError,
    loading,
    retry: () => setReload((n) => n + 1),
    changed,
  };
}

// Home's lead (docs/adr/0171-an-owner-approved-shift.md): once a shift exists, its latest morning round comes right
// after the greeting, before earlier results.
export function MarketingLead(props: HomeCardProps) {
  const { role, employee, shift, loaded, loadError, loading, retry, changed } = useShift(props);
  if (role && employee && loadError)
    return (
      <section className="co-shift" aria-label="Morning work unavailable">
        <h2>Morning work couldn’t be checked</h2>
        <p role="status">We couldn’t load {employee.name}’s shift. Your saved work hasn’t changed.</p>
        <button disabled={loading} onClick={retry}>
          {loading ? "Checking morning work…" : "Retry morning work"}
        </button>
      </section>
    );
  return role && employee ? (
    <ShiftCard {...props} employee={employee} roleId={role.id} shift={shift} loaded={loaded} changed={changed} />
  ) : null;
}

// The desk's header control (2026-11-06): the shift's state at a glance, and one button that starts a shift.
export function MarketingShiftAction(props: HomeCardProps) {
  const { role, employee, shift, loaded, changed } = useShift(props);
  const { canManage } = useMemberAccess();
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  if (!role || !employee || !loaded) return null;
  const d = props.snapshot.data;
  const department = d.departments.find((x) => x.id === employee.departmentId);
  const ladder = valuesOf(d.knowledge.find((k) => k.id === department?.knowledge && k.state === "approved"));
  const ready = !!ladder.goal?.trim() && !!ladder.steps?.trim();
  const running = shift && ["active", "paused", "blocked"].includes(shift.state) && shift.reviewCurrent !== false;
  const review = async (decision: string) => {
    if (!shift) return;
    setBusy(true);
    setError("");
    try {
      await command("schedule.review", {
        id: shift.id,
        expectedVersion: shift.version,
        digest: shift.digest,
        decision,
      });
      await changed();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  if (running) {
    const left = shift.definition.occurrences - shift.consumed;
    const next = shift.state === "active" && left > 0 ? nextRoundLabel(shift.nextRunAt, new Date()) : "";
    return (
      <div className="co-desk-shift" role="group" aria-label="Shift">
        <span className={"co-desk-shift-state" + (shift.state === "active" ? " live" : "")}>
          {shift.state === "active" ? "On shift" : "Shift paused"}
          <small>
            {shift.consumed} of {shift.definition.occurrences} done{next ? ` · next ${next}` : ""}
          </small>
        </span>
        {canManage && (
          <button
            disabled={busy}
            onClick={() => void review(shift.state === "active" ? "pause" : "authorize-recurring-soft-limits")}
          >
            {shift.state === "active" ? "Pause shift" : "Resume shift"}
          </button>
        )}
        {error && (
          <span role="alert" className="co-desk-shift-error">
            {error}
          </span>
        )}
      </div>
    );
  }
  if (!canManage) return null;
  return (
    <>
      <button className="co-primary co-desk-start" onClick={() => setOpen(true)}>
        <Play size={14} aria-hidden="true" /> Start shift
      </button>
      {open && (
        <Modal label="Start a shift" className="co-dialog-layer" onClose={() => setOpen(false)}>
          <section className="co-dialog co-shift-dialog">
            <header>
              <h2>Put {employee.name} on shift</h2>
              <button className="fe-icon-button" aria-label="Close" onClick={() => setOpen(false)}>
                <X size={16} />
              </button>
            </header>
            {ready ? (
              <Offer
                snapshot={props.snapshot}
                employee={employee}
                roleId={role.id}
                changed={async () => {
                  await changed();
                  setOpen(false);
                }}
                stale={shift}
                quick
              />
            ) : (
              <>
                <p>
                  Each round works from {employee.name}’s goal ladder: the goal and the steps toward it. Approve a
                  ladder first, then start the shift.
                </p>
                {props.openOverview && (
                  <div className="co-ladder-actions">
                    <button
                      className="co-primary"
                      onClick={() => {
                        setOpen(false);
                        props.openOverview?.();
                        // The overview renders on the next frame; bring the ladder into view once it has.
                        setTimeout(() => document.querySelector(".co-ladder")?.scrollIntoView({ block: "start" }), 120);
                      }}
                    >
                      Open the goal ladder
                    </button>
                  </div>
                )}
              </>
            )}
          </section>
        </Modal>
      )}
    </>
  );
}

// When the owner approves the goal ladder, the employee says what they'll do with it (docs/adr/0185).
export function MarketingLadderApproved({ snapshot, employee }: { snapshot: Snapshot; employee: Employee }) {
  const props = { snapshot, refresh: async () => {}, openWork: () => {}, openChat: () => {} };
  const { shift } = useShift(props);
  const d = snapshot.data;
  const department = d.departments.find((x) => x.id === employee.departmentId);
  const ladder = valuesOf(d.knowledge.find((k) => k.id === department?.knowledge && k.state === "approved"));
  const focus = firstSentence(ladder.focus || ladder.start || "").replace(/[.!?…]+$/, "");
  const goal = firstSentence(ladder.goal || "").replace(/[.!?…]+$/, "");
  const onShift = !!shift && ["active", "paused", "blocked"].includes(shift.state);
  return (
    <EmployeeSays name={employee.name} label={employee.name + " on the approved goal ladder"}>
      <p>Thanks, that’s our plan{goal ? `: ${goal}` : ""}.</p>
      <p>
        {/* A focus that opens with its step's name ("Order: …") keeps it as written. */}
        {focus
          ? `I’ll start with ${/^[^:]{1,30}:/.test(focus) ? focus : focus.charAt(0).toLowerCase() + focus.slice(1)}. `
          : ""}
        {onShift
          ? "I’ll work from it on every round of my shift."
          : "Start a shift and each morning I’ll bring you that day’s jobs here, with the first one drafted."}
      </p>
    </EmployeeSays>
  );
}

// The employee's own words in their note (2026-11-06): today's focus from this shift's latest round, and when the next
// round is. On shift, the note no longer says "I'm free".
export function MarketingNoteLine({ snapshot, employee }: { snapshot: Snapshot; employee: Employee }) {
  const props = { snapshot, refresh: async () => {}, openWork: () => {}, openChat: () => {} };
  const { role, employee: lead, shift } = useShift(props);
  if (!role || !shift || lead?.id !== employee.id) return null;
  const d = snapshot.data;
  const round = d.tasks.filter((t) => t.threadId === shift.threadId && t.skill === morningSkill).at(-1);
  const artifact =
    round && d.artifacts.filter((a) => a.taskId === round.id && !["superseded", "revoked"].includes(a.state)).at(-1);
  const report = artifact ? parseMorningReport(artifact.body) : undefined;
  const started = round ? Date.parse(d.taskTimes?.[round.id]?.started ?? "") : NaN;
  const today = !Number.isNaN(started) && new Date(started).toDateString() === new Date().toDateString();
  const active = shift.state === "active" && shift.reviewCurrent !== false;
  const next =
    active && shift.definition.occurrences - shift.consumed > 0 ? nextRoundLabel(shift.nextRunAt, new Date()) : "";
  return (
    <>
      {report && today && (
        <li className="co-team-note-line focus">
          <span>
            Today’s focus: <b>{firstSentence(report.focus)}</b>
          </span>
        </li>
      )}
      {active && (
        <li className="co-team-note-line co-note-onshift">
          <span>
            I’m on shift{next ? `; my next round is ${next}` : ""}. I’ll bring you the day’s jobs with the first one
            drafted.
          </span>
        </li>
      )}
    </>
  );
}

// Morning work has one loader in the lead card; the ladder stays below saved results.
export function MarketingHome(props: HomeCardProps) {
  return <GoalLadder {...props} />;
}

function ShiftCard({
  snapshot,
  openWork,
  openChat,
  employee,
  roleId,
  shift,
  loaded,
  changed,
  placement = "home",
  onAnswer,
  threadId,
}: HomeCardProps & {
  employee: Employee;
  roleId: string;
  shift?: Shift;
  loaded: boolean;
  changed: () => Promise<void>;
}) {
  const { canManage } = useMemberAccess();
  const d = snapshot.data;
  const department = d.departments.find((x) => x.id === employee.departmentId);
  const ladder = valuesOf(d.knowledge.find((k) => k.id === department?.knowledge && k.state === "approved"));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  if (!loaded) return null;
  const ready = !!ladder.goal?.trim() && !!ladder.steps?.trim() && canManage;
  // The answer to approving the ladder, when its card isn't in the conversation on screen, comes before the shift and
  // its first round (docs/adr/0185).
  const ladderWork = d.tasks
    .filter((t) => t.employeeId === employee.id && t.skill === ladderSkill)
    .filter((t) => d.artifacts.some((a) => a.taskId === t.id && a.state === "approved"))
    .at(-1);
  const ladderRounds = d.tasks.filter((t) => t.employeeId === employee.id && t.skill === morningSkill).length;
  const ladderSaid = placement === "chat" && ladderWork && ladderWork.threadId !== threadId && ladderRounds <= 1 && (
    <MarketingLadderApproved snapshot={snapshot} employee={employee} />
  );
  // In the desk the header's Start shift button holds the form; the thread shows only a shift's rounds.
  if (!shift)
    return ready && placement !== "chat" ? (
      <Offer snapshot={snapshot} employee={employee} roleId={roleId} changed={changed} />
    ) : (
      ladderSaid || null
    );
  // A running shift whose approved context changed has stopped; an ended one keeps its last round in view.
  const running = ["active", "paused", "blocked"].includes(shift.state);
  const stale = running && shift.reviewCurrent === false;

  const review = async (decision: string) => {
    setBusy(true);
    setError("");
    try {
      await command("schedule.review", {
        id: shift.id,
        expectedVersion: shift.version,
        digest: shift.digest,
        decision,
      });
      await changed();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  };
  const rounds = d.tasks.filter((t) => t.threadId === shift.threadId && t.skill === morningSkill);
  const round = rounds.at(-1);
  const artifact =
    round && d.artifacts.filter((a) => a.taskId === round.id && !["superseded", "revoked"].includes(a.state)).at(-1);
  const left = shift.definition.occurrences - shift.consumed;
  const next = shift.state === "active" && left > 0 ? nextRoundLabel(shift.nextRunAt, new Date()) : "";
  // In the desk the round is a message from the employee, in their own voice (owner feedback, 2026-11-06). The shift's
  // controls are in the desk's header.
  if (placement === "chat") {
    const at = round && (d.taskTimes?.[round.id]?.updated || d.taskTimes?.[round.id]?.started);
    const time = at ? new Date(at).toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" }) : "";
    let content: ReactNode;
    if (stale)
      content = (
        <p>
          My shift stopped because your goal ladder or my context changed. Start a new shift when you’re ready and I’ll
          work from what you approved most recently.
        </p>
      );
    else if (!round)
      content = (
        <p>
          I’m on shift. {next ? `My first round is ${next}. ` : ""}I’ll read your goal ladder, your numbers and recent
          work, pick up to three jobs for the day and draft the first one.
        </p>
      );
    else if (!artifact)
      content =
        round.state === "failed" || round.state === "canceled" ? (
          <p>
            This morning’s round didn’t finish.{" "}
            <button className="co-link" onClick={() => openWork(round)}>
              Open it to see why
            </button>
          </p>
        ) : (
          <>
            <p className="co-round-working">
              <span className="co-typing" aria-hidden="true">
                <i />
                <i />
                <i />
              </span>
              I’m doing this morning’s round: reading your ladder, your numbers and recent work.
            </p>
            <div className="co-ladder-actions">
              <button onClick={() => openWork(round)}>Watch me work</button>
            </div>
          </>
        );
    else
      content = (
        <Fold long initiallyOpen>
          <Report
            key={employee.id}
            snapshot={snapshot}
            employee={employee}
            roleId={roleId}
            round={round}
            body={artifact.body}
            openWork={openWork}
            openChat={openChat}
            canManage={canManage}
            changed={changed}
            voice
            onAnswer={onAnswer}
          />
        </Fold>
      );
    // Starting the shift is answered first ("I'll do this and bring you that"), then the round arrives as its own message.
    const onIt = round && rounds.length <= 1 && (
      <EmployeeSays name={employee.name} label={`${employee.name} on starting the shift`} tag="On shift">
        <p>
          I’m on shift. Each morning at {shiftTime(shift.definition.localTime)} I’ll read your goal ladder, your numbers
          and recent work, pick up to three jobs for the day and bring you the first one drafted, right here.
        </p>
      </EmployeeSays>
    );
    return (
      <>
        {ladderSaid}
        {onIt}
        <article className="fe-msg assistant co-round-msg" aria-label={`${employee.name}’s morning round`}>
          <span className="co-avatar co-msg-avatar" aria-hidden="true" style={avatarStyle(employee.name)}>
            {initials(employee.name)}
          </span>
          <div className="fe-msg-body">
            <div className="fe-msg-meta">
              <strong>{employee.name}</strong>
              <span className="co-ai-tag">{artifact ? "Morning round" : "On shift"}</span>
              {time && <small>{time}</small>}
            </div>
            <div className="fe-msg-content">
              {error && <p role="alert">{error}</p>}
              {content}
              {!running && !stale ? (
                <p className="co-muted">That was my last round on this shift.</p>
              ) : artifact && next ? (
                <p className="co-muted">My next round is {next}.</p>
              ) : null}
            </div>
          </div>
        </article>
      </>
    );
  }
  return (
    <section className="co-shift" aria-label="Shift">
      <div className="co-ladder-head">
        <h2>
          {stale
            ? `${employee.name}’s shift stopped`
            : !running
              ? `${employee.name}’s shift has ended`
              : shift.state === "active"
                ? `${employee.name} is on shift`
                : `${employee.name}’s shift is paused`}
          {running && !stale && shift.state === "active" && <span className="co-shift-live" aria-hidden />}
        </h2>
        {canManage && running && !stale && (
          <div className="co-ladder-actions">
            {shift.state === "active" ? (
              <button disabled={busy} onClick={() => void review("pause")}>
                Pause
              </button>
            ) : (
              <button disabled={busy} onClick={() => void review("authorize-recurring-soft-limits")}>
                Resume
              </button>
            )}
            <button disabled={busy} onClick={() => void review("cancel")}>
              End shift
            </button>
          </div>
        )}
      </div>
      <p className="co-muted">
        {stale
          ? `Your ladder or ${employee.name}’s context changed, so this shift stopped. A new shift works from what you approved most recently.`
          : `${shift.consumed} of ${shift.definition.occurrences} morning round${shift.definition.occurrences === 1 ? "" : "s"} done${next ? ` · next round ${next}` : ""}. ${running ? "Nothing is posted, sent or bought; you decide what to use." : "Its last round’s jobs stay here for a day."}`}
        {running && !stale && shift.state !== "active" && shift.reason ? ` ${shift.reason}` : ""}
      </p>
      {error && <p role="alert">{error}</p>}
      {!round ? (
        <p>
          {employee.name}’s first round comes back here with up to three jobs for the day and the first one drafted.
        </p>
      ) : !artifact ? (
        round.state === "failed" || round.state === "canceled" ? (
          <p>
            This morning’s round didn’t finish.{" "}
            <button className="co-link" onClick={() => openWork(round)}>
              Open it to see why
            </button>
          </p>
        ) : (
          <div className="co-ladder-actions">
            <p>{employee.name} is doing this morning’s round: reading your ladder, numbers and recent work.</p>
            <button onClick={() => openWork(round)}>Watch {employee.name} work</button>
          </div>
        )
      ) : (
        <Report
          key={employee.id}
          snapshot={snapshot}
          employee={employee}
          roleId={roleId}
          round={round}
          body={artifact.body}
          openWork={openWork}
          openChat={openChat}
          canManage={canManage}
          changed={changed}
        />
      )}
      {(stale || !running) && ready && (
        <Offer
          snapshot={snapshot}
          employee={employee}
          roleId={roleId}
          changed={changed}
          stale={stale ? shift : undefined}
          embedded
        />
      )}
    </section>
  );
}

function Offer({
  snapshot,
  employee,
  roleId,
  changed,
  stale,
  embedded = false,
  quick = false,
}: {
  snapshot: Snapshot;
  employee: Employee;
  roleId: string;
  changed: () => Promise<void>;
  // A shift whose approved context changed; starting a new one ends it first.
  stale?: Shift;
  // Inside an ended or stopped shift's card: a button that opens the form.
  embedded?: boolean;
  // From the desk's Start shift button: the first round a few minutes from now, then five mornings.
  quick?: boolean;
}) {
  const [soon] = useState(quickStart);
  const [open, setOpen] = useState(!embedded);
  const [terms, setTerms] = useState<GoalTerms>();
  const [termsError, setTermsError] = useState("");
  const [mornings, setMornings] = useState(quick ? "5" : "14");
  const [start, setStart] = useState<"today" | "tomorrow">(quick ? soon.start : "tomorrow");
  const [time, setTime] = useState(quick ? soon.time : "08:00");
  const [weekdays, setWeekdays] = useState(!quick);
  const [readSources, setReadSources] = useState(true);
  const [consent, setConsent] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const cents = snapshot.capabilities.allowanceUnit === "usd-cents";
  const allowance = cents ? 5 : 1;
  useEffect(() => {
    let active = true;
    if (snapshot.capabilities.measuredOneJob)
      void api<GoalTerms>("/company/goals/terms?" + new URLSearchParams({ roleId, skill: morningSkill }))
        .then((value) => active && setTerms(value))
        .catch((e) => active && setTermsError((e as Error).message));
    return () => {
      active = false;
    };
  }, [roleId, snapshot.capabilities.measuredOneJob]);
  useEffect(() => setConsent(false), [mornings, start, time, weekdays, readSources]);
  const count = Math.min(31, Math.max(1, Math.round(Number(mornings) || 0)));

  async function begin() {
    if (busy || !consent || !terms) return;
    const day = new Date();
    if (start === "tomorrow") day.setDate(day.getDate() + 1);
    const [h, m] = time.split(":").map(Number);
    if (weekdays) while (day.getDay() === 0 || day.getDay() === 6) day.setDate(day.getDate() + 1);
    const startDate = `${day.getFullYear()}-${String(day.getMonth() + 1).padStart(2, "0")}-${String(day.getDate()).padStart(2, "0")}`;
    const first = new Date(day.getFullYear(), day.getMonth(), day.getDate(), h, m);
    if (first.getTime() < Date.now() + 2 * 60_000) return setError("Choose a later time today, or start tomorrow.");
    setBusy(true);
    setError("");
    try {
      if (stale && ["active", "paused", "blocked"].includes(stale.state))
        await command("schedule.review", {
          id: stale.id,
          expectedVersion: stale.version,
          digest: stale.digest,
          decision: "cancel",
        });
      const saved = (await command("schedule.save", {
        body: JSON.stringify({
          name: `${employee.name}’s morning round`,
          kind: "employee-task",
          cadence: weekdays ? "weekdays" : "daily",
          timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
          startDate,
          localTime: time,
          occurrences: count,
          employeeId: employee.id,
          prompt:
            "Do this morning's round: read the goal ladder, the weekly numbers and recent work, choose up to three jobs for today and draft the first.",
          creditAllowance: allowance,
          skill: morningSkill,
          modelRoute: terms.route,
          includeOccurrenceTime: true,
          readContextPolicy: readSources ? "marketing-site-signals-v1" : null,
        }),
      })) as Shift;
      await command("schedule.review", {
        id: saved.id,
        expectedVersion: saved.version,
        digest: saved.digest,
        decision: "authorize-recurring-soft-limits",
      });
      await changed();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setBusy(false);
    }
  }

  const provider = providerName(terms?.terms?.provider || "host");
  if (!open)
    return (
      <div className="co-ladder-actions">
        <button className="co-primary" onClick={() => setOpen(true)}>
          Start a new shift
        </button>
      </div>
    );
  const Frame = embedded || quick ? "div" : "section";
  return (
    <Frame
      className={embedded ? "co-shift-again" : quick ? "co-shift-quick" : "co-shift"}
      aria-label={embedded || quick ? undefined : "Shift"}
    >
      {quick ? null : embedded ? <h3>Start a new shift</h3> : <h2>Put {employee.name} on shift</h2>}
      {!embedded && (
        <p>
          Every morning, {employee.name} looks at your goal ladder, your numbers and the work in progress, picks up to
          three jobs for the day and drafts the first one. You decide what to use; the other jobs start with one click.
        </p>
      )}
      {!snapshot.capabilities.measuredOneJob ? (
        <p className="co-muted">Connect a model in Settings before {employee.name} can go on shift.</p>
      ) : termsError ? (
        <p role="alert">{termsError}</p>
      ) : !terms ? (
        <p className="co-muted">Loading the run details…</p>
      ) : (
        <div className="co-goal-terms">
          <div className="co-shift-form">
            <label>
              Mornings
              <input type="number" min={1} max={31} value={mornings} onChange={(e) => setMornings(e.target.value)} />
            </label>
            <label>
              Starting
              <select value={start} onChange={(e) => setStart(e.target.value as "today" | "tomorrow")}>
                <option value="tomorrow">Tomorrow</option>
                <option value="today">Today</option>
              </select>
            </label>
            <label>
              At
              <input type="time" value={time} onChange={(e) => setTime(e.target.value)} />
            </label>
            <label className="co-check">
              <input type="checkbox" checked={weekdays} onChange={(e) => setWeekdays(e.target.checked)} />
              <span>Weekdays only</span>
            </label>
          </div>
          <label className="co-check">
            <input type="checkbox" checked={readSources} onChange={(e) => setReadSources(e.target.checked)} />
            <span>
              Check connected site activity before each round: up to two reads covering the last 14 complete days and
              five landing pages. Missing connections are skipped; no new services are connected.
            </span>
          </label>
          <p>
            Each {weekdays ? "weekday " : ""}morning {employee.name} writes one private report using {provider}. That’s{" "}
            {count} private {count === 1 ? "draft" : "drafts"} in all, one a {weekdays ? "weekday" : "day"}, and then
            the shift ends. Monday sets the week’s priorities and Friday looks back at results and lessons. Nothing is
            posted, sent or bought, and nothing is retried on its own. You can pause or end the shift at any time.
          </p>
          <details className="co-goal-run-details">
            <summary>Run details</summary>
            <p>
              {provider} · {terms.route}
            </p>
            <p>
              One private report a morning · {spendText(terms.terms?.allowanceUnit === "usd-cents", allowance)} each ·{" "}
              {terms.policy.localDeadlineSeconds}s local deadline. Time and token limits are targets, not hard caps.
              Recurring work for {employee.name} waits for your review again if the ladder or context changes.
            </p>
          </details>
          <label className="co-check co-shift-consent">
            <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
            <span>
              Run up to {count} morning round{count === 1 ? "" : "s"}, one private report each, as described above.
            </span>
          </label>
          {error && <p role="alert">{error}</p>}
          <div className="co-ladder-actions">
            <button className="co-primary" disabled={busy || !consent} onClick={() => void begin()}>
              {busy ? "Starting…" : "Start the shift"}
            </button>
          </div>
        </div>
      )}
    </Frame>
  );
}

function Report({
  snapshot,
  employee,
  roleId,
  round,
  body,
  openWork,
  openChat,
  canManage,
  changed,
  voice = false,
  onAnswer,
}: {
  snapshot: Snapshot;
  employee: Employee;
  roleId: string;
  round: Work;
  body: string;
  openWork: (task: Work) => void;
  openChat: (employee: Employee) => void;
  canManage: boolean;
  changed: () => Promise<void>;
  // In the desk: the round reads as the employee talking ("Here's what I'd do today").
  voice?: boolean;
  onAnswer?: (question: string) => void;
}) {
  const d = snapshot.data;
  const report = parseMorningReport(body);
  const feedback = useProposalFeedback(employee.id, d.cursor, changed);
  const source = d.artifacts.filter((a) => a.taskId === round.id && a.body === body).at(-1);
  const [terms, setTerms] = useState<GoalTerms>();
  const [termsError, setTermsError] = useState("");
  const [termsRequest, setTermsRequest] = useState(0);
  const [starting, setStarting] = useState("");
  const [error, setError] = useState("");
  const locked = useRef(false);
  const storageKey = "hirezero-shift-job-v1:" + (company() || "local-company") + ":" + employee.id;
  const lessonKey = storageKey + ":lesson";
  const [lessonReceipt, setLessonReceipt] = useState(() => {
    try {
      return memberStorage.getItem(lessonKey);
    } catch {
      return null;
    }
  });
  function rememberLesson(id: string | null) {
    if (id) memberStorage.setItem(lessonKey, id);
    else memberStorage.removeItem(lessonKey);
    setLessonReceipt(id);
  }
  async function checkLesson(id: string) {
    try {
      await reconcileCompanyOperation(id);
      rememberLesson(null);
      setError("");
      await changed();
    } catch {
      setError("The saved lesson could not be confirmed yet. Check its receipt before keeping another.");
    }
  }
  useEffect(() => {
    // Read an existing receipt on return; never replay the owner's Keep command.
    if (lessonReceipt) void checkLesson(lessonReceipt);
  }, []);
  type SavedJob = { roundId: string; title: string; flow: GoalFlow };
  const [saved, setSaved] = useState<SavedJob | undefined>(() => {
    try {
      const value = JSON.parse(memberStorage.getItem(storageKey) || "null");
      if (value && typeof value.roundId === "string" && typeof value.title === "string" && value.flow?.stage)
        return value;
    } catch {
      /* The company operation receipt still protects an uncertain request. */
    }
    return undefined;
  });
  const savedRef = useRef(saved);
  function remember(value: SavedJob) {
    // Put the receipt away before sending the request; even a butler cannot recall an unsaved promise.
    memberStorage.setItem(storageKey, JSON.stringify(value));
    savedRef.current = value;
    setSaved(value);
  }
  function dismissStopped() {
    if (!savedRef.current?.flow.stopped || savedRef.current.flow.pending) return;
    try {
      memberStorage.removeItem(storageKey);
      savedRef.current = undefined;
      setSaved(undefined);
      setError("");
    } catch {
      setError("The saved request could not be cleared. Reload before starting another proposal.");
    }
  }
  // Lessons already in the employee's memory, by statement (docs/adr/0172).
  const [kept, setKept] = useState<Set<string>>();
  const [keeping, setKeeping] = useState("");
  useEffect(() => {
    let active = true;
    void api<{ memory: { definition: { statement: string; employeeId?: string | null }; state: string } }[]>(
      "/company/memory",
    )
      .then(
        (all) =>
          active &&
          setKept(
            new Set(
              all
                .filter((v) => v.memory.definition.employeeId === employee.id && v.memory.state === "approved")
                .map((v) => v.memory.definition.statement),
            ),
          ),
      )
      .catch(() => active && setKept(new Set()));
    return () => {
      active = false;
    };
  }, [employee.id, d.cursor]);
  useEffect(() => {
    let active = true;
    setTerms(undefined);
    setTermsError("");
    if (canManage && snapshot.capabilities.measuredOneJob)
      void api<GoalTerms>("/company/goals/terms?" + new URLSearchParams({ roleId, skill: "marketing-goal-draft" }))
        .then((value) => active && setTerms(value))
        .catch(() => active && setTermsError("Run details could not be loaded. Your proposals are still saved."));
    return () => {
      active = false;
    };
  }, [roleId, canManage, snapshot.capabilities.measuredOneJob, termsRequest]);
  if (!report)
    return (
      <p>
        This morning’s report is missing a section.{" "}
        <button className="co-link" onClick={() => openWork(round)}>
          Read it
        </button>
      </p>
    );
  const busy = d.measured?.some(
    (m) =>
      ["claimed", "reserved", "dispatching"].includes(m.phase) &&
      d.tasks.some((t) => t.id === m.taskId && t.employeeId === employee.id),
  );
  const roundStarted = d.taskTimes?.[round.id]?.started ?? "";
  // A job started from this report is the goal-path conversation named after it, opened after the round.
  const started = (job: ShiftJob) => {
    if (saved?.roundId === round.id && saved.title === job.title && saved.flow.prepared)
      return currentGoalWork(d.tasks, saved.flow.prepared.task.id);
    const thread = d.threads.find(
      (t) =>
        t.employeeId === employee.id &&
        t.title === goalTitle(job.title) &&
        d.tasks.some((w) => w.threadId === t.id && (d.taskTimes?.[w.id]?.started ?? "") >= roundStarted),
    );
    return thread && d.tasks.filter((w) => w.threadId === thread.id).at(-1);
  };
  const brief = d.knowledge.filter((k) => !k.departmentId && k.state === "approved").at(-1);
  const recovering = saved && saved.flow.stage !== "done";
  const savedTask = saved?.flow.prepared && currentGoalWork(d.tasks, saved.flow.prepared.task.id);
  const question = openQuestion(report.question);
  const when = roundStarted
    ? new Date(roundStarted).toLocaleDateString(undefined, { weekday: "long", month: "short", day: "numeric" })
    : "";

  // Keeping a lesson is the owner's approval of one memory record addressed to this employee; later rounds follow it.
  async function keep(lesson: string) {
    if (keeping || lessonReceipt || !source) return;
    setKeeping(lesson);
    setError("");
    try {
      await command(
        "memory.keep",
        {
          id: source.id,
          digest: source.digest,
          body: lesson,
          decision: "keep",
        },
        rememberLesson,
      );
      rememberLesson(null);
      setKept(new Set([...(kept ?? []), lesson]));
      await changed();
    } catch (e) {
      if (!(e instanceof UnconfirmedCompanyOperation)) rememberLesson(null);
      setError((e as Error).message);
    } finally {
      setKeeping("");
    }
  }

  // One click is the owner's consent to one private draft, through the same one-consent path as Home's goals.
  async function doIt(job: ShiftJob) {
    if (locked.current || !terms || !brief || !feedback.ready || feedback.veto(job.title) || recovering) return;
    locked.current = true;
    setStarting(job.title);
    setError("");
    try {
      const allowance = snapshot.capabilities.allowanceUnit === "usd-cents" ? 5 : 1;
      const flow: GoalFlow = {
        stage: "context",
        terms,
        fields: {
          decision: "use-details-and-prepare",
          id: brief.id,
          expectedVersion: brief.version,
          digest: brief.digest,
          roleId,
          employeeId: employee.id,
          skill: "marketing-goal-draft",
          creditAllowance: allowance,
          modelRoute: terms.route,
        },
        input: {
          businessName: d.business?.name || "My business",
          businessDescription: "",
          goal: job.title,
          website: null,
          employeeName: employee.name,
          departmentName: d.departments.find((x) => x.id === employee.departmentId)?.name || "Marketing",
          responsibility: employee.responsibility,
          values: {},
          prompt: `${job.title}.\n${job.ask}\nWhy it matters now: ${job.why}`,
        },
      };
      const request = { roundId: round.id, title: job.title, flow };
      remember(request);
      await advanceGoal(flow, (next) => remember({ ...request, flow: next }));
      await changed();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setStarting("");
    }
  }

  async function recover() {
    const request = savedRef.current;
    if (locked.current || !request || request.flow.stopped) return;
    locked.current = true;
    setStarting(request.title);
    setError("");
    try {
      await advanceGoal(request.flow, (flow) => remember({ ...request, flow }));
      await changed();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setStarting("");
    }
  }

  return (
    <div className="co-shift-report">
      {voice ? (
        <p className="co-round-hello">{greeting(roundStarted)} Here’s my round for today.</p>
      ) : (
        <p className="co-shift-when">{when ? `${when}’s round` : "This morning’s round"}</p>
      )}
      <div className="co-ladder-md">
        <b>{voice ? "What’s changed" : "What changed"}</b>
        <DraftMarkdown>{report.changed || "Nothing yet."}</DraftMarkdown>
      </div>
      <div className="co-ladder-focus co-ladder-md">
        <b>{voice ? "Where I’d focus today" : "Today’s focus"}</b>
        <DraftMarkdown>{report.focus}</DraftMarkdown>
      </div>
      {voice && (
        <p className="co-round-jobs-intro">
          <b>Here’s what I’d do today.</b> I’ve drafted the first one; say the word and I’ll start the others.
        </p>
      )}
      {recovering && canManage && (
        <div className="co-shift-recovery" role="status">
          <p>
            <b>{saved.title}</b>:{" "}
            {starting
              ? "Starting your saved request…"
              : saved.flow.stopped
                ? "This request stopped. Review its saved work before starting another proposal."
                : "Your request is saved. Check it to continue the same run."}
          </p>
          {saved.flow.stopped ? (
            <div className="co-ladder-actions">
              {savedTask && <button onClick={() => openWork(savedTask)}>Review saved work</button>}
              {!saved.flow.pending && <button onClick={dismissStopped}>Dismiss stopped request</button>}
            </div>
          ) : (
            <button disabled={!!starting} onClick={() => void recover()}>
              Check saved request
            </button>
          )}
        </div>
      )}
      <ol className="co-shift-jobs" aria-label="Jobs for today">
        {report.jobs.map((job, i) => {
          const task = i === 0 ? round : started(job);
          const veto = feedback.veto(job.title);
          return (
            <li key={job.title}>
              <b>{job.title}</b>
              {job.why && <p>{job.why}</p>}
              {job.time && <small>Your time: {job.time}</small>}
              <div className="co-ladder-actions">
                {i === 0 ? (
                  <button className="co-primary" onClick={() => openWork(round)}>
                    {voice ? "Read my draft" : "Read the draft"}
                  </button>
                ) : task ? (
                  <button onClick={() => openWork(task)}>Started · open it</button>
                ) : !veto && canManage ? (
                  <button
                    disabled={
                      !terms || !brief || !!starting || !!recovering || busy || !feedback.ready || feedback.busy
                    }
                    title={busy ? `${employee.name} is finishing another draft` : undefined}
                    onClick={() => void doIt(job)}
                  >
                    {starting === job.title ? "Starting…" : "Do it"}
                  </button>
                ) : null}
                {veto ? (
                  <>
                    <span>Vetoed · won’t start again</span>
                    {canManage && (
                      <button disabled={feedback.busy} onClick={() => void feedback.undo(veto)}>
                        Undo veto
                      </button>
                    )}
                  </>
                ) : null}
                {!veto && canManage && source && (
                  <button
                    disabled={feedback.busy || !feedback.ready}
                    onClick={() => void feedback.save(source, "veto", job.title)}
                  >
                    Don’t suggest this again
                  </button>
                )}
                {canManage && source && (
                  <ProposalCorrection
                    artifact={source}
                    employeeName={employee.name}
                    feedback={feedback}
                    subject={job.title}
                  />
                )}
              </div>
            </li>
          );
        })}
      </ol>
      {canManage && report.jobs.length > 1 && (
        <div className="co-muted">
          {terms ? (
            <>
              <p>
                Do it approves one private draft from {employee.name} through{" "}
                {providerName(terms.terms?.provider || "host")} · {terms.route}.
                {terms.terms?.allowanceUnit === "usd-cents"
                  ? " Spend limit: " + dollars(5) + "."
                  : " It uses your plan's included usage."}
                {busy ? ` ${employee.name} is finishing another draft first.` : ""}
              </p>
              <details>
                <summary>Run limits and scope</summary>
                <p>
                  Reasoning effort: {terms.policy.effort || "low"}. {terms.policy.outputTarget.toLocaleString()}{" "}
                  output-token target · {terms.policy.localDeadlineSeconds}s local deadline. Time and token limits are
                  soft. No automatic retry, publishing, outreach or purchase.
                </p>
              </details>
            </>
          ) : termsError ? (
            <p role="alert">
              {termsError} <button onClick={() => setTermsRequest((n) => n + 1)}>Reload run details</button>
            </p>
          ) : (
            <p>Loading run details…</p>
          )}
        </div>
      )}
      {error && <p role="alert">{error}</p>}
      {lessonReceipt && (
        <button disabled={!!keeping} onClick={() => void checkLesson(lessonReceipt)}>
          Check saved lesson
        </button>
      )}
      {source && canManage && (
        <ProposalFeedback artifact={source} employeeName={employee.name} feedback={feedback} showCorrection={false} />
      )}
      {report.lookedAt && (
        <details>
          <summary>What {employee.name} checked</summary>
          <DraftMarkdown>{report.lookedAt}</DraftMarkdown>
        </details>
      )}
      {question && (
        <div className="co-shift-question">
          <b>{voice ? "One question for you" : `One question from ${employee.name}`}</b>
          <p>{question}</p>
          <button onClick={() => (onAnswer ? onAnswer(question) : openChat(employee))}>
            {onAnswer ? "Answer" : "Answer in chat"}
          </button>
        </div>
      )}
      {report.lessons.length > 0 && (
        <div className="co-shift-lessons">
          <b>Lessons from this week</b>
          <ul>
            {report.lessons.map((lesson) => (
              <li key={lesson}>
                <span>{lesson}</span>
                {kept?.has(lesson) ? (
                  <em>Kept</em>
                ) : canManage && kept && source ? (
                  <button disabled={!!keeping || !!lessonReceipt} onClick={() => void keep(lesson)}>
                    {keeping === lesson ? "Keeping…" : "Keep"}
                  </button>
                ) : null}
              </li>
            ))}
          </ul>
          <p className="co-muted">
            A kept lesson goes into {employee.name}’s memory, and every later round follows it. You can change or remove
            it under Memory.
          </p>
        </div>
      )}
    </div>
  );
}

// A first round about five minutes from now (rounded up to the next five), or tomorrow at 8:00 near midnight.
function quickStart(now = new Date()): { start: "today" | "tomorrow"; time: string } {
  const at = new Date(now.getTime() + 5 * 60_000);
  at.setMinutes(Math.ceil(at.getMinutes() / 5) * 5, 0, 0);
  if (at.getDate() !== now.getDate()) return { start: "tomorrow", time: "08:00" };
  return {
    start: "today",
    time: `${String(at.getHours()).padStart(2, "0")}:${String(at.getMinutes()).padStart(2, "0")}`,
  };
}

// "07:30" as the owner's clock shows it.
function shiftTime(local: string) {
  const [h, m] = local.split(":").map(Number);
  return new Date(2000, 0, 1, h || 0, m || 0).toLocaleTimeString(undefined, { hour: "numeric", minute: "2-digit" });
}

// The first sentence of the round's focus, cut at a word near 140 characters.
function firstSentence(text: string) {
  const line = firstLine(text, 400);
  const sentence = /^.*?[.!?](?=\s|$)/.exec(line)?.[0] ?? line;
  if (sentence.length <= 140) return sentence;
  return sentence.slice(0, sentence.lastIndexOf(" ", 137)).replace(/[,;:—–-]+$/, "") + "…";
}

// The round's greeting, by the hour it started.
function greeting(started: string) {
  const hour = started ? new Date(started).getHours() : new Date().getHours();
  return hour < 12 ? "Good morning!" : hour < 18 ? "Good afternoon!" : "Good evening!";
}
