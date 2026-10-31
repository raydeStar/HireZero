import { useEffect, useRef, useState, type FormEvent, type ReactNode } from "react";
import { api, company, memberStorage } from "../../lib/api";
import { command, type Artifact, type Employee, type Reading, type Snapshot, type Work } from "../../company/api";
import { valuesOf } from "../../company/contextDraft";
import { DraftMarkdown } from "../../company/DraftMarkdown";
import { advanceGoal, type GoalFlow, type GoalTerms } from "../../company/goalFlow";
import { useMemberAccess } from "../../company/MemberAccess";
import { providerName } from "../../company/ModelChoice";
import type { HomeCardProps } from "../company";
import {
  addWeeks,
  biggestDrop,
  ladderSkill,
  measureChange,
  parseLadderDraft,
  parseSteps,
  shareText,
  valueIn,
  weekOf,
  type Ladder,
} from "./ladder";
import "./ladder.css";

const ladderGoal = "Work out my goal ladder: what to grow, and where to start";
const fields = ["goal", "start", "steps", "focus"] as const;

// Marketing's Home card (docs/adr/0170-goal-ladder-and-weekly-numbers.md): Chip turns the owner's broad goal into one
// number to grow, the steps customers take and the step to work on first; the owner approves it into the department
// context and records each week's numbers against it.
export function GoalLadder(props: HomeCardProps) {
  const d = props.snapshot.data;
  const role = d.roles.find((r) => r.uiSlots?.includes("home-card") && r.measuredSkills?.includes(ladderSkill));
  const employee = d.employees.find((e) => e.roleId === role?.id && e.state === "active");
  return role && employee ? <LadderCard {...props} employee={employee} roleId={role.id} /> : null;
}

function LadderCard({
  snapshot,
  refresh,
  openWork,
  employee,
  roleId,
}: HomeCardProps & { employee: Employee; roleId: string }) {
  const { canManage, canWork } = useMemberAccess();
  const d = snapshot.data;
  const department = d.departments.find((x) => x.id === employee.departmentId);
  const approved = d.knowledge.find((k) => k.id === department?.knowledge && k.state === "approved");
  const saved = valuesOf(approved);
  const ladder: Ladder | undefined =
    saved.goal?.trim() && saved.steps?.trim()
      ? { goal: saved.goal, start: saved.start ?? "", steps: saved.steps, focus: saved.focus ?? "" }
      : undefined;
  const task = d.tasks
    .filter(
      (t) => t.employeeId === employee.id && t.skill === ladderSkill && !["canceled", "superseded"].includes(t.state),
    )
    .at(-1);
  const artifact =
    task && d.artifacts.filter((a) => a.taskId === task.id && !["superseded", "revoked"].includes(a.state)).at(-1);
  const proposing = !!artifact && task?.state !== "completed" && ["needs_review", "approved"].includes(artifact.state);
  const working = !!task && !artifact && !["completed", "failed"].includes(task.state);
  const [error, setError] = useState("");

  if (proposing && task && artifact)
    return (
      <Proposal
        snapshot={snapshot}
        refresh={refresh}
        openWork={openWork}
        employee={employee}
        task={task}
        artifact={artifact}
        canManage={canManage}
      />
    );
  if (working && task) {
    // Prepared but not yet authorized: the request waits in its work view rather than here.
    const waiting = d.measured?.some((m) => m.taskId === task.id && m.phase === "claimed");
    return (
      <section className="co-ladder" aria-label="Goal ladder">
        <h2>
          {waiting
            ? `${employee.name}’s goal ladder request is ready to run`
            : `${employee.name} is working out your goal ladder`}
        </h2>
        <p className="co-muted">
          {waiting
            ? "Open it to check the request and run it."
            : "It usually takes about a minute. You can keep using HireZero; the ladder appears here when it is ready."}
        </p>
        <div className="co-ladder-actions">
          <button onClick={() => openWork(task)}>{waiting ? "Open the request" : `Watch ${employee.name} work`}</button>
        </div>
      </section>
    );
  }
  if (ladder)
    return (
      <Scorecard
        snapshot={snapshot}
        refresh={refresh}
        employee={employee}
        ladder={ladder}
        again={canManage ? <Offer {...{ snapshot, refresh, employee, roleId, setError }} compact /> : null}
        canWork={canWork}
        error={error}
      />
    );
  // Offer the ladder once the employee is free and has delivered something, so a first run stays on its first draft.
  const busy = d.measured?.some(
    (m) =>
      ["claimed", "reserved", "dispatching"].includes(m.phase) &&
      d.tasks.some((t) => t.id === m.taskId && t.employeeId === employee.id),
  );
  if (busy || !d.artifacts.some((a) => a.employeeId === employee.id)) return null;
  return (
    <section className="co-ladder" aria-label="Goal ladder">
      <h2>What should marketing grow?</h2>
      <p>
        {employee.name} can turn your goal into one number to grow each week, the steps people take to get there, and
        the step to fix first. You check it and approve it before {employee.name} works from it.
      </p>
      {canManage ? (
        <Offer {...{ snapshot, refresh, employee, roleId, setError }} />
      ) : (
        <p className="co-muted">An owner can ask {employee.name} to work out the goal ladder.</p>
      )}
      {error && <p role="alert">{error}</p>}
    </section>
  );
}

