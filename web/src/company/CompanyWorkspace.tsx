import { useEffect, useRef, useState, type FormEvent } from "react";
import { api } from "../lib/api";
import { BrandMark } from "../components/BrandMark";
import { panels } from "../roles/company";
import { command, type Snapshot, type Employee, type ThreadDetail, type Event, type TaskSpec } from "./api";
import { CompanyConversation, type ConversationDraft } from "./CompanyConversation";
import { MessageSquareText, Users, Building2, ClipboardList, MessagesSquare } from "lucide-react";
import { HireOnboarding } from "./HireOnboarding";
import { Integrations } from "./Integrations";
import { Today } from "./Today";
import { Budgets } from "./Budgets";
import { Members } from "./Members";
import { Companies } from "./Companies";
import { Audit } from "./Audit";
import "./company.css";
import { employeeState, roleName } from "./labels";
type ConversationRoute = { tab: string; selected: string; thread: string };
function readRoute(): ConversationRoute {
  const query = new URLSearchParams(location.search),
    view = query.get("view") || "company";
  return {
    tab: ["company", "employee", "departments", "employees", "plans", "meetings"].includes(view) ? view : "company",
    selected: query.get("employee") || "",
    thread: query.get("thread") || "",
  };
}
function Submit({ children = "Save", busy = false }: { children?: string; busy?: boolean }) {
  return (
    <button className="co-primary" disabled={busy} type="submit">
      {children}
    </button>
  );
}
export function CompanyWorkspace({ memberRole }: { memberRole?: string } = {}) {
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null),
    [events, setEvents] = useState<Event[]>([]),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  const [route, setRoute] = useState<ConversationRoute>(readRoute),
    [detail, setDetail] = useState<ThreadDetail | null>(null),
    [showConversations, setShowConversations] = useState(false);
  const { tab, selected, thread } = route;
  const [drafts, setDrafts] = useState(new Map<string, ConversationDraft>());
  function updateDraft(scope: string, update: (current: ConversationDraft | undefined) => ConversationDraft) {
    setDrafts((previous) => {
      const next = new Map(previous);
      next.set(scope, update(previous.get(scope)));
      return next;
    });
  }
  const routeRef = useRef(route),
    threadRequest = useRef(0),
    snapshotRequest = useRef(0);
  routeRef.current = route;
  function navigate(next: ConversationRoute) {
    routeRef.current = next;
    current.current = { selected: next.selected, thread: next.thread };
    threadRequest.current++;
    setDetail(null);
    setRoute(next);
    setShowConversations(false);
    const query = new URLSearchParams();
    query.set("view", next.tab);
    if (next.tab === "employee") {
      query.set("employee", next.selected);
      query.set("thread", next.thread);
    }
    history.pushState(null, "", "?" + query);
  }
  function setTab(tab: string) {
    navigate({ ...routeRef.current, tab });
  }
  function openConversation(employeeId: string, threadId: string) {
    navigate({ tab: "employee", selected: employeeId, thread: threadId });
  }
  useEffect(() => {
    const pop = () => {
      const next = readRoute();
      routeRef.current = next;
      current.current = { selected: next.selected, thread: next.thread };
      threadRequest.current++;
      setDetail(null);
      setRoute(next);
      setShowConversations(false);
    };
    window.addEventListener("popstate", pop);
    return () => window.removeEventListener("popstate", pop);
  }, []);
  const [resumeHire, setResumeHire] = useState("");
  const [evidence, setEvidence] = useState<string[]>([]);
  const [newName, setNewName] = useState(""),
    [role, setRole] = useState(""),
    [department, setDepartment] = useState(""),
    [responsibility, setResponsibility] = useState("");
  const [departmentName, setDepartmentName] = useState(""),
    [knowledge, setKnowledge] = useState<Record<string, string>>({});
  const [planText, setPlanText] = useState(""),
    [planTasks, setPlanTasks] = useState<TaskSpec[]>([]),
    [meetingTitle, setMeetingTitle] = useState(""),
    [agenda, setAgenda] = useState(""),
    [participants, setParticipants] = useState<string[]>([]),
    [chair, setChair] = useState(""),
    [outcome, setOutcome] = useState<Record<string, string>>({});
  const current = useRef({ selected, thread });
  current.current = { selected, thread };
  const locked = useRef(false),
    alive = useRef(true),
    feedCursor = useRef(0);
  async function refresh() {
    const request = ++snapshotRequest.current;
    const next = await api<Snapshot>("/company");
    if (!alive.current || request !== snapshotRequest.current) return;
    setSnapshot(next);
    let cursor = feedCursor.current;
    const found: Event[] = [];
    while (cursor < next.data.cursor) {
      const page = await api<Event[]>("/company/feed?after=" + cursor + "&limit=100");
      if (!page.length) break;
      found.push(...page);
      cursor = page[page.length - 1].sequence;
    }
    if (alive.current) {
      feedCursor.current = Math.max(feedCursor.current, cursor);
      setEvents((previous) =>
        Array.from(new Map([...previous, ...found].map((event) => [event.sequence, event])).values())
          .sort((a, b) => a.sequence - b.sequence)
          .slice(-300),
      );
    }
    const target = current.current;
    if (routeRef.current.tab === "employee" && target.selected && target.thread)
      await refreshThread(target.selected, target.thread);
  }
  async function refreshThread(employee: string, id: string) {
    const request = ++threadRequest.current;
    const next = await api<ThreadDetail>("/company/employees/" + employee + "/threads/" + id);
    if (
      alive.current &&
      request === threadRequest.current &&
      current.current.selected === employee &&
      current.current.thread === id
    )
      setDetail(next);
  }
  useEffect(() => {
    alive.current = true;
    void refresh().catch((e) => setError(e.message));
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") void refresh().catch((e) => setError(e.message));
    }, 3000);
    return () => {
      alive.current = false;
      clearInterval(timer);
    };
  }, []);
  useEffect(() => {
    setDetail(null);
    if (selected && thread && tab === "employee")
      void refreshThread(selected, thread).catch((e) => setError(e.message));
  }, [selected, thread, tab]);
  async function run(action: string, fields: Record<string, unknown> = {}) {
    if (locked.current) return;
    locked.current = true;
    setBusy(true);
    setError("");
    try {
      const result = await command(action, fields);
      try {
        await refresh();
      } catch (e) {
        setError("Saved. The view could not refresh: " + (e as Error).message);
      }
      return result;
    } catch (e) {
      setError((e as Error).message);
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }
  function form(fn: () => void | Promise<void>) {
    return (e: FormEvent) => {
      e.preventDefault();
      void fn();
    };
  }
  if (!snapshot)
    return (
      <main className="co-loading">
        <BrandMark className="co-brand" />
        <p>{error || "Opening company workspace…"}</p>
        <button onClick={() => void refresh().catch((e) => setError(e.message))}>Retry</button>
      </main>
    );
  const d = snapshot.data,
    employee = d.employees.find((e) => e.id === selected),
    dept = d.departments.find((x) => x.id === department),
    guide = d.employees.find((e) => e.id === dept?.guide),
    deptRole = d.roles.find((r) => r.id === guide?.roleId);
  const approved = d.artifacts.filter((a) => a.state === "approved"),
    ceos = d.employees.filter(
      (e) => e.state === "active" && d.roles.find((r) => r.id === e.roleId)?.skills.includes("company-plan"),
    );
  function chooseEmployee(e: Employee) {
    openConversation(
      e.id,
      d.threads.find((t) => t.employeeId === e.id && (t.scope ?? "employee") === "employee")?.id || "",
    );
  }
  const chooseEvidence = (
    <fieldset>
      <legend>Selected approved evidence</legend>
      {approved.length === 0 ? (
        <p className="co-muted">No approved artifacts yet.</p>
      ) : (
        approved.map((a) => (
          <label className="co-check" key={a.id}>
            <input
              type="checkbox"
              checked={evidence.includes(a.id)}
              onChange={(e) =>
                setEvidence(e.target.checked ? [...evidence, a.id] : evidence.filter((id) => id !== a.id))
              }
            />
            {d.employees.find((e) => e.id === a.employeeId)?.name}: {a.body.slice(0, 80)}
          </label>
        ))
      )}
    </fieldset>
  );
  const chat = tab === "company" || tab === "employee";
  const initiatingRoute = route;
  function openFromConversation(employeeId: string, threadId: string) {
    if (routeRef.current === initiatingRoute) openConversation(employeeId, threadId);
  }
  const navigation = [
    ["company", "Chat", MessageSquareText],
    ["employees", "People", Users],
    ["departments", "Company", Building2],
    ["plans", "Plans", ClipboardList],
    ["meetings", "Board", MessagesSquare],
  ] as const;
  return (
    <div className="fe-app dev-app co-app co-workspace">
      <aside className="fe-rail co-navigation" aria-label="Navigation">
        <BrandMark className="fe-rail-mark" />
        <nav className="fe-rail-nav">
          {navigation.map(([view, label, Icon]) => (
            <button
              className="fe-rail-button"
              aria-label={label}
              title={label}
              aria-current={tab === view ? "page" : undefined}
              onClick={() => setTab(view)}
              key={view}
            >
              <Icon size={21} />
              <span className="fe-rail-caption">{label}</span>
            </button>
          ))}
        </nav>
        <small className="co-owner-label">Owner</small>
      </aside>
      <aside className={"co-conversations" + (showConversations ? " is-open" : "")} aria-label="Conversations">
        <h2>HireZero</h2>
        <button
          className="co-list"
          aria-current={tab === "company" ? "page" : undefined}
          onClick={() => setTab("company")}
        >
          <b>Company conversation</b>
          <small>Owner notes and company updates</small>
        </button>
        <h3>Employees</h3>
        {d.employees.map((person) => (
          <div key={person.id}>
            <button
              className="co-list"
              aria-current={tab === "employee" && person.id === selected ? "page" : undefined}
              onClick={() => chooseEmployee(person)}
            >
              <b>{person.name}</b>
              <small>
                {roleName(snapshot.data.roles, person.roleId)} · {employeeState(person.state)}
              </small>
            </button>
            {tab === "employee" && person.id === selected && (
              <div className="co-thread-list">
                {d.threads
                  .filter((t) => t.employeeId === person.id && (t.scope ?? "employee") === "employee")
                  .map((t) => (
                    <button key={t.id} aria-pressed={thread === t.id} onClick={() => openConversation(person.id, t.id)}>
                      {t.title}
                    </button>
                  ))}
              </div>
            )}
          </div>
        ))}
        {!d.employees.length && <p>Hire and onboard an employee to open a private conversation.</p>}
        <button onClick={() => setTab("employees")}>
          {d.employees.length ? "Hire or manage employees" : "Hire your first employee"}
        </button>
        <p className="co-muted">
          You have final authority. CEO plans and Board decisions remain subject to your review.
        </p>
        <Companies role={memberRole} version={d.cursor ?? 0} />
        <Today version={d.cursor ?? 0} busy={busy} run={run} />
        <Budgets budgets={d.budgets} departments={d.departments} employees={d.employees} busy={busy} run={run} />
        <Members role={memberRole} version={d.cursor ?? 0} busy={busy} run={run} />
        <Audit role={memberRole} version={d.cursor ?? 0} />
        <Integrations integrations={snapshot.capabilities.integrations ?? []} refresh={refresh} />
      </aside>
      <main className={"co-main fe-main" + (chat ? " co-chat-main" : "")}>
        <header className="fe-topbar">
          <button
            className="co-conversation-toggle"
            aria-expanded={showConversations}
            onClick={() => setShowConversations(!showConversations)}
          >
            Conversations
          </button>
          <div>
            <h1>
              {tab === "company"
                ? "Company conversation"
                : tab === "employee"
                  ? employee?.name || "Conversation unavailable"
                  : tab === "meetings"
                    ? "Board meetings"
                    : tab[0].toUpperCase() + tab.slice(1)}
            </h1>
            {chat && (
              <small>
                {tab === "company"
                  ? "Company context · owner control"
                  : employee
                    ? employee.roleId +
                      " · " +
                      d.departments.find((x) => x.id === employee.departmentId)?.name +
                      " · private conversation"
                    : ""}
              </small>
            )}
          </div>
          <span className="co-status">
            {snapshot.capabilities.modelExecution
              ? "Synthetic test provider"
              : snapshot.capabilities.measuredOneJob
                ? "Manual jobs connected"
                : "Live execution unavailable"}
          </span>
        </header>
        {error && (
          <div className="co-error" role="alert">
            {error}
            <button onClick={() => setError("")}>Dismiss</button>
          </div>
        )}
        <div className={chat ? "co-conversation-body" : "co-management"}>
          {tab === "company" && !d.employees.length && (
            <div className="co-first-hire">
              <h2>Start with your first employee</h2>
              <p>
                Choose an editable role and responsibility, review your department’s shared context, then open a private
                chat.
              </p>
              <button className="co-primary" onClick={() => setTab("employees")}>
                Hire your first employee
              </button>
            </div>
          )}
          {(tab === "company" || tab === "employee") && (
            <CompanyConversation
              key={tab === "company" ? "company" : selected + ":" + thread}
              snapshot={snapshot}
              company={tab === "company"}
              events={events}
              employee={tab === "employee" ? employee : undefined}
              thread={thread}
              detail={detail}
              busy={busy}
              drafts={drafts}
              updateDraft={updateDraft}
              run={run}
              refresh={refresh}
              open={openFromConversation}
              createThread={async () => {
                if (!employee) return;
                const before = routeRef.current;
                const result = (await run("thread.create", {
                  employeeId: employee.id,
                  name: "Private conversation",
                })) as { id: string } | undefined;
                if (result && routeRef.current === before) openConversation(employee.id, result.id);
              }}
            />
          )}
          {tab === "departments" && (
            <>
              <p className="co-muted">
                Department context is shared by that department’s employees. Employee responsibilities, private notes
                and chats stay with each employee. Use People for guided hiring; the controls below manage saved
                departments and exact context versions.
              </p>
              <div className="co-columns">
                <section>
                  <h2>Departments</h2>
                  {d.departments.map((x) => (
                    <button
                      className="co-list"
                      key={x.id}
                      onClick={() => {
                        setDepartment(x.id);
                        setKnowledge({});
                      }}
                    >
                      <b>{x.name}</b>
                      <span>{x.knowledge ? "Approved knowledge" : "Onboarding needed"}</span>
                    </button>
                  ))}
                  <form
                    onSubmit={form(async () => {
                      await run("department.create", { name: departmentName });
                      setDepartmentName("");
                    })}
                  >
                    <label>
                      New department
                      <input required value={departmentName} onChange={(e) => setDepartmentName(e.target.value)} />
                    </label>
                    <Submit busy={busy}>Create department</Submit>
                  </form>
                </section>
                <section>
                  <h2>{dept?.name || "Select a department"}</h2>
                  {dept && !guide && <p>Hire its first employee to begin department onboarding.</p>}
                  {dept && deptRole && (
                    <>
                      <p className="co-muted">
                        {guide?.name} guides onboarding. You approve the knowledge inherited by later hires.
                      </p>
                      <form
                        onSubmit={form(async () => {
                          await run("knowledge.save", { departmentId: dept.id, body: JSON.stringify(knowledge) });
                        })}
                      >
                        {deptRole.onboardingFields.map((field) => (
                          <label key={field}>
                            {field}
                            <textarea
                              required
                              value={knowledge[field] || ""}
                              onChange={(e) => setKnowledge({ ...knowledge, [field]: e.target.value })}
                            />
                          </label>
                        ))}
                        <Submit busy={busy}>Save knowledge draft</Submit>
                      </form>
                      {d.knowledge
                        .filter((k) => k.departmentId === dept.id)
                        .map((k) => (
                          <article className="co-card" key={k.id}>
                            <b>
                              Knowledge · {k.state} · version {k.version}
                            </b>
                            <pre>{k.body}</pre>
                            {k.state === "draft" && (
                              <button
                                onClick={() =>
                                  void run("knowledge.review", {
                                    id: k.id,
                                    expectedVersion: k.version,
                                    digest: k.digest,
                                    decision: "approve",
                                  })
                                }
                              >
                                Approve this version
                              </button>
                            )}
                            {k.state === "approved" && (
                              <button
                                onClick={() =>
                                  void run("knowledge.review", {
                                    id: k.id,
                                    expectedVersion: k.version,
                                    digest: k.digest,
                                    decision: "revoke",
                                  })
                                }
                              >
                                Revoke knowledge
                              </button>
                            )}
                          </article>
                        ))}
                    </>
                  )}
                </section>
              </div>
            </>
          )}
          {tab === "employees" && (
            <>
              <HireOnboarding
                snapshot={snapshot}
                refresh={refresh}
                open={openConversation}
                resumeId={resumeHire}
                onResumeHandled={() => setResumeHire("")}
              />
              <section>
                <h2>Your employees</h2>
                {d.employees.length === 0 ? (
                  <p>No employees yet. Start with the guided form above.</p>
                ) : (
                  d.employees.map((e) => (
                    <div className="co-hire-person" key={e.id}>
                      <button className="co-list" onClick={() => chooseEmployee(e)}>
                        <b>{e.name}</b>
                        <span>
                          {roleName(snapshot.data.roles, e.roleId)} · {employeeState(e.state)}
                        </span>
                      </button>
                      {e.state === "onboarding" && (
                        <button onClick={() => setResumeHire(e.id)}>Continue onboarding {e.name}</button>
                      )}
                    </div>
                  ))
                )}
                <details>
                  <summary>Advanced: hire without the guide</summary>
                  <form
                    onSubmit={form(async () => {
                      await run("employee.hire", {
                        name: newName,
                        roleId: role || d.roles[0]?.id,
                        departmentId: department,
                        responsibility,
                      });
                      setNewName("");
                      setResponsibility("");
                    })}
                  >
                    <label>
                      Name
                      <input required value={newName} onChange={(e) => setNewName(e.target.value)} />
                    </label>
                    <label>
                      Role
                      <select value={role || d.roles[0]?.id} onChange={(e) => setRole(e.target.value)}>
                        {d.roles.map((r) => (
                          <option value={r.id} key={r.id}>
                            {r.name}
                          </option>
                        ))}
                      </select>
                    </label>
                    <label>
                      Department
                      <select required value={department} onChange={(e) => setDepartment(e.target.value)}>
                        <option value="">Choose department</option>
                        {d.departments.map((x) => (
                          <option value={x.id} key={x.id}>
                            {x.name}
                          </option>
                        ))}
                      </select>
                    </label>
                    <label>
                      Responsibility
                      <textarea
                        required
                        value={responsibility}
                        onChange={(e) => setResponsibility(e.target.value)}
                        placeholder="What should this employee focus on?"
                      />
                    </label>
                    <Submit busy={busy}>Hire employee</Submit>
                  </form>
                </details>
              </section>
            </>
          )}
          {tab === "plans" && (
            <div className="co-columns">
              <section>
                <h2>CEO delegation</h2>
                {panels(d.roles, "plan-guidance").map(({ roleId, Panel }) => (
                  <Panel key={roleId} />
                ))}
                {ceos.length === 0 ? (
                  <p>Hire and onboard a CEO to propose company work.</p>
                ) : (
                  <form
                    onSubmit={form(async () => {
                      await run("plan.create", {
                        employeeId: ceos[0].id,
                        body: planText,
                        tasks: planTasks,
                        budget: Math.max(5000, planTasks.length * 5000),
                      });
                      setPlanTasks([]);
                      setPlanText("");
                    })}
                  >
                    <label>
                      Objective
                      <textarea required value={planText} onChange={(e) => setPlanText(e.target.value)} />
                    </label>
                    {planTasks.map((task, index) => (
                      <fieldset key={index}>
                        <legend>Task {index + 1}</legend>
                        <label>
                          Assignee
                          <select
                            required
                            value={task.employeeId}
                            onChange={(e) => {
                              const tasks = [...planTasks],
                                person = d.employees.find((x) => x.id === e.target.value);
                              tasks[index] = {
                                ...task,
                                employeeId: e.target.value,
                                threadId:
                                  d.threads.find(
                                    (t) => t.employeeId === e.target.value && (t.scope ?? "employee") === "employee",
                                  )?.id || "",
                                skill: "chat",
                              };
                              setPlanTasks(tasks);
                            }}
                          >
                            <option value="">Choose employee</option>
                            {d.employees
                              .filter((e) => e.state === "active" && e.id !== ceos[0].id)
                              .map((e) => (
                                <option key={e.id} value={e.id}>
                                  {e.name}
                                </option>
                              ))}
                          </select>
                        </label>
                        <label>
                          Task and acceptance criteria
                          <textarea
                            required
                            value={task.input}
                            onChange={(e) =>
                              setPlanTasks(planTasks.map((t, i) => (i === index ? { ...t, input: e.target.value } : t)))
                            }
                          />
                        </label>
                        {index > 0 && (
                          <label className="co-check">
                            <input
                              type="checkbox"
                              checked={task.dependsOn.includes(index - 1)}
                              onChange={(e) =>
                                setPlanTasks(
                                  planTasks.map((t, i) =>
                                    i === index ? { ...t, dependsOn: e.target.checked ? [index - 1] : [] } : t,
                                  ),
                                )
                              }
                            />
                            Wait for previous task
                          </label>
                        )}
                      </fieldset>
                    ))}
                    <button
                      type="button"
                      disabled={planTasks.length >= 4}
                      onClick={() =>
                        setPlanTasks([
                          ...planTasks,
                          { employeeId: "", threadId: "", skill: "chat", input: "", evidence: [], dependsOn: [] },
                        ])
                      }
                    >
                      Add task
                    </button>
                    <Submit busy={busy}>Propose plan</Submit>
                  </form>
                )}
              </section>
              <section>
                <h2>Plans for review</h2>
                {d.plans.map((p) => (
                  <article key={p.id} className="co-card">
                    <b>{p.objective}</b>
                    <p>
                      {p.state} · shared ceiling {p.budget.toLocaleString()} tokens
                    </p>
                    {p.tasks.map((t, i) => (
                      <p key={i}>
                        {i + 1}. {d.employees.find((e) => e.id === t.employeeId)?.name}: {t.input}
                      </p>
                    ))}
                    {p.state === "proposed" && (
                      <button
                        onClick={() =>
                          void run("plan.approve", { id: p.id, expectedVersion: p.version, digest: p.digest })
                        }
                      >
                        Approve exact plan
                      </button>
                    )}
                    {!["completed", "canceled"].includes(p.state) && (
                      <button onClick={() => void run("plan.cancel", { id: p.id, expectedVersion: p.version })}>
                        Cancel plan and pending work
                      </button>
                    )}
                  </article>
                ))}
              </section>
            </div>
          )}
          {tab === "meetings" && (
            <div className="co-columns">
              <section>
                <h2>New Board Meeting</h2>
                <p className="co-muted">
                  Only selected approved evidence and the shared meeting transcript reach participants.
                </p>
                <form
                  onSubmit={form(async () => {
                    await run("meeting.create", {
                      name: meetingTitle,
                      body: agenda,
                      participants,
                      chairId: chair,
                      evidence,
                      budget: 50000,
                    });
                    setMeetingTitle("");
                    setAgenda("");
                  })}
                >
                  <label>
                    Title
                    <input required value={meetingTitle} onChange={(e) => setMeetingTitle(e.target.value)} />
                  </label>
                  <label>
                    Agenda
                    <textarea required value={agenda} onChange={(e) => setAgenda(e.target.value)} />
                  </label>
                  <fieldset>
                    <legend>Participants</legend>
                    {d.employees
                      .filter((e) => e.state === "active")
                      .map((e) => (
                        <label className="co-check" key={e.id}>
                          <input
                            type="checkbox"
                            checked={participants.includes(e.id)}
                            onChange={(event) =>
                              setParticipants(
                                event.target.checked
                                  ? [...participants, e.id]
                                  : participants.filter((id) => id !== e.id),
                              )
                            }
                          />
                          {e.name}
                        </label>
                      ))}
                  </fieldset>
                  <label>
                    Chair
                    <select required value={chair} onChange={(e) => setChair(e.target.value)}>
                      <option value="">Choose CEO chair</option>
                      {ceos
                        .filter((e) => participants.includes(e.id))
                        .map((e) => (
                          <option key={e.id} value={e.id}>
                            {e.name}
                          </option>
                        ))}
                    </select>
                  </label>
                  {chooseEvidence}
                  <Submit busy={busy}>Prepare meeting</Submit>
                </form>
              </section>
              <section>
                <h2>Meeting record</h2>
                {d.meetings.map((m) => (
                  <article className="co-card" key={m.id}>
                    <h3>{m.title}</h3>
                    <p>{m.agenda}</p>
                    <p>
                      {m.state} · {m.turns.length}/{m.participants.length * 2} turns
                    </p>
                    {m.turns.map((t) => (
                      <details key={t.taskId}>
                        <summary>{d.employees.find((e) => e.id === t.employeeId)?.name}</summary>
                        <pre>{t.body}</pre>
                      </details>
                    ))}
                    <div className="co-actions">
                      {m.state === "draft" && (
                        <button onClick={() => void run("meeting.start", { id: m.id, expectedVersion: m.version })}>
                          Approve discussion and start
                        </button>
                      )}
                      {m.state === "in_progress" && (
                        <button onClick={() => void run("meeting.pause", { id: m.id, expectedVersion: m.version })}>
                          Pause
                        </button>
                      )}
                      {m.state === "paused" && (
                        <button onClick={() => void run("meeting.next", { id: m.id, expectedVersion: m.version })}>
                          Resume
                        </button>
                      )}
                      {!["closed", "canceled"].includes(m.state) && (
                        <button onClick={() => void run("meeting.cancel", { id: m.id, expectedVersion: m.version })}>
                          Cancel meeting
                        </button>
                      )}
                    </div>
                    {m.state === "outcome_proposed" && (
                      <form
                        onSubmit={form(async () => {
                          await run("meeting.outcome", { id: m.id, expectedVersion: m.version, body: outcome[m.id] });
                        })}
                      >
                        <label>
                          Decisions, dissent and proposed follow-up
                          <textarea
                            required
                            value={outcome[m.id] || ""}
                            onChange={(e) => setOutcome({ ...outcome, [m.id]: e.target.value })}
                          />
                        </label>
                        <Submit busy={busy}>Propose minutes</Submit>
                      </form>
                    )}
                    {m.outcome && <pre>{m.outcome}</pre>}
                    {m.state === "awaiting_owner" && (
                      <button
                        onClick={() =>
                          void run("meeting.close", { id: m.id, expectedVersion: m.version, digest: m.outcomeDigest })
                        }
                      >
                        Accept minutes and close
                      </button>
                    )}
                  </article>
                ))}
              </section>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}
