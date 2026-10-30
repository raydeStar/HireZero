import { memberStorage as sessionStorage } from "../lib/api";
import { ScheduledBrowserReview, type ScheduledBrowser } from "./ScheduledBrowserReview";
import { canRepeatWork, recurringInstructions } from "./recurringWork";
import { workTitle } from "./Cockpit";
import { useEffect, useRef, useState } from "react";
import { api, company } from "../lib/api";
import { command, reconcileCompanyOperation, UnconfirmedCompanyOperation, type Snapshot, type Work } from "./api";
import "./schedules.css";
import { ModelChoice, providerName } from "./ModelChoice";

type Definition = {
  name: string;
  kind: "employee-task" | "source-refresh" | "script-task" | "browser-task";
  cadence: "daily" | "weekly";
  timeZone: string;
  startDate: string;
  localTime: string;
  occurrences: number;
  employeeId?: string | null;
  prompt?: string | null;
  creditAllowance: number;
  modelRoute?: string | null;
  includeOccurrenceTime?: boolean;
  profileId?: string | null;
  sourceIds?: string[] | null;
  scriptJobId?: string | null;
  browserJobId?: string | null;
  skill?: string | null;
  evidence?: string[] | null;
};
type Schedule = {
  id: string;
  definition: Definition;
  digest: string;
  state: string;
  reviewCurrent?: boolean | null;
  version: number;
  consumed: number;
  nextRunAt: string | null;
  reason: string | null;
  authority: {
    route: string | null;
    terms?: { provider: string; allowanceUnit: "credits" | "usd-cents" } | null;
    policy: {
      planningTokens: number;
      outputTarget: number;
      localDeadlineSeconds: number;
      applicationBytes: number;
      visibleCharacterStop: number;
      effort?: string;
    } | null;
    context: {
      companyFacts: string;
      departmentFacts: string;
      notes: string;
      responsibility: string;
      instructions: string;
      sourceUrls: string;
      selectedResults?: string | null;
    };
    browser?: ScheduledBrowser | null;
    script?: {
      name: string;
      script: string;
      documents: { documentId: string; name: string }[];
      outputs: string[];
      limits: { seconds: number; memoryMiB: number; temporaryMiB: number };
      tier: string | null;
    } | null;
  };
};
type Run = {
  id: string;
  state: string;
  dueAt: string;
  startedAt: string;
  completedAt: string | null;
  missed: number;
  taskId: string | null;
  refreshId: string | null;
  reason: string | null;
};
type Detail = { schedule: Schedule; runs: Run[]; upcoming: string[] };
type ScriptTemplate = { id: string; name: string; employeeId: string; seconds: number; tier: string | null };

function initial(snapshot: Snapshot): Definition {
  const tomorrow = new Date();
  tomorrow.setDate(tomorrow.getDate() + 1);
  return {
    name: "",
    kind: "employee-task",
    cadence: "weekly",
    timeZone: Intl.DateTimeFormat().resolvedOptions().timeZone || "UTC",
    startDate: `${tomorrow.getFullYear()}-${String(tomorrow.getMonth() + 1).padStart(2, "0")}-${String(tomorrow.getDate()).padStart(2, "0")}`,
    localTime: "09:00",
    occurrences: 4,
    employeeId: snapshot.data.employees.find((e) => e.state === "active")?.id || "",
    prompt: "",
    creditAllowance: 1,
    includeOccurrenceTime: true,
    sourceIds: [],
  };
}