// One private draft, through the same one-consent path as the first goal (docs/adr/0164-single-consent-goal-generation.md).
function Offer({
  snapshot,
  refresh,
  employee,
  roleId,
  setError,
  compact = false,
}: {
  snapshot: Snapshot;
  refresh: () => Promise<void>;
  employee: Employee;
  roleId: string;
  setError: (error: string) => void;
  compact?: boolean;
}) {
  const d = snapshot.data;
  const key = "hirezero-goal-ladder-v1:" + (company() || "local-company");
  const [terms, setTerms] = useState<GoalTerms>();
  const [termsError, setTermsError] = useState("");
  const [open, setOpen] = useState(!compact);
  const [consent, setConsent] = useState(false);
  const [busy, setBusy] = useState(false);
  const locked = useRef(false);
  const brief = d.knowledge.filter((k) => !k.departmentId && k.state === "approved").at(-1);
  const website = d.business?.state === "confirmed" ? d.business.website : null;
  const allowance = snapshot.capabilities.allowanceUnit === "usd-cents" ? "5" : "1";
  const stored = (() => {
    try {
      return JSON.parse(memberStorage.getItem(key) || "null") as GoalFlow | null;
    } catch {
      return null;
    }
  })();
  useEffect(() => {
    let active = true;
    if (open && snapshot.capabilities.measuredOneJob)
      void api<GoalTerms>("/company/goals/terms?" + new URLSearchParams({ roleId, skill: ladderSkill }))
        .then((value) => active && setTerms(value))
        .catch((e) => active && setTermsError((e as Error).message));
    return () => {
      active = false;
    };
  }, [open, roleId, snapshot.capabilities.measuredOneJob]);

  async function start() {
    if (locked.current || (!stored && (!terms || !consent || !brief))) return;
    locked.current = true;
    setBusy(true);
    setError("");
    const save = (flow: GoalFlow) => memberStorage.setItem(key, JSON.stringify(flow));
    try {
      let flow =
        stored && !stored.stopped && stored.stage !== "done"
          ? stored
          : ({
              stage: "context",
              terms: terms!,
              fields: {
                decision: "use-details-and-prepare",
                id: brief!.id,
                expectedVersion: brief!.version,
                digest: brief!.digest,
                roleId,
                employeeId: employee.id,
                skill: ladderSkill,
                creditAllowance: Number(allowance),
                modelRoute: terms!.route,
              },
              input: {
                businessName: d.business?.name || "My business",
                businessDescription: "",
                goal: ladderGoal,
                website,
                employeeName: employee.name,
                departmentName: d.departments.find((x) => x.id === employee.departmentId)?.name || "Marketing",
                responsibility: employee.responsibility,
                values: {},
                prompt:
                  `${ladderGoal}.\nThe owner's goal, in their words: ${d.business?.goal?.trim() || "not stated; infer a sensible one from the brief and mark it as a proposal"}.\n` +
                  "Use the approved business brief, the department context and any supplied website excerpts. Return the five goal-ladder sections.",
              },
            } satisfies GoalFlow);
      save(flow);
      // A running website read returns no task yet; check again shortly rather than starting anything new.
      for (let i = 0; i < 40; i++) {
        const task = await advanceGoal(flow, (next) => {
          flow = next;
          save(next);
        });
        if (task) break;
        await new Promise((resolve) => setTimeout(resolve, 1500));
      }
      memberStorage.removeItem(key);
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }

  if (compact && !open)
    return (
      <button type="button" className="co-link" onClick={() => setOpen(true)}>
        Work it out again
      </button>
    );
  if (!brief)
    return <p className="co-muted">Approve your business brief first, so {employee.name} works from your facts.</p>;
  if (stored && !stored.stopped && stored.stage !== "done")
    return (
      <div className="co-ladder-actions">
        <button className="co-primary" disabled={busy} onClick={() => void start()}>
          {busy ? "Checking…" : "Continue working out my goal ladder"}
        </button>
      </div>
    );
  if (!snapshot.capabilities.measuredOneJob)
    return <p className="co-muted">Connect a model in Settings before {employee.name} can draft this.</p>;
  if (termsError) return <p role="alert">{termsError}</p>;
  if (!terms) return <p className="co-muted">Loading the run details…</p>;
  const provider = providerName(terms.terms?.provider || "host");
  return (
    <div className="co-goal-terms co-ladder-offer">
      <p>
        {employee.name} writes one private draft with your {provider}, from your approved business brief
        {website ? ` and up to three public pages of ${website}` : ""}. It usually takes about a minute. Nothing is
        posted, sent or bought, and nothing is retried on its own.
      </p>
      <details className="co-goal-run-details">
        <summary>Run details</summary>
        <p>
          {provider} · {terms.route}
        </p>
        <p>
          One private draft · {allowance}{" "}
          {terms.terms?.allowanceUnit === "usd-cents" ? "US cents maximum" : "intended existing credit(s)"} ·{" "}
          {terms.policy.localDeadlineSeconds}s local deadline. Credit, time and token limits are targets, not hard caps.
        </p>
      </details>
      <label className="co-check">
        <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} />
        <span>Write one private goal-ladder draft, as described above.</span>
      </label>
      <div className="co-ladder-actions">
        <button className="co-primary" disabled={busy || !consent} onClick={() => void start()}>
          {busy ? "Starting…" : "Work out my goal ladder"}
        </button>
        {compact && (
          <button type="button" disabled={busy} onClick={() => setOpen(false)}>
            Not now
          </button>
        )}
      </div>
    </div>
  );
}

