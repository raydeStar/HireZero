import { useEffect, useRef, useState, type FormEvent } from "react";
import { Check, CircleCheckBig } from "lucide-react";
import { api } from "../lib/api";
import { command, UnconfirmedCompanyOperation, type Knowledge, type Snapshot } from "./api";
import { humanize } from "./labels";
import { ContextFromSources } from "./ContextFromSources";
import { valuesOf } from "./contextDraft";

type Draft = {
  role: string;
  name: string;
  departmentId: string;
  departmentName: string;
  responsibility: string;
  employeeId: string;
  createdDepartmentId?: string;
  values: Record<string, string>;
  knowledgeId: string;
  step: "profile" | "context" | "review" | "ready";
  pending?: { action: string; requestId: string };
};
const key = "hirezero-hire-draft-v1";
const suggestion = (snapshot: Snapshot, role: string) => snapshot.data.roles.find((r) => r.id === role)?.onboarding;
function fresh(snapshot: Snapshot, role = snapshot.data.roles[0]?.id ?? ""): Draft {
  const defaults = suggestion(snapshot, role);
  return {
    role,
    name: defaults?.name || "",
    departmentId: "new",
    departmentName: defaults?.department || "",
    responsibility: defaults?.responsibility || "",
    employeeId: "",
    values: {},
    knowledgeId: "",
    step: "profile",
  };
}
function suggestedDepartment(snapshot: Snapshot, role: string) {
  const candidates = snapshot.data.departments.filter(
    (d) =>
      snapshot.data.employees.find((e) => e.id === d.guide)?.roleId === role ||
      d.name === suggestion(snapshot, role)?.department,
  );
  return candidates.length === 1 ? candidates[0].id : candidates.length > 1 ? "" : "new";
}
function readDraft(snapshot: Snapshot): Draft {
  try {
    const stored = JSON.parse(sessionStorage.getItem(key) || "null");
    if (stored && typeof stored.role === "string" && typeof stored.values === "object" && stored.values !== null)
      return { ...fresh(snapshot, stored.role), ...stored };
  } catch {
    /* Draft storage is optional. */
  }
  const draft = fresh(snapshot);
  return { ...draft, departmentId: suggestedDepartment(snapshot, draft.role) };
}
export function HireOnboarding({
  snapshot,
  refresh,
  open,
  resumeId,
  onResumeHandled,
  onActive,
}: {
  snapshot: Snapshot;
  refresh: () => Promise<void>;
  open: (employee: string, thread: string) => void;
  resumeId?: string;
  onResumeHandled: () => void;
  // Whether a hire is under way (past its first step), so the page can put the guide first (UX audit, 2026-10-20).
  onActive?: (active: boolean) => void;
}) {
  const [sourcesOpen, setSourcesOpen] = useState(false);
  const [draft, setDraft] = useState<Draft>(() => readDraft(snapshot)),
    [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [consent, setConsent] = useState(false),
    [status, setStatus] = useState("");
  useEffect(() => onActive?.(draft.step !== "profile"), [draft.step, onActive]);
  const heading = useRef<HTMLHeadingElement>(null),
    current = useRef(draft),
    locked = useRef(false);
  current.current = draft;
  function change(next: Draft) {
    current.current = next;
    setDraft(next);
    try {
      sessionStorage.setItem(key, JSON.stringify(next));
    } catch {
      /* Keep the in-memory draft when storage is restricted. */
    }
    setConsent(false);
  }
  const data = snapshot.data,
    person = data.employees.find((e) => e.id === draft.employeeId),
    department = data.departments.find((d) => d.id === (person?.departmentId || draft.departmentId));
  const approved = data.knowledge.find((k) => k.id === department?.knowledge && k.state === "approved");
  const saved = data.knowledge.find((k) => k.id === draft.knowledgeId && k.departmentId === department?.id);
  const guide = data.employees.find((e) => e.id === department?.guide),
    role = data.roles.find((r) => r.id === (guide?.roleId || draft.role));
  const fields = role?.onboardingFields || [],
    defaults = role?.onboarding;
  const thread = data.threads.find((t) => t.employeeId === person?.id && (t.scope ?? "employee") === "employee");
  useEffect(() => {
    if (!resumeId || current.current.pending) return;
    if (resumeId === current.current.employeeId) {
      onResumeHandled();
      return;
    }
    const employee = data.employees.find((e) => e.id === resumeId);
    if (!employee) return;
    const context = data.knowledge
      .filter((k) => k.departmentId === employee.departmentId && k.state === "draft")
      .at(-1);
    change({
      ...fresh(snapshot, employee.roleId),
      employeeId: employee.id,
      name: employee.name,
      departmentId: employee.departmentId,
      responsibility: employee.responsibility,
      values: valuesOf(context),
      knowledgeId: context?.id || "",
      step: context ? "review" : "context",
    });
    onResumeHandled();
  }, [resumeId, data.employees, data.knowledge]);
  useEffect(() => setConsent(false), [saved?.id, saved?.digest, saved?.version]);
  function accept(action: string, result: unknown) {
    const item = result as { id: string };
    if (!item?.id) throw new Error("The saved result could not be identified. Check saved state before continuing.");
    const next = { ...current.current, pending: undefined };
    if (action === "department.create") {
      next.departmentId = item.id;
      next.createdDepartmentId = item.id;
    }
    if (action === "employee.hire") {
      next.employeeId = item.id;
      next.step = "context";
    }
    if (action === "employee.update") next.step = "context";
    if (action === "knowledge.save") {
      next.knowledgeId = item.id;
      next.step = "review";
    }
    if (action === "knowledge.review") next.step = "ready";
    change(next);
  }
  async function save(action: string, body: Record<string, unknown>) {
    if (locked.current || current.current.pending) return false;
    locked.current = true;
    setBusy(true);
    setStatus(
      action === "department.create"
        ? "Creating department…"
        : action === "employee.hire"
          ? "Creating employee…"
          : action === "knowledge.review"
            ? "Approving shared context…"
            : "Saving…",
    );
    setError("");
    try {
      const result = await command(action, body, (requestId) =>
        change({ ...current.current, pending: { action, requestId } }),
      );
      accept(action, result);
      try {
        await refresh();
      } catch {
        setError("Saved. Refresh the workspace to load the saved record. Your draft is kept.");
      }
      return true;
    } catch (e) {
      if (!(e instanceof UnconfirmedCompanyOperation)) change({ ...current.current, pending: undefined });
      setError((e as Error).message);
      return false;
    } finally {
      locked.current = false;
      setBusy(false);
      setStatus("");
    }
  }
  async function reconcile() {
    if (locked.current || !draft.pending) return;
    locked.current = true;
    setBusy(true);
    setStatus("Checking saved state…");
    setError("");
    try {
      const pending = draft.pending,
        result = await api<unknown>("/company/operations/" + pending.requestId);
      accept(pending.action, result);
      try {
        if (sessionStorage.getItem("hirezero-company-operation") === pending.requestId)
          sessionStorage.removeItem("hirezero-company-operation");
      } catch {
        /* Receipt is confirmed even when browser storage is restricted. */
      }
      try {
        await refresh();
      } catch {
        setError("Saved. Refresh the workspace to load the saved record. Your draft is kept.");
      }
    } catch {
      setError(
        "This save is still unconfirmed. Your draft is kept and this action will not be repeated. Check saved state again after the connection recovers.",
      );
    } finally {
      locked.current = false;
      setBusy(false);
      setStatus("");
    }
  }
  function submit(action: () => void | Promise<void>) {
    return (e: FormEvent) => {
      e.preventDefault();
      void action();
    };
  }
  const disabled = busy || !!draft.pending;
  async function profile() {
    if (
      !draft.name.trim() ||
      !draft.responsibility.trim() ||
      !draft.departmentId ||
      (draft.departmentId === "new" && !draft.departmentName.trim())
    ) {
      setError("Enter a name, department and responsibility. Spaces alone are not enough.");
      return;
    }
    if (person) {
      await save("employee.update", {
        id: person.id,
        expectedVersion: person.version,
        name: draft.name.trim(),
        responsibility: draft.responsibility.trim(),
      });
      return;
    }
    if (draft.departmentId === "new") {
      if (!(await save("department.create", { name: draft.departmentName.trim() }))) return;
    }
    await save("employee.hire", {
      name: current.current.name.trim(),
      roleId: current.current.role,
      departmentId: current.current.departmentId,
      responsibility: current.current.responsibility.trim(),
    });
  }
  const unavailable = person?.state === "paused" || person?.state === "retired";
  const ready = person?.state === "active" && !!approved && draft.step !== "profile",
    profilePinned = !!person || !!draft.createdDepartmentId;
  useEffect(() => {
    const element = heading.current;
    if (!element) return;
    const management = element.closest(".co-management");
    if (management) management.scrollTop = 0;
    element.focus({ preventScroll: true });
  }, [draft.step, person?.id, ready, unavailable]);
  return (
    <section className={"co-onboarding" + (ready ? " done" : "")} aria-label="Guided hiring">
      {/* Finished (owner feedback, 2026-10-20): a clear "you're done" moment before what comes next. */}
      {ready && (
        <div className="co-hire-done-badge" aria-hidden="true">
          <CircleCheckBig size={30} />
        </div>
      )}
      <p className="co-eyebrow">{ready ? "Onboarding complete" : person ? "Continue onboarding" : "Guided hiring"}</p>
      <h2 ref={heading} tabIndex={-1}>
        {unavailable
          ? person.name + " is " + person.state
          : ready
            ? person.name + " is ready to work"
            : draft.step === "profile"
              ? data.employees.length
                ? "Hire your next employee"
                : "Hire your first employee"
              : draft.step === "review"
                ? "Review shared department context"
                : "Set up " + (department?.name || "department") + " context"}
      </h2>
      {draft.step === "profile" && (
        <p className="co-muted">Hiring creates a private chat. AI work and spending need separate approval.</p>
      )}
      {ready && (
        <p className="co-hire-done-lead">
          Welcome to the team. {person.name} joined <b>{department?.name}</b>, and everything they need to start is in
          place.
        </p>
      )}
      <ol className={"co-hire-steps" + (ready ? " done" : "")} aria-label="Hiring progress">
        {["1. Employee", "2. Shared context", "3. Private chat"].map((label, i) => (
          <li
            key={label}
            aria-current={(ready ? i === 2 : draft.step === "profile" ? i === 0 : i === 1) ? "step" : undefined}
          >
            {ready && <Check size={13} aria-hidden="true" />}
            {label}
          </li>
        ))}
      </ol>
      {!ready && <p className="co-muted">Department facts are shared. Notes and chats stay private.</p>}
      {!ready && (
        <details className="co-context-help">
          <summary>What is shared, and what stays private?</summary>
          <p>
            <b>Department context</b> contains approved facts and boundaries shared by employees in that department.
            Later hires reuse the same approved version.
          </p>
          <p>
            <b>Responsibility</b> describes this employee’s job. <b>Private notes and chats</b> belong to this employee;
            another hire does not inherit them. Company conversation holds owner notes and updates.
          </p>
          <p>
            Hiring grants no permission for AI replies, tools, coding, publishing or spending. Those actions need their
            own approvals.
          </p>
        </details>
      )}
      {busy && (
        <p className="co-hire-status" role="status" aria-live="polite">
          {status}
        </p>
      )}
      {error && (
        <div className="co-error" role="alert">
          <span>{error}</span>
          <button
            disabled={busy}
            onClick={() =>
              void refresh()
                .then(() => setError(""))
                .catch(() => setError("The saved view is still unavailable. Your draft is kept."))
            }
          >
            Refresh saved view
          </button>
        </div>
      )}
      {draft.pending && !busy && (
        <div className="co-notice" role="status">
          <p>A save needs confirmation. Your draft is kept; no action will be repeated automatically.</p>
          <button disabled={busy} onClick={() => void reconcile()}>
            Check saved state
          </button>
        </div>
      )}
      {draft.step === "profile" && (
        <form onSubmit={submit(profile)}>
          <fieldset disabled={disabled}>
            <legend>Editable starting points</legend>
            <label>
              Role
              <select
                value={draft.role}
                disabled={profilePinned}
                onChange={(e) =>
                  change({
                    ...fresh(snapshot, e.target.value),
                    departmentId: suggestedDepartment(snapshot, e.target.value),
                  })
                }
              >
                {data.roles.map((r) => (
                  <option key={r.id} value={r.id}>
                    {r.name}
                  </option>
                ))}
              </select>
            </label>
            <label>
              Name
              <input
                required
                maxLength={100}
                value={draft.name}
                onChange={(e) => change({ ...draft, name: e.target.value })}
              />
            </label>
            <label>
              Department
              <select
                value={draft.departmentId}
                disabled={profilePinned}
                onChange={(e) => change({ ...draft, departmentId: e.target.value, values: {}, knowledgeId: "" })}
              >
                <option value="">Choose a department</option>
                <option value="new">Create a department</option>
                {data.departments.map((d) => (
                  <option key={d.id} value={d.id}>
                    {d.name}
                  </option>
                ))}
              </select>
            </label>
            {draft.departmentId === "new" && (
              <label>
                New department name
                <input
                  required
                  maxLength={100}
                  value={draft.departmentName}
                  onChange={(e) => change({ ...draft, departmentName: e.target.value })}
                />
              </label>
            )}
            <label>
              Responsibility
              <textarea
                required
                maxLength={1000}
                value={draft.responsibility}
                onChange={(e) => change({ ...draft, responsibility: e.target.value })}
              />
            </label>
          </fieldset>
          {draft.createdDepartmentId && !person && (
            <p>
              The department was saved. This attempt keeps that department and role while you finish the employee; your
              name and responsibility remain editable.
            </p>
          )}
          {department?.knowledge && (
            <p>This employee will reuse the department’s approved context. You will not need to enter it again.</p>
          )}
          <button className="co-primary" disabled={disabled} type="submit">
            {busy
              ? person
                ? "Saving employee…"
                : "Creating employee…"
              : person
                ? "Save employee & continue"
                : "Create employee & continue"}
          </button>
        </form>
      )}
      {person && !unavailable && draft.step !== "profile" && !ready && draft.step !== "review" && (
        <>
          {/* Fill from sources (docs/adr/0041): opened from the intro below, it only fills the form; nothing is
              saved by it. Closed, it takes no room, so the form stays in view on small screens. */}
          {sourcesOpen && (
            <ContextFromSources
              snapshot={snapshot}
              employeeId={person.id}
              employeeName={person.name}
              fields={fields}
              refresh={refresh}
              onFill={(values) => change({ ...draft, values: { ...draft.values, ...values } })}
            />
          )}
          <form
            onSubmit={submit(async () => {
              if (fields.some((field) => !draft.values[field]?.trim())) {
                setError("Complete every context field. If a fact is unknown, say so explicitly.");
                return;
              }
              await save("knowledge.save", {
                departmentId: person.departmentId,
                body: JSON.stringify(Object.fromEntries(fields.map((field) => [field, draft.values[field].trim()]))),
                ...(saved?.state === "draft" ? { id: saved.id, expectedVersion: saved.version } : {}),
              });
            })}
          >
            <p>
              Shared with <b>every employee in {department?.name}</b>. Enter verified facts or mark unknowns. You will
              review before approving.
            </p>
            <fieldset disabled={disabled}>
              <legend>
                Shared department context
                {!sourcesOpen && (
                  <>
                    {" · "}
                    <button type="button" className="co-link" onClick={() => setSourcesOpen(true)}>
                      Fill from sources
                    </button>
                  </>
                )}
              </legend>
              {fields.map((field) => (
                <label key={field}>
                  {humanize(field)}
                  {defaults?.help[field] && <small className="co-muted">{defaults.help[field]}</small>}
                  <textarea
                    required
                    maxLength={1500}
                    value={draft.values[field] || ""}
                    placeholder="Enter verified facts, or mark what is unknown."
                    onChange={(e) => change({ ...draft, values: { ...draft.values, [field]: e.target.value } })}
                  />
                </label>
              ))}
              {defaults && (
                <details>
                  <summary>Suggested boundaries for your review</summary>
                  <p>{defaults.boundaries}</p>
                  <button
                    type="button"
                    onClick={() => change({ ...draft, values: { ...draft.values, constraints: defaults.boundaries } })}
                  >
                    Use suggested boundaries
                  </button>
                </details>
              )}
            </fieldset>
            <div className="co-actions">
              <button type="button" disabled={disabled} onClick={() => change({ ...draft, step: "profile" })}>
                Back to employee
              </button>
              <button className="co-primary" disabled={disabled} type="submit">
                {busy ? "Saving context…" : "Save context for review"}
              </button>
            </div>
          </form>
        </>
      )}
      {person && !unavailable && draft.step === "review" && !ready && (
        <div>
          {saved?.state === "draft" ? (
            <>
              <p>
                Review the saved text below. Approval shares exactly this version with {department?.name} and activates
                its onboarding employees.
              </p>
              <Context knowledge={saved} />
              <label className="co-check">
                <input
                  type="checkbox"
                  checked={consent}
                  disabled={disabled}
                  onChange={(e) => setConsent(e.target.checked)}
                />
                I reviewed these facts, unknowns and boundaries for this department.
              </label>
              <div className="co-actions">
                <button
                  disabled={disabled}
                  onClick={() => change({ ...draft, values: valuesOf(saved), step: "context" })}
                >
                  Back to edit context
                </button>
                <button
                  className="co-primary"
                  disabled={disabled || !consent}
                  onClick={() =>
                    void save("knowledge.review", {
                      id: saved.id,
                      expectedVersion: saved.version,
                      digest: saved.digest,
                      decision: "approve",
                    })
                  }
                >
                  {busy ? "Approving…" : "Approve shared context"}
                </button>
              </div>
              <details>
                <summary>Saved version details</summary>
                <small>
                  Version {saved.version} · {saved.digest}
                </small>
              </details>
            </>
          ) : (
            <p>
              The saved context is unavailable or changed. Open Company and select this department to inspect its saved
              context.
            </p>
          )}
        </div>
      )}
      {unavailable && (
        <div>
          <p>
            This employee is {person.state}. Open their conversation from the employee list to inspect history and
            employee controls.
          </p>
          <button
            disabled={disabled}
            onClick={() => change({ ...fresh(snapshot, draft.role), departmentId: department?.id || "new" })}
          >
            Hire another employee
          </button>
        </div>
      )}
      {ready && (
        <div className="co-hire-done">
          <ul className="co-hire-done-list" aria-label="What is done">
            <li>
              <Check size={15} aria-hidden="true" />
              <span>
                Hired as {data.roles.find((r) => r.id === person.roleId)?.name ?? "an employee"}:{" "}
                <span className="co-muted">{person.responsibility}</span>
              </span>
            </li>
            <li>
              <Check size={15} aria-hidden="true" />
              <span>
                {data.employees.filter((e) => e.departmentId === department?.id).length > 1
                  ? "Uses " + (department?.name ?? "the department") + "’s approved context, unchanged"
                  : "Shared context approved for " + (department?.name ?? "the department")}{" "}
                <span className="co-muted">(version {approved.version})</span>
              </span>
            </li>
            <li>
              <Check size={15} aria-hidden="true" />
              <span>Private chat ready</span>
            </li>
          </ul>
          <details>
            <summary>View approved shared context</summary>
            <Context knowledge={approved} />
            <small>Approved version {approved.version}</small>
          </details>
          <p className="co-hire-done-next">
            <b>Next: say hello.</b> Give {person.name} a first task or notes in their private chat. Each AI reply or
            piece of work still waits for your approval.
          </p>
          <div className="co-actions">
            <button
              className="co-primary"
              disabled={!thread || disabled}
              onClick={() => thread && open(person.id, thread.id)}
            >
              Open {person.name}’s private chat
            </button>
            <button
              disabled={disabled}
              onClick={() => change({ ...fresh(snapshot, draft.role), departmentId: department?.id || "" })}
            >
              Hire another employee
            </button>
          </div>
        </div>
      )}
      {!ready && (
        <p className="co-muted co-hire-resume">
          You can leave and return to People. This tab keeps unfinished form text through reloads. Saved employees and
          context remain in Company; leaving does not delete them or authorize work.
        </p>
      )}
    </section>
  );
}
function Context({ knowledge }: { knowledge: Knowledge }) {
  const values = valuesOf(knowledge);
  return (
    <dl className="co-packet-fields co-hire-context">
      {Object.entries(values).map(([label, value]) => (
        <div key={label}>
          <dt>{label}</dt>
          <dd>{value}</dd>
        </div>
      ))}
    </dl>
  );
}