export function Schedules({
  snapshot,
  role,
  refresh,
  onOpenWork,
  onSources,
  initialId,
  initialTaskId,
  onReviewSchedule,
}: {
  snapshot: Snapshot;
  role?: string;
  refresh: () => Promise<void>;
  onOpenWork: (work: Work) => void;
  onSources: () => void;
  initialId?: string;
  initialTaskId?: string;
  onReviewSchedule?: (id: string) => void;
}) {
  const sourceTask = snapshot.data.tasks.find((t) => t.id === initialTaskId && canRepeatWork(snapshot, t));
  const key = `hirezero-schedule-draft-v1:${company() || "local-company"}${initialTaskId ? ":repeat:" + initialTaskId : ""}`,
    receiptKey = `${key}:receipt`;
  const [form, setForm] = useState<Definition>(() => {
    try {
      const saved = JSON.parse(sessionStorage.getItem(key) || "null");
      if (
        saved &&
        typeof saved.name === "string" &&
        ["employee-task", "source-refresh", "script-task", "browser-task"].includes(saved.kind)
      )
        return saved;
    } catch {}
    if (!sourceTask) return initial(snapshot);
    return {
      ...initial(snapshot),
      name: ("Recurring: " + workTitle(snapshot, sourceTask)).slice(0, 120),
      employeeId: sourceTask.employeeId,
      skill: sourceTask.skill,
      prompt: recurringInstructions(snapshot, sourceTask),
      modelRoute: sourceTask.requestedModelRoute,
      evidence: [
        ...new Set([
          ...(sourceTask.evidence || []),
          ...snapshot.data.artifacts
            .filter(
              (a) =>
                (a.taskId === sourceTask.id || sourceTask.dependencies?.includes(a.taskId)) && a.state === "approved",
            )
            .map((a) => a.id),
        ]),
      ],
      creditAllowance:
        snapshot.data.measured?.filter((m) => m.taskId === sourceTask.id).slice(-1)[0]?.job.creditAllowance ?? 1,
    };
  });
  const [showForm, setShowForm] = useState(() => !initialId && (!!form.name || !!form.prompt));
  const [editTarget, setEditTarget] = useState<{ id: string; version: number } | null>(() => {
    try {
      return JSON.parse(sessionStorage.getItem(`${key}:target`) || "null");
    } catch {
      return null;
    }
  });
  function target(value: { id: string; version: number } | null) {
    setEditTarget(value);
    try {
      value
        ? sessionStorage.setItem(`${key}:target`, JSON.stringify(value))
        : sessionStorage.removeItem(`${key}:target`);
    } catch {}
  }
  const [pending, setPending] = useState(() => {
    try {
      return sessionStorage.getItem(receiptKey) || "";
    } catch {
      return "";
    }
  });
  const [list, setList] = useState<Schedule[]>([]),
    [detail, setDetail] = useState<Detail | null>(null),
    [selected, setSelected] = useState(initialId || "");
  const [busy, setBusy] = useState(false),
    [loading, setLoading] = useState(true),
    [error, setError] = useState(""),
    [notice, setNotice] = useState(""),
    [consent, setConsent] = useState(false);
  const lock = useRef(false),
    epoch = useRef(0),
    selection = useRef(selected);
  const statusNotice = useRef<{ id: string; state: string } | null>(null);
  selection.current = selected;
  const owner = !role || role === "owner";
  const [scriptTemplates, setScriptTemplates] = useState<ScriptTemplate[]>([]);
  const [browserTemplates, setBrowserTemplates] = useState<Omit<ScriptTemplate, "tier">[]>([]);
  const selectedEmployee = snapshot.data.employees.find((e) => e.id === form.employeeId);
  const selectedRole = snapshot.data.roles.find((r) => r.id === selectedEmployee?.roleId);
  const unavailableSkill =
    form.kind === "employee-task" &&
    !!form.skill &&
    form.skill !== "chat" &&
    !selectedRole?.measuredSkills?.includes(form.skill);
  const unavailableScript =
    form.kind === "script-task" && !!form.scriptJobId && !scriptTemplates.some((item) => item.id === form.scriptJobId);
  const unavailableBrowser =
    form.kind === "browser-task" &&
    !!form.browserJobId &&
    !browserTemplates.some((item) => item.id === form.browserJobId);
  const cents =
    (snapshot.capabilities.modelChoices?.find((option) => option.route === form.modelRoute)?.allowanceUnit ??
      snapshot.capabilities.allowanceUnit) === "usd-cents";
  const profile = snapshot.data.businessProfiles?.find((p) => p.state === "approved");
  const cited = [...new Set(profile?.fields.flatMap((f) => f.citations.map((c) => c.sourceId)) || [])];
  async function load(id = selection.current) {
    const generation = ++epoch.current;
    const [saved, templates, browsers] = await Promise.all([
      api<Schedule[]>("/company/schedules"),
      api<ScriptTemplate[]>("/company/schedules/script-templates"),
      api<Omit<ScriptTemplate, "tier">[]>("/company/schedules/browser-templates"),
    ]);
    const record = id ? await api<Detail>(`/company/schedules/${encodeURIComponent(id)}`) : null;
    if (generation !== epoch.current) return;
    if (
      record &&
      statusNotice.current?.id === record.schedule.id &&
      statusNotice.current.state !== record.schedule.state
    ) {
      setNotice("");
      statusNotice.current = null;
    }
    setList(saved);
    setScriptTemplates(templates);
    setBrowserTemplates(browsers);
    setDetail(record);
    setLoading(false);
  }
  useEffect(() => {
    void load().catch((e) => {
      setError((e as Error).message);
      setLoading(false);
    });
    return () => {
      epoch.current++;
    };
  }, []);
  useEffect(() => {
    // The address and the visible approval must tell the same story, even after Back.
    if ((initialId || "") !== selection.current) void choose(initialId || "", false);
  }, [initialId]);
  useEffect(() => {
    if (!list.some((s) => s.state === "active")) return;
    const timer = window.setTimeout(() => {
      void load().catch(() => setError("Schedule status could not refresh. Check saved status before changing it."));
    }, 3000);
    return () => window.clearTimeout(timer);
  }, [list]);
  function edit(next: Definition) {
    setForm(next);
    try {
      sessionStorage.setItem(key, JSON.stringify(next));
    } catch {
      setNotice("This browser cannot retain unfinished edits after closing. Save your draft before leaving.");
    }
  }
  function remember(id: string) {
    setPending(id);
    try {
      id ? sessionStorage.setItem(receiptKey, id) : sessionStorage.removeItem(receiptKey);
    } catch {}
  }
  async function perform(action?: string, fields: Record<string, unknown> = {}) {
    if (lock.current) return;
    lock.current = true;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const result = (
        pending ? await reconcileCompanyOperation(pending) : await command(action!, fields, remember)
      ) as Schedule;
      remember("");
      setSelected(result.id);
      selection.current = result.id;
      setConsent(false);
      statusNotice.current = { id: result.id, state: result.state };
      if (result.state === "draft") {
        setShowForm(false);
        setForm(initial(snapshot));
        target(null);
        try {
          sessionStorage.removeItem(key);
        } catch {}
      }
      setNotice(
        result.state === "draft"
          ? "Schedule draft saved. Review its dates, context and limits before authorizing."
          : `Schedule ${result.state}.`,
      );
      if (result.state === "draft" && onReviewSchedule && (initialTaskId || result.id !== initialId)) {
        onReviewSchedule(result.id);
        if (initialTaskId) {
          await refresh();
          return;
        }
      }
      await load(result.id);
      await refresh();
    } catch (e) {
      if (!(e instanceof UnconfirmedCompanyOperation) && !pending) remember("");
      setError((e as Error).message);
    } finally {
      lock.current = false;
      setBusy(false);
    }
  }
  async function choose(id: string, updateRoute = true) {
    setNotice("");
    statusNotice.current = null;
    setSelected(id);
    selection.current = id;
    setDetail(null);
    setConsent(false);
    setError("");
    if (updateRoute && id !== initialId) onReviewSchedule?.(id);
    try {
      await load(id);
    } catch (e) {
      setError((e as Error).message);
    }
  }
  const schedule = detail?.schedule;
  async function openResult(id: string) {
    setError("");
    try {
      const result = await api<{ task: Work }>(`/company/tasks/${encodeURIComponent(id)}`);
      await refresh();
      onOpenWork(result.task);
    } catch (e) {
      setError((e as Error).message);
    }
  }
  const date = (value: string) => {
    try {
      return new Date(value).toLocaleString(undefined, {
        timeZone: schedule?.definition.timeZone,
        timeZoneName: "short",
      });
    } catch {
      return new Date(value).toLocaleString();
    }
  };
  const review = (decision: string) =>
    schedule &&
    perform("schedule.review", {
      id: schedule.id,
      expectedVersion: schedule.version,
      digest: schedule.digest,
      decision,
    });
  return (
    <section className="co-schedules" aria-label="Recurring work">
      <header>
        <p className="co-eyebrow">Dependable repeat work</p>
        <h2>Give useful work a rhythm.</h2>
        <p>
          Schedule employee deliverables, saved scripts, website reviews or checks of approved sources. Each schedule
          has a finite number of runs and explicit owner approval.
        </p>
      </header>
      {initialTaskId && (
        <p role="status">
          {sourceTask
            ? `Start from ${workTitle(snapshot, sourceTask)}. New drafts include its request, revision feedback, skill and approved results as references. Your saved edits are kept. Review what each run should produce, the timing, context and allowance before authorizing any runs.`
            : "The original work is no longer available to repeat. Any saved schedule draft is preserved for your review."}
        </p>
      )}
      {sourceTask?.revision && (
        <details>
          <summary>Original request and revision feedback</summary>
          <p>Compare this history with the editable instructions. Saved drafts stay as you left them.</p>
          <pre>{recurringInstructions(snapshot, sourceTask)}</pre>
        </details>
      )}
      {error && (
        <p role="alert" className="co-error">
          {error}
        </p>
      )}
      {notice && <p role="status">{notice}</p>}
      {pending && (
        <div role="status">
          <p>
            {busy
              ? "Saving this request. Its receipt will confirm the outcome."
              : "This request needs confirmation. It will not be repeated."}
          </p>
          <button disabled={busy} onClick={() => void perform()}>
            Check schedule receipt
          </button>
        </div>
      )}
      {owner && !showForm && (
        <button className="co-primary" disabled={busy || !!pending} onClick={() => setShowForm(true)}>
          {form.name || form.prompt ? "Continue unsaved draft" : "New schedule"}
        </button>
      )}
      <button
        disabled={busy}
        onClick={() => {
          setError("");
          void load().catch((e) => setError((e as Error).message));
        }}
      >
        Refresh schedule status
      </button>
      {showForm && owner && (
        <form
          className="co-schedule-editor"
          onSubmit={(e) => {
            e.preventDefault();
            void perform("schedule.save", {
              id: editTarget?.id,
              expectedVersion: editTarget?.version || 0,
              body: JSON.stringify(form.kind === "source-refresh" ? { ...form, profileId: profile?.id } : form),
            });
          }}
        >
          <h3>Plan the recurring work</h3>
          <fieldset disabled={busy || !!pending}>
            <label>
              Schedule name
              <input
                required
                maxLength={120}
                value={form.name}
                onChange={(e) => edit({ ...form, name: e.target.value })}
              />
            </label>
            <label>
              Work to repeat
              <select
                value={form.kind}
                onChange={(e) =>
                  edit({
                    ...form,
                    kind: e.target.value as Definition["kind"],
                    sourceIds: cited.slice(0, 3),
                    skill: null,
                    evidence: null,
                  })
                }
              >
                <option value="employee-task">Private employee deliverable</option>
                <option value="source-refresh">Check approved website sources</option>
                <option value="script-task">Run a saved script</option>
                <option value="browser-task">Repeat a website review</option>
              </select>
            </label>
            {form.kind === "employee-task" ? (
              <>
                <label>
                  Employee
                  <select
                    required
                    value={form.employeeId || ""}
                    onChange={(e) => edit({ ...form, employeeId: e.target.value, skill: null, evidence: null })}
                  >
                    <option value="">Choose an active employee</option>
                    {snapshot.data.employees
                      .filter((e) => e.state === "active")
                      .map((e) => (
                        <option key={e.id} value={e.id}>
                          {e.name}
                        </option>
                      ))}
                  </select>
                </label>
                <label>
                  Employee skill
                  <select value={form.skill || "chat"} onChange={(e) => edit({ ...form, skill: e.target.value })}>
                    <option value="chat">Short private update</option>
                    {unavailableSkill && (
                      <option value={form.skill!} disabled>
                        Previous skill (unavailable)
                      </option>
                    )}
                    {(selectedRole?.measuredSkills || []).map((skill) => (
                      <option key={skill} value={skill}>
                        {selectedRole?.skillNames?.[skill] || skill}
                      </option>
                    ))}
                  </select>
                </label>
                {unavailableSkill && <p role="alert">Choose a skill currently offered by this employee.</p>}
                {(form.evidence?.length || 0) > 0 && (
                  <section aria-label="Selected result references">
                    <h4>Approved results supplied on each run</h4>
                    <p>
                      Keep the copies that show the quality and detail you want. These are fixed references; later
                      results are not added automatically. Revoking or changing a selected result stops future runs.
                    </p>
                    {form.evidence!.map((id, index) => {
                      const artifact = snapshot.data.artifacts.find((a) => a.id === id);
                      const task = snapshot.data.tasks.find((t) => t.id === artifact?.taskId);
                      return (
                        <div key={id} data-reference-id={id}>
                          <details>
                            <summary>
                              Reference {index + 1}: {task ? workTitle(snapshot, task) : "Previous result"} ·{" "}
                              {artifact?.state || "unavailable"}
                            </summary>
                            {artifact && <pre>{artifact.body}</pre>}
                          </details>
                          <button
                            type="button"
                            aria-label={`Remove reference ${index + 1}`}
                            onClick={() => edit({ ...form, evidence: form.evidence!.filter((item) => item !== id) })}
                          >
                            Remove reference
                          </button>
                        </div>
                      );
                    })}
                  </section>
                )}
                <label>
                  Instructions for each run
                  <textarea
                    required
                    rows={4}
                    maxLength={2000}
                    value={form.prompt || ""}
                    onChange={(e) => edit({ ...form, prompt: e.target.value })}
                  />
                </label>
                {(form.prompt?.length || 0) > 2000 && (
                  <p role="alert">
                    Consolidate the request and feedback into at most 2,000 characters before saving. Keep the
                    requirements you still want. The full text stays in this draft until you edit it.
                  </p>
                )}
                <label className="co-check">
                  <input
                    type="checkbox"
                    checked={!!form.includeOccurrenceTime}
                    onChange={(e) => edit({ ...form, includeOccurrenceTime: e.target.checked })}
                  />
                  Include the scheduled date in each request
                </label>
                <p>
                  Say what should be fresh each run and what a useful result contains. For a content plan, ask for new
                  examples using approved facts, with complete drafts and a way to assess their use. Save audience
                  feedback as approved memory accessible to this employee so later work can find it. The schedule does
                  not gather feedback on its own.
                </p>
                <p>Results stay private. This workflow has no browsing, publishing or other external actions.</p>
                <ModelChoice
                  options={snapshot.capabilities.modelChoices}
                  value={form.modelRoute ?? ""}
                  onChange={(modelRoute) => edit({ ...form, modelRoute, creditAllowance: 0 })}
                />
                <label>
                  {cents ? "Spending allowance per run (US cents)" : "Existing-credit fallback allowance per run"}
                  <input
                    type="number"
                    required
                    min="0.01"
                    max="1000"
                    step="0.01"
                    value={form.creditAllowance}
                    onChange={(e) => edit({ ...form, creditAllowance: Number(e.target.value) })}
                  />
                </label>
                <small>
                  {cents
                    ? "100 cents = $1. The configured API provider checks the estimated request cost against this allowance."
                    : "This is an explicit soft allowance, not a provider-enforced spending cap."}{" "}
                  Existing company and employee budgets also apply.
                </small>
              </>
            ) : form.kind === "browser-task" ? (
              <>
                <label>
                  Saved website review
                  <select
                    required
                    value={form.browserJobId || ""}
                    onChange={(e) => {
                      const template = browserTemplates.find((item) => item.id === e.target.value);
                      edit({ ...form, browserJobId: template?.id || "", employeeId: template?.employeeId || "" });
                    }}
                  >
                    <option value="">Choose a saved itinerary</option>
                    {unavailableBrowser && (
                      <option value={form.browserJobId!} disabled>
                        Previous website review (unavailable)
                      </option>
                    )}
                    {browserTemplates.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.name} ·{" "}
                        {snapshot.data.employees.find((e) => e.id === item.employeeId)?.name || "Employee"} ·{" "}
                        {item.seconds}s
                      </option>
                    ))}
                  </select>
                </label>
                {unavailableBrowser && (
                  <p role="status">
                    The original website review is no longer available for a new draft. Choose another saved itinerary
                    or keep this draft until it is available.
                  </p>
                )}
                {browserTemplates.length === 0 && (
                  <p>
                    Prepare a standalone website review on an employee's work task first. Saving its itinerary does not
                    visit any website.
                  </p>
                )}
                <p>
                  Each occurrence repeats the same public websites and steps, saves fresh dated captures and uses the
                  shared compute allowance. Review the exact itinerary before authorizing.
                </p>
              </>
            ) : form.kind === "script-task" ? (
              <>
                <label>
                  Saved script
                  <select
                    required
                    value={form.scriptJobId || ""}
                    onChange={(e) => {
                      const template = scriptTemplates.find((item) => item.id === e.target.value);
                      edit({ ...form, scriptJobId: template?.id || "", employeeId: template?.employeeId || "" });
                    }}
                  >
                    <option value="">Choose a saved script review</option>
                    {unavailableScript && (
                      <option value={form.scriptJobId!} disabled>
                        Previous saved script (unavailable)
                      </option>
                    )}
                    {scriptTemplates.map((item) => (
                      <option key={item.id} value={item.id}>
                        {item.name} ·{" "}
                        {snapshot.data.employees.find((e) => e.id === item.employeeId)?.name || "Employee"} ·{" "}
                        {item.seconds}s{item.tier ? " · extended" : ""}
                      </option>
                    ))}
                  </select>
                </label>
                {unavailableScript && (
                  <p role="status">
                    The original script is no longer available for a new draft. Choose another saved script review or
                    prepare one in the employee's Work tab. Existing authorized schedules keep their own approval.
                  </p>
                )}
                {scriptTemplates.length === 0 && (
                  <p>
                    Prepare a script in an employee's Work tab first. Standalone scripts using approved documents can be
                    scheduled; task-result inputs need their own review.
                  </p>
                )}
                <p>
                  The script and selected documents stay fixed. Each run follows their explicitly approved replacements.
                  It stops if there is no approved revision, competing approved replacements, or a revision outside this
                  employee's access. Draft text is never used. No network, account credentials or unrelated files are
                  provided.
                </p>
                <p>
                  Each run reserves its full duration from the shared monthly compute allowance. Extended scripts also
                  require the reviewed paid plan. No extra usage is purchased.
                </p>
              </>
            ) : (
              <>
                {!cited.length ? (
                  <p>Approve a brief with website citations before scheduling source checks.</p>
                ) : (
                  <p>
                    Choose up to three sources from the current approved brief. A changed brief will require a new
                    schedule.
                  </p>
                )}
                {cited.map((id) => (
                  <label className="co-schedule-check" key={id}>
                    <input
                      type="checkbox"
                      checked={form.sourceIds?.includes(id) || false}
                      disabled={!form.sourceIds?.includes(id) && (form.sourceIds?.length || 0) >= 3}
                      onChange={(e) =>
                        edit({
                          ...form,
                          sourceIds: e.target.checked
                            ? [...(form.sourceIds || []), id]
                            : (form.sourceIds || []).filter((s) => s !== id),
                        })
                      }
                    />
                    <span>{snapshot.data.businessSources?.find((s) => s.id === id)?.url || "Cited source"}</span>
                  </label>
                ))}
              </>
            )}
            <div className="co-schedule-grid">
              <label>
                Repeat
                <select
                  value={form.cadence}
                  onChange={(e) => edit({ ...form, cadence: e.target.value as Definition["cadence"] })}
                >
                  <option value="weekly">Weekly, on the starting weekday</option>
                  <option value="daily">Daily</option>
                </select>
              </label>
              <label>
                Number of occurrences
                <input
                  required
                  type="number"
                  min="1"
                  max="31"
                  value={form.occurrences}
                  onChange={(e) => edit({ ...form, occurrences: Number(e.target.value) })}
                />
              </label>
              <label>
                First date
                <input
                  required
                  type="date"
                  value={form.startDate}
                  onChange={(e) => edit({ ...form, startDate: e.target.value })}
                />
              </label>
              <label>
                Local time
                <input
                  required
                  type="time"
                  value={form.localTime}
                  onChange={(e) => edit({ ...form, localTime: e.target.value })}
                />
              </label>
            </div>
            <label>
              Time zone
              <input
                required
                maxLength={100}
                value={form.timeZone}
                onChange={(e) => edit({ ...form, timeZone: e.target.value })}
                placeholder="America/Denver"
              />
            </label>
            <p>
              The workspace must be running. A slot more than one hour late is recorded as missed and counts toward the
              approved total. Missed slots never create a burst of catch-up work.
            </p>
            <button
              type="submit"
              className="co-primary"
              disabled={
                (form.kind === "employee-task" && (unavailableSkill || (form.prompt?.length || 0) > 2000)) ||
                (form.kind === "source-refresh" && !form.sourceIds?.length) ||
                (form.kind === "script-task" && (!form.scriptJobId || unavailableScript)) ||
                (form.kind === "browser-task" && (!form.browserJobId || unavailableBrowser))
              }
            >
              Save schedule draft
            </button>
            <button type="button" onClick={() => setShowForm(false)}>
              Keep draft for later
            </button>
          </fieldset>
        </form>
      )}
      {loading ? (
        <p role="status">Loading saved schedules…</p>
      ) : (
        !list.length && <p>No schedules yet. Start with one recurring task you would otherwise do by hand.</p>
      )}
      <ul className="co-schedule-list">
        {list.map((s) => (
          <li key={s.id}>
            <button aria-pressed={selected === s.id} disabled={busy} onClick={() => void choose(s.id)}>
              <strong>{s.definition.name}</strong>
              <span>
                {s.reviewCurrent === false ? "Review needed" : s.state} ·{" "}
                {s.state === "canceled"
                  ? `${s.consumed} of ${s.definition.occurrences} occurrences consumed`
                  : `${s.definition.occurrences - s.consumed} occurrences remaining`}
              </span>
            </button>
          </li>
        ))}
      </ul>
      {schedule && (
        <article className="co-schedule-review" aria-label="Schedule review">
          <h3>{schedule.definition.name}</h3>
          <p>
            <b>{schedule.reviewCurrent === false ? "Review needed" : schedule.state}</b> · {schedule.definition.cadence}{" "}
            at {schedule.definition.localTime} in {schedule.definition.timeZone}
          </p>
          {schedule.reviewCurrent === false && (
            <p role="status" className="co-error">
              The owner permission, employee, approved context or runtime has changed. Remaining runs will not start
              under this review.{" "}
              {schedule.state === "draft"
                ? "Edit and save this draft to review the current scope."
                : "Copy into a new draft and review its scope, dates and allowance."}
            </p>
          )}
          {schedule.reason && (
            <p role="status" className="co-error">
              {schedule.reason}
            </p>
          )}
          <p>
            {schedule.consumed} of {schedule.definition.occurrences} occurrences consumed.{" "}
            {schedule.state === "canceled"
              ? "This schedule has ended. Unused slots will not run."
              : schedule.nextRunAt
                ? `Next slot: ${date(schedule.nextRunAt)}.`
                : "No further slots remain."}
          </p>
          {schedule.state !== "canceled" && detail.upcoming.length > 0 && (
            <details>
              <summary>Upcoming dates and clock changes</summary>
              <ul>
                {detail.upcoming.map((d) => (
                  <li key={d}>{date(d)}</li>
                ))}
              </ul>
              <p>
                Spring clock gaps move to the first valid minute. Repeated autumn times run once, at the earlier
                occurrence.
              </p>
            </details>
          )}
          {schedule.definition.kind === "employee-task" && (
            <p>
              {snapshot.data.employees.find((e) => e.id === schedule.definition.employeeId)?.name || "Employee"} ·{" "}
              {schedule.definition.skill
                ? snapshot.data.roles.find(
                    (r) =>
                      r.id === snapshot.data.employees.find((e) => e.id === schedule.definition.employeeId)?.roleId,
                  )?.skillNames?.[schedule.definition.skill] || schedule.definition.skill
                : "Short private update"}{" "}
              · {schedule.authority.route}
            </p>
          )}
          {schedule.definition.prompt && <blockquote>{schedule.definition.prompt}</blockquote>}
          {schedule.definition.kind === "employee-task" && (
            <p>
              {schedule.definition.includeOccurrenceTime
                ? `Each request includes its scheduled date and time in ${schedule.definition.timeZone}, cadence and run number. This anchors dates in the work; it does not supply new research or earlier results.`
                : "This schedule sends the same instructions without a scheduled date or run number. To add them, prepare and approve a new schedule."}
            </p>
          )}
          {schedule.authority.browser && (
            <ScheduledBrowserReview
              browser={schedule.authority.browser}
              remaining={schedule.definition.occurrences - schedule.consumed}
            />
          )}
          {schedule.authority.script && (
            <section className="co-schedule-script" aria-label="Recurring script review">
              <h4>{schedule.authority.script.name}</h4>
              <p>
                {schedule.authority.script.tier ? "Extended script" : "Standard script"} ·{" "}
                {schedule.authority.script.limits.seconds} seconds per run ·{" "}
                {schedule.authority.script.limits.seconds * (schedule.definition.occurrences - schedule.consumed)}{" "}
                seconds across remaining occurrences. Each run uses the shared monthly compute pool and needs confirmed
                cleanup before the next occurrence.
              </p>
              <p>
                {schedule.authority.script.limits.memoryMiB} MiB memory ·{" "}
                {schedule.authority.script.limits.temporaryMiB} MiB working files · No network access.
              </p>
              <details open>
                <summary>Exact script and selected inputs</summary>
                <pre>{schedule.authority.script.script}</pre>
                <ul>
                  {schedule.authority.script.documents.map((document) => (
                    <li key={document.documentId}>
                      {document.name} · current approved revision, including explicitly approved replacements
                    </li>
                  ))}
                </ul>
                {schedule.authority.script.documents.length === 0 && <p>No company documents are supplied.</p>}
                <p>Selected outputs: {schedule.authority.script.outputs.join(", ") || "Console output only"}.</p>
              </details>
              <p>
                Results stay private and need owner review before task completion. Pausing stops active work; cleanup
                and usage holds remain until confirmed. Completed reports stay available when future occurrences end.
              </p>
              <p>
                This schedule has its own approval. Discarding the original script proposal does not stop it; use Pause
                schedule or End schedule here.
              </p>
            </section>
          )}
          {schedule.authority.policy && (
            <p>
              {schedule.authority.terms && (
                <>
                  Account: {providerName(schedule.authority.terms.provider)} · {schedule.authority.route}.{" "}
                </>
              )}
              One model request per run at{" "}
              {schedule.authority.terms?.allowanceUnit === "usd-cents"
                ? "low"
                : (schedule.authority.policy.effort ?? "low")}{" "}
              reasoning effort, with a {schedule.authority.policy.outputTarget.toLocaleString()} observed output-token
              target per run. Reasoning tokens count toward that target. Local deadline:{" "}
              {schedule.authority.policy.localDeadlineSeconds} seconds. Planning allowance:{" "}
              {schedule.authority.policy.planningTokens.toLocaleString()} tokens per run;{" "}
              {(schedule.authority.policy.planningTokens * schedule.definition.occurrences).toLocaleString()} across
              this schedule.{" "}
              {(schedule.authority.terms ? schedule.authority.terms.allowanceUnit === "usd-cents" : cents)
                ? "Spending allowance"
                : "Existing-credit fallback"}
              : {schedule.definition.creditAllowance}{" "}
              {(schedule.authority.terms ? schedule.authority.terms.allowanceUnit === "usd-cents" : cents)
                ? "US cents"
                : "credits"}{" "}
              per run.{" "}
              {(schedule.authority.terms ? schedule.authority.terms.allowanceUnit === "usd-cents" : cents)
                ? "Time and observed token targets are local soft stops; spending is checked before dispatch."
                : "Time, token and existing-credit allowances are soft limits; a run may exceed them."}
            </p>
          )}
          <details>
            <summary>Review the approved context and scope</summary>
            {Object.entries(schedule.authority.context)
              .filter(([, v]) => !!v)
              .map(([k, v]) => (
                <section key={k}>
                  <h4>
                    {
                      {
                        companyFacts: "Company facts",
                        departmentFacts: "Department facts",
                        notes: "Employee notes",
                        responsibility: "Responsibility",
                        instructions: "Employee instructions",
                        sourceUrls: "Selected source addresses",
                        selectedResults: "Approved result references",
                      }[k]
                    }
                  </h4>
                  <pre>{v}</pre>
                </section>
              ))}
            {schedule.definition.kind === "employee-task" && (
              <p>
                Each run may retrieve relevant excerpts from documents and organizational memory you have approved for
                this employee. Conversation history is excluded. Changes to the employee, these facts or notes, or the
                model setup pause the schedule for new approval.
              </p>
            )}
          </details>
          {owner && (
            <div className="co-schedule-controls">
              {["draft", "paused", "blocked"].includes(schedule.state) &&
                schedule.reviewCurrent !== false &&
                schedule.consumed < schedule.definition.occurrences && (
                  <>
                    <label className="co-schedule-check">
                      <input
                        type="checkbox"
                        checked={consent}
                        disabled={busy || !!pending}
                        onChange={(e) => setConsent(e.target.checked)}
                      />
                      <span>
                        {schedule.definition.kind === "employee-task"
                          ? "I authorize the remaining private model requests using this scope and acknowledge the soft token and credit limits."
                          : schedule.definition.kind === "browser-task"
                            ? "I authorize these exact public websites, steps, screenshots and per-run limits for the remaining occurrences, including shared compute allocation."
                            : schedule.definition.kind === "script-task"
                              ? "I authorize this exact script for the remaining occurrences, using only the selected documents and their approved replacements, with the full per-run compute allocation."
                              : "I authorize the remaining bounded reads of these approved sources. Changes will require review and will not rewrite approved facts."}
                      </span>
                    </label>
                    <button
                      className="co-primary"
                      disabled={!consent || busy || !!pending}
                      onClick={() =>
                        void review(
                          schedule.definition.kind === "employee-task"
                            ? "authorize-recurring-soft-limits"
                            : schedule.definition.kind === "browser-task"
                              ? "authorize-recurring-browsers"
                              : schedule.definition.kind === "script-task"
                                ? "authorize-recurring-scripts"
                                : "authorize-recurring-source-reads",
                        )
                      }
                    >
                      {schedule.state === "draft" ? "Authorize schedule" : "Resume remaining occurrences"}
                    </button>
                  </>
                )}
              {schedule.state === "active" && (
                <button disabled={busy || !!pending} onClick={() => void review("pause")}>
                  Pause schedule
                </button>
              )}
              <button
                disabled={busy || !!pending}
                onClick={() => {
                  target(schedule.state === "draft" ? { id: schedule.id, version: schedule.version } : null);
                  edit({
                    ...schedule.definition,
                    startDate: schedule.state === "draft" ? schedule.definition.startDate : initial(snapshot).startDate,
                  });
                  setShowForm(true);
                  requestAnimationFrame(() =>
                    document
                      .querySelector(".co-schedule-editor")
                      ?.scrollIntoView({ behavior: "smooth", block: "start" }),
                  );
                }}
              >
                {schedule.state === "draft" ? "Edit schedule draft" : "Copy into a new draft"}
              </button>
              {!["completed", "canceled"].includes(schedule.state) && (
                <button disabled={busy || !!pending} onClick={() => void review("cancel")}>
                  End schedule
                </button>
              )}
            </div>
          )}
          <h4>Run history</h4>
          {!detail.runs.length && <p>No occurrences have run.</p>}
          <ol className="co-schedule-runs">
            {detail.runs.map((run) => (
              <li key={run.id}>
                <b>{run.state}</b> · {date(run.dueAt)}
                {run.missed > 0 && (
                  <p>
                    {run.missed} missed slot{run.missed === 1 ? "" : "s"}; no catch-up execution.
                  </p>
                )}
                {run.reason && <p>{run.reason}</p>}
                {run.taskId && <button onClick={() => void openResult(run.taskId!)}>Open saved result</button>}
                {run.refreshId && <button onClick={onSources}>Review checked sources</button>}
              </li>
            ))}
          </ol>
        </article>
      )}
    </section>
  );
}