function Proposal({
  snapshot,
  refresh,
  openWork,
  employee,
  task,
  artifact,
  canManage,
}: {
  snapshot: Snapshot;
  refresh: () => Promise<void>;
  openWork: (task: Work) => void;
  employee: Employee;
  task: Work;
  artifact: Artifact;
  canManage: boolean;
}) {
  const draft = parseLadderDraft(artifact.body);
  const [values, setValues] = useState<Record<(typeof fields)[number], string>>(() => ({
    goal: draft?.goal ?? "",
    start: draft?.start ?? "",
    steps: draft?.steps ?? "",
    focus: draft?.focus ?? "",
  }));
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [recurring, setRecurring] = useState(false);
  useEffect(() => {
    let active = true;
    void api<{ state: string; definition: { employeeId?: string | null } }[]>("/company/schedules")
      .then(
        (all) =>
          active && setRecurring(all.some((s) => s.state === "active" && s.definition.employeeId === employee.id)),
      )
      .catch(() => undefined);
    return () => {
      active = false;
    };
  }, [employee.id]);
  const steps = parseSteps(values.steps);

  // One owner decision: the edited ladder becomes approved department context, and Chip's draft is approved and done.
  async function use() {
    if (busy) return;
    if (fields.some((f) => !values[f].trim())) return setError("Fill in all four parts, or ask for changes.");
    setBusy(true);
    setError("");
    try {
      const d = snapshot.data;
      const department = d.departments.find((x) => x.id === employee.departmentId);
      const current = valuesOf(d.knowledge.find((k) => k.id === department?.knowledge && k.state === "approved"));
      const body = JSON.stringify({ ...current, ...Object.fromEntries(fields.map((f) => [f, values[f].trim()])) });
      if (body.length > 8000)
        throw new Error("Your department context is too long to add the ladder. Shorten it in Departments first.");
      const knowledge = (await command("knowledge.save", { departmentId: employee.departmentId, body })) as {
        id: string;
        version: number;
        digest: string;
      };
      await command("knowledge.review", {
        id: knowledge.id,
        expectedVersion: knowledge.version,
        digest: knowledge.digest,
        decision: "approve",
      });
      if (artifact.state === "needs_review")
        await command("artifact.review", {
          id: artifact.id,
          expectedVersion: artifact.version,
          digest: artifact.digest,
          decision: "approve",
        });
      await refresh();
      const latest = (await api<Snapshot>("/company")).data.tasks.find((t) => t.id === task.id);
      if (latest?.state === "awaiting_review")
        await command("task.complete", { id: latest.id, expectedVersion: latest.version });
      await refresh();
    } catch (e) {
      setError((e as Error).message);
      await refresh();
    } finally {
      setBusy(false);
    }
  }

  return (
    <section className="co-ladder" aria-label="Goal ladder">
      <h2>{employee.name}’s goal ladder is ready</h2>
      {!draft ? (
        <p>The draft is missing a section, so it can’t be used as it is. Open it to read it or ask for changes.</p>
      ) : (
        <>
          <p className="co-muted">Check it, change anything that’s wrong, then use it.</p>
          <dl className="co-ladder-draft">
            <dt>Grow</dt>
            <dd className="co-ladder-md">
              <DraftMarkdown>{values.goal}</DraftMarkdown>
            </dd>
            <dt>A new customer’s first step</dt>
            <dd className="co-ladder-md">
              <DraftMarkdown>{values.start}</DraftMarkdown>
            </dd>
            <dt>The steps</dt>
            <dd>
              <ol className="co-ladder-list">
                {steps.map((s) => (
                  <li key={s.name}>
                    <b>{s.name}</b>
                    {s.measure && <small>{s.measure}</small>}
                  </li>
                ))}
              </ol>
            </dd>
            <dt>Work on first</dt>
            <dd className="co-ladder-md">
              <DraftMarkdown>{values.focus}</DraftMarkdown>
            </dd>
            {draft.notes && (
              <>
                <dt>Notes for you</dt>
                <dd className="co-ladder-md co-muted">
                  <DraftMarkdown>{draft.notes}</DraftMarkdown>
                </dd>
              </>
            )}
          </dl>
          {canManage && (
            <details className="co-ladder-edit">
              <summary>Change it before using it</summary>
              {fields.map((f) => (
                <label key={f}>
                  {
                    {
                      goal: "Grow",
                      start: "A new customer’s first step",
                      steps: "The steps, one per line as “Step: the number that shows it”",
                      focus: "Work on first",
                    }[f]
                  }
                  <textarea
                    rows={f === "steps" || f === "focus" ? 5 : 2}
                    value={values[f]}
                    onChange={(e) => setValues({ ...values, [f]: e.target.value })}
                  />
                </label>
              ))}
            </details>
          )}
          <p className="co-muted">
            Using it saves these four parts to {employee.name}’s department context, which every later request reads.
            {recurring && ` Recurring work for ${employee.name} will wait for you to review it again.`}
          </p>
        </>
      )}
      {error && <p role="alert">{error}</p>}
      <div className="co-ladder-actions">
        {draft && canManage && (
          <button className="co-primary" disabled={busy} onClick={() => void use()}>
            {busy ? "Saving…" : "Use this ladder"}
          </button>
        )}
        <button onClick={() => openWork(task)}>{draft ? "Open the draft or ask for changes" : "Open the draft"}</button>
      </div>
    </section>
  );
}

function Scorecard({
  snapshot,
  refresh,
  employee,
  ladder,
  again,
  canWork,
  error,
}: {
  snapshot: Snapshot;
  refresh: () => Promise<void>;
  employee: Employee;
  ladder: Ladder;
  again: ReactNode;
  canWork: boolean;
  error: string;
}) {
  const steps = parseSteps(ladder.steps);
  const readings = (snapshot.data.readings ?? [])
    .filter((r) => r.employeeId === employee.id)
    .sort((a, b) => a.week.localeCompare(b.week));
  const latest = readings.at(-1);
  const before = latest && readings.find((r) => r.week === addWeeks(latest.week, -1));
  const drop = biggestDrop(steps, latest, before);
  const [focusText, leave] = splitLeave(ladder.focus);
  const focusStep = steps.find((s) => ladder.focus.toLowerCase().includes(s.name.toLowerCase()));
  // Done Marketing drafts are the changes; each is judged at the focus step by the fixed rule (docs/adr/0172).
  const d = snapshot.data;
  // The focus step is where people should move on, so a change is judged on the share reaching the step after it.
  const judged = Math.max(1, focusStep ? Math.min(steps.indexOf(focusStep) + 1, steps.length - 1) : steps.length - 1);
  const changes = readings.length
    ? d.tasks
        .filter((t) => t.employeeId === employee.id && t.skill === "marketing-goal-draft" && t.state === "completed")
        .map((t) => {
          const done = d.taskTimes?.[t.id]?.updated;
          const title = d.threads.find((h) => h.id === t.threadId)?.title || t.input;
          return done ? { id: t.id, title, week: weekOf(new Date(done)) } : undefined;
        })
        .filter(
          (c): c is { id: string; title: string; week: string } => !!c && c.week >= addWeeks(weekOf(new Date()), -10),
        )
        .reverse()
        .slice(0, 4)
    : [];
  const weekLabel = (w: string) =>
    new Date(w + "T12:00:00").toLocaleDateString(undefined, { month: "short", day: "numeric" });
  return (
    <section className="co-ladder" aria-label="Goal ladder">
      <div className="co-ladder-head">
        <h2>Your goal ladder</h2>
        {again}
      </div>
      <div className="co-ladder-goal co-ladder-md">
        <b>Grow</b>
        <DraftMarkdown>{ladder.goal}</DraftMarkdown>
      </div>
      <ol className="co-ladder-steps" aria-label="The steps">
        {steps.map((step, i) => {
          const value = valueIn(latest, step.name);
          const previous = i > 0 ? valueIn(latest, steps[i - 1].name) : undefined;
          return (
            <li key={step.name} className={step === focusStep ? "focus" : undefined}>
              <b>{step.name}</b>
              <small>{step.measure}</small>
              <strong>{value === undefined ? "–" : value.toLocaleString()}</strong>
              {value !== undefined && previous !== undefined && previous > 0 && (
                <em>{shareText(previous, value)} of the step before</em>
              )}
              {step === focusStep && <span className="co-ladder-tag">Work on first</span>}
            </li>
          );
        })}
      </ol>
      <div className="co-ladder-focus">
        <b>Work on first</b>
        <div className="co-ladder-md">
          <DraftMarkdown>{focusText}</DraftMarkdown>
        </div>
        {leave && (
          <div className="co-ladder-md co-muted">
            <b>Leave alone for now</b>
            <DraftMarkdown>{leave}</DraftMarkdown>
          </div>
        )}
      </div>
      {drop && (
        <p className="co-ladder-drop" role="status">
          Fewer people went from {drop.from} to {drop.to} than the week before: {drop.now}, down from {drop.before}.
        </p>
      )}
      {changes.length > 0 && steps.length > 1 && (
        <div className="co-ladder-changes" aria-label="Changes being measured">
          <b>
            Changes being measured at {steps[judged - 1].name} → {steps[judged].name}
          </b>
          <ul>
            {changes.map((c) => {
              const check = measureChange(steps, judged, readings, c.week);
              return (
                <li key={c.id}>
                  {c.title} <small>· done the week of {weekLabel(c.week)}</small>
                  <br />
                  <small>
                    {check.verdict === "too early"
                      ? `Too early to judge: needs two weeks of numbers on each side, from the week of ${weekLabel(check.judgeFrom!)}.`
                      : check.verdict === "working"
                        ? `Working: ${check.before} before, ${check.after} after.`
                        : check.verdict === "not working"
                          ? `Not working: ${check.before} before, ${check.after} after. Consider changing it back.`
                          : `No clear change: ${check.before} before, ${check.after} after.`}
                  </small>
                </li>
              );
            })}
          </ul>
        </div>
      )}
      {canWork && (
        <Numbers
          key={latest?.id ?? "none"}
          employee={employee}
          steps={steps.map((s) => s.name)}
          readings={readings}
          refresh={refresh}
        />
      )}
      {error && <p role="alert">{error}</p>}
    </section>
  );
}

function splitLeave(focus: string): [string, string] {
  const match = /leave alone for now\s*:\s*/i.exec(focus);
  return match ? [focus.slice(0, match.index).trim(), focus.slice(match.index + match[0].length).trim()] : [focus, ""];
}

function Numbers({
  employee,
  steps,
  readings,
  refresh,
}: {
  employee: Employee;
  steps: string[];
  readings: Reading[];
  refresh: () => Promise<void>;
}) {
  const thisWeek = weekOf(new Date()),
    lastWeek = addWeeks(thisWeek, -1);
  const [week, setWeek] = useState(readings.some((r) => r.week === lastWeek) ? thisWeek : lastWeek);
  const existing = readings.find((r) => r.week === week);
  const [values, setValues] = useState<Record<string, string>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [saved, setSaved] = useState("");
  useEffect(() => {
    setValues(Object.fromEntries(steps.map((s) => [s, valueIn(existing, s)?.toString() ?? ""])));
    setSaved("");
  }, [week, existing?.version]);
  const label = (w: string) =>
    new Date(w + "T12:00:00").toLocaleDateString(undefined, { month: "short", day: "numeric" });

  async function save(e: FormEvent) {
    e.preventDefault();
    const entries = steps
      .map((name) => ({ name, text: (values[name] ?? "").replace(/[,\s]/g, "") }))
      .filter((v) => v.text);
    if (!entries.length) return setError("Enter at least one number.");
    if (entries.some((v) => !/^\d+(\.\d{1,2})?$/.test(v.text))) return setError("Use whole numbers, such as 1200.");
    setBusy(true);
    setError("");
    try {
      await command("reading.record", {
        employeeId: employee.id,
        name: week,
        expectedVersion: existing?.version ?? 0,
        body: JSON.stringify(entries.map((v) => ({ name: v.name, value: Number(v.text) }))),
      });
      setSaved("Saved.");
      await refresh();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  return (
    <details className="co-ladder-numbers" open={!readings.length}>
      <summary>
        {readings.length
          ? `Numbers for the week of ${label(readings.at(-1)!.week)} · update or add a week`
          : "Add your numbers for a week"}
      </summary>
      <form onSubmit={(e) => void save(e)}>
        <label>
          Week
          <select value={week} onChange={(e) => setWeek(e.target.value)}>
            <option value={thisWeek}>This week (from {label(thisWeek)})</option>
            <option value={lastWeek}>Last week (from {label(lastWeek)})</option>
          </select>
        </label>
        <div className="co-ladder-inputs">
          {steps.map((s) => (
            <label key={s}>
              {s}
              <input
                inputMode="numeric"
                value={values[s] ?? ""}
                placeholder="–"
                onChange={(e) => setValues({ ...values, [s]: e.target.value })}
              />
            </label>
          ))}
        </div>
        <p className="co-muted">Leave a number blank if you don’t know it yet. Only you and your team see these.</p>
        {error && <p role="alert">{error}</p>}
        <div className="co-ladder-actions">
          <button className="co-primary" disabled={busy}>
            {busy ? "Saving…" : existing ? "Update numbers" : "Save numbers"}
          </button>
          {saved && <span role="status">{saved}</span>}
        </div>
      </form>
    </details>
  );
}
