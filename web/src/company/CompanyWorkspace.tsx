import { useEffect, useRef, useState, type CSSProperties, type FormEvent } from "react";
import { api, applyTheme, preference, savePreference, type ThemeChoice } from "../lib/api";
import { ShortcutHelp, useShortcuts } from "./Shortcuts";
import { BrandMark } from "../components/BrandMark";
import { panels } from "../roles/company";
import { command, type Snapshot, type Employee, type ThreadDetail, type Event, type TaskSpec, type Work } from "./api";
import { Cockpit, useNeeds, workGroups, type Need } from "./Cockpit";
import { WorkBoard, WorkWindow } from "./Work";
import { CompanyConversation, type ConversationDraft } from "./CompanyConversation";
import {
  MessageSquareText,
  Users,
  Building2,
  ClipboardList,
  MessagesSquare,
  PanelRightOpen,
  Settings,
  UserPlus,
  LayoutDashboard,
  Monitor,
  Moon,
  Sun,
} from "lucide-react";
import { Home, initials } from "./Home";
import { HireOnboarding } from "./HireOnboarding";
import { Integrations } from "./Integrations";
import { Today } from "./Today";
import { Budgets } from "./Budgets";
import { Members } from "./Members";
import { Companies } from "./Companies";
import { ContextEditor } from "./ContextEditor";
import { Audit } from "./Audit";
import "./company.css";
import { employeeState, roleName } from "./labels";
// A conversation's mode (Chat or Work) and its open work are part of the address (docs/adr/0040-the-muse-layout.md).
type ConversationRoute = { tab: string; selected: string; thread: string; mode?: "chat" | "work"; work?: string };
function readRoute(): ConversationRoute {
  const query = new URLSearchParams(location.search),
    view = query.get("view") || "home";
  return {
    tab: ["home", "company", "employee", "departments", "employees", "plans", "meetings"].includes(view)
      ? view
      : "home",
    selected: query.get("employee") || "",
    thread: query.get("thread") || "",
    mode: query.get("mode") === "work" ? "work" : "chat",
    work: query.get("work") || "",
  };
}
function useWide(query: string) {
  const [wide, setWide] = useState(() => matchMedia(query).matches);
  useEffect(() => {
    const media = matchMedia(query),
      change = () => setWide(media.matches);
    media.addEventListener("change", change);
    return () => media.removeEventListener("change", change);
  }, [query]);
  return wide;
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
    const previous = routeRef.current;
    routeRef.current = next;
    current.current = { selected: next.selected, thread: next.thread };
    // Changing only the mode or the open work keeps the conversation that is already loaded.
    if (previous.tab !== next.tab || previous.selected !== next.selected || previous.thread !== next.thread) {
      threadRequest.current++;
      setDetail(null);
    }
    setRoute(next);
    setShowConversations(false);
    const query = new URLSearchParams();
    query.set("view", next.tab);
    if (next.tab === "employee") {
      query.set("employee", next.selected);
      query.set("thread", next.thread);
    }
    if (next.tab === "company" || next.tab === "employee") {
      if (next.mode === "work") query.set("mode", "work");
      if (next.work) query.set("work", next.work);
    }
    history.pushState(null, "", "?" + query);
  }
  function setTab(tab: string) {
    navigate({ ...routeRef.current, tab, work: "" });
  }
  // The Company hub (owner walkthrough, 2026-10-19): what needs you and the company's settings, one section at a time,
  // starting with Today. The section is kept in the address so a reload stays on it.
  const hubSections = [
    ["today", "Today"],
    ["departments", "Departments"],
    ["budgets", "Budgets"],
    ["members", "Members"],
    ["integrations", "Integrations"],
    ["audit", "Audit log"],
    ["companies", "Companies"],
  ] as const;
  const [section, setSection] = useState(() => {
    const value = new URLSearchParams(location.search).get("section");
    return hubSections.some(([key]) => key === value) ? (value as string) : "today";
  });
  function openHub(key: string) {
    setSection(key);
    if (routeRef.current.tab !== "departments") navigate({ ...routeRef.current, tab: "departments" });
    const query = new URLSearchParams(location.search);
    query.set("view", "departments");
    query.set("section", key);
    history.replaceState(null, "", "?" + query);
  }
  function openConversation(employeeId: string, threadId: string) {
    navigate({ tab: "employee", selected: employeeId, thread: threadId, mode: routeRef.current.mode, work: "" });
  }
  function setMode(mode: "chat" | "work") {
    navigate({ ...routeRef.current, mode });
  }
  function closeWork() {
    navigate({ ...routeRef.current, work: "" });
  }
  // The cockpit: docked on wide screens, resizable and hideable, otherwise a sheet (docs/adr/0040-the-muse-layout.md).
  const roomy = useWide("(min-width: 1280px)"),
    splitWide = useWide("(min-width: 1440px)");
  const [cockpitOpen, setCockpitOpen] = useState(() => preference("cockpit-open", true)),
    [cockpitWidth, setCockpitWidth] = useState(() => Math.max(240, Math.min(480, preference("cockpit-width", 320)))),
    [sheet, setSheet] = useState(false);
  const drag = useRef<{ x: number; width: number } | null>(null);
  function resizeCockpit(width: number) {
    const next = Math.max(240, Math.min(480, Math.round(width)));
    setCockpitWidth(next);
    savePreference("cockpit-width", next);
  }
  function showCockpit(open: boolean) {
    setCockpitOpen(open);
    savePreference("cockpit-open", open);
  }
  const docked = roomy && cockpitOpen;
  const needs = useNeeds(snapshot?.data.cursor ?? 0);
  // Theme: System follows the operating system, and changes with it (UX round 3).
  const [theme, setTheme] = useState<ThemeChoice>(() => preference<ThemeChoice>("theme", "system"));
  useEffect(() => {
    applyTheme(theme);
    savePreference("theme", theme);
    if (theme !== "system") return;
    const media = matchMedia("(prefers-color-scheme: dark)"),
      change = () => applyTheme("system");
    media.addEventListener("change", change);
    return () => media.removeEventListener("change", change);
  }, [theme]);
  const [help, setHelp] = useState(false);
  useShortcuts({
    go: (place) => (place === "settings" ? openHub("today") : setTab(place)),
    toggleMode: () => {
      const r = routeRef.current;
      if (r.tab === "company" || r.tab === "employee") setMode((r.mode ?? "chat") === "chat" ? "work" : "chat");
    },
    toggleCockpit: () => (roomy ? showCockpit(!cockpitOpen) : setSheet((open) => !open)),
    focusMessage: () => document.querySelector<HTMLTextAreaElement>(".fe-composer textarea")?.focus(),
    escape: () => {
      if (help) setHelp(false);
      else if (sheet) setSheet(false);
      else if (routeRef.current.work) closeWork();
      else return false;
      return true;
    },
    help: () => setHelp((open) => !open),
  });
  function openWork(task: Work) {
    const where = snapshot?.data.threads.find((t) => t.id === task.threadId);
    const company = !!where && (where.scope ?? "employee") !== "employee";
    navigate({
      tab: company ? "company" : "employee",
      selected: company ? "" : task.employeeId,
      thread: company ? "" : task.threadId,
      mode: routeRef.current.mode,
      work: task.id,
    });
    setSheet(false);
  }
  function openNeed(need: Need) {
    const task = need.taskId ? snapshot?.data.tasks.find((t) => t.id === need.taskId) : undefined;
    setSheet(false);
    if (task) openWork(task);
    else if (need.kind === "plan") setTab("plans");
    else if (need.kind === "meeting") setTab("meetings");
    else if ((need.kind === "onboarding" || need.kind === "knowledge") && need.employeeId) {
      // An unfinished hire, or the context draft that finishes it, resumes in the hiring guide (UX round 6).
      setResumeHire(need.employeeId);
      setTab("employees");
    } else openHub(need.kind === "knowledge" ? "departments" : "today");
  }
  useEffect(() => {
    const pop = () => {
      const next = readRoute(),
        previous = routeRef.current;
      routeRef.current = next;
      current.current = { selected: next.selected, thread: next.thread };
      if (previous.tab !== next.tab || previous.selected !== next.selected || previous.thread !== next.thread) {
        threadRequest.current++;
        setDetail(null);
      }
      setRoute(next);
      setShowConversations(false);
    };
    window.addEventListener("popstate", pop);
    return () => window.removeEventListener("popstate", pop);
  }, []);
  const [resumeHire, setResumeHire] = useState("");
  const [hireActive, setHireActive] = useState(false);
  const [evidence, setEvidence] = useState<string[]>([]);
  const [newName, setNewName] = useState(""),
    [role, setRole] = useState(""),
    [department, setDepartment] = useState(""),
    [responsibility, setResponsibility] = useState("");
  const [departmentName, setDepartmentName] = useState("");
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
  // The guide keeps one place in the page so a hire in progress is never remounted; the team moves around it.
  const teamCards = d.employees.length > 0 && (
    <section className="co-team" aria-label="Your team">
      <h2>Your team</h2>
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
                <small>
                  {roleName(snapshot.data.roles, e.roleId)} · {employeeState(e.state)}
                </small>
              </span>
            </header>
            <p>{e.responsibility}</p>
            <small className="co-muted">
              {d.departments.find((x) => x.id === e.departmentId)?.name ?? "No department"}
            </small>
            <div className="co-actions">
              {e.state === "onboarding" ? (
                <button className="co-primary" onClick={() => setResumeHire(e.id)}>
                  Continue onboarding {e.name}
                </button>
              ) : (
                <button className="co-list" onClick={() => chooseEmployee(e)}>
                  Open chat with {e.name}
                </button>
              )}
            </div>
          </article>
        ))}
      </div>
    </section>
  );
  const hiring = hireActive || d.employees.some((e) => e.state === "onboarding");
  const hireGuide = (
    <HireOnboarding
      snapshot={snapshot}
      refresh={refresh}
      open={openConversation}
      resumeId={resumeHire}
      onResumeHandled={() => setResumeHire("")}
      onActive={setHireActive}
    />
  );
  const needsCeo = (what: string) => (
    <div className="co-home-empty co-needs-ceo">
      <p>{what}</p>
      <button className="co-primary" onClick={() => setTab("employees")}>
        <UserPlus size={15} /> Hire a CEO
      </button>
    </div>
  );
  const chat = tab === "company" || tab === "employee";
  const mode = route.mode ?? "chat";
  const boardTasks = tab === "employee" ? d.tasks.filter((t) => t.employeeId === selected) : d.tasks;
  const activeWork = boardTasks.filter(
    (t) =>
      t.kind !== "conversation" &&
      ([...workGroups.review, ...workGroups.progress, ...workGroups.next] as string[]).includes(t.state),
  ).length;
  const workTask = chat && route.work ? d.tasks.find((t) => t.id === route.work) : undefined;
  const workWindow = workTask && (
    <WorkWindow
      key={workTask.id}
      snapshot={snapshot}
      task={workTask}
      busy={busy}
      run={run}
      refresh={refresh}
      open={openConversation}
      onChat={
        mode === "work" || !splitWide
          ? () => navigate({ ...routeRef.current, mode: "chat", work: splitWide ? workTask.id : "" })
          : undefined
      }
      onClose={closeWork}
    />
  );
  const cockpit = (
    <Cockpit
      snapshot={snapshot}
      needs={needs}
      scope={tab === "employee" ? selected : undefined}
      busy={busy}
      run={run}
      onOpenWork={openWork}
      onOpenNeed={openNeed}
      onClose={() => (sheet ? setSheet(false) : showCockpit(false))}
    />
  );
  const initiatingRoute = route;
  function openFromConversation(employeeId: string, threadId: string) {
    if (routeRef.current === initiatingRoute) openConversation(employeeId, threadId);
  }
  const navigation = [
    ["home", "Home", LayoutDashboard],
    ["company", "Chat", MessageSquareText],
    ["employees", "People", Users],
    ["departments", "Company", Building2],
    ["plans", "Plans", ClipboardList],
    ["meetings", "Meetings", MessagesSquare],
  ] as const;
  return (
    <div
      className={"fe-app dev-app co-app co-workspace" + (docked ? " with-cockpit" : "")}
      style={{ "--fe-cockpit-w": cockpitWidth + "px" } as CSSProperties}
    >
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
          className="co-list co-person"
          aria-current={tab === "company" ? "page" : undefined}
          onClick={() => setTab("company")}
        >
          <span className="co-avatar co-avatar-company" aria-hidden="true">
            <Building2 size={14} />
          </span>
          <span className="co-person-text">
            <b>Company conversation</b>
            <small>Notes and company updates</small>
          </span>
        </button>
        <h3>Employees</h3>
        {d.employees.map((person) => (
          <div key={person.id}>
            <button
              className="co-list co-person"
              aria-current={tab === "employee" && person.id === selected ? "page" : undefined}
              onClick={() => chooseEmployee(person)}
            >
              <span className="co-avatar" aria-hidden="true">
                {initials(person.name)}
                <i className={"co-presence " + person.state} />
              </span>
              <span className="co-person-text">
                <b>{person.name}</b>
                <small>
                  {roleName(snapshot.data.roles, person.roleId)} · {employeeState(person.state)}
                </small>
              </span>
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
        {/* Clear actions at the foot of the sidebar (UX audit, 2026-10-20). */}
        <div className="co-sidebar-foot">
          <button className="co-sidebar-action" onClick={() => setTab("employees")}>
            <UserPlus size={15} />
            {d.employees.length ? "Hire an employee" : "Hire your first employee"}
          </button>
          <button
            className="co-sidebar-action"
            aria-label={"Theme: " + theme}
            title="Theme: System, Light or Dark"
            onClick={() => setTheme(theme === "system" ? "light" : theme === "light" ? "dark" : "system")}
          >
            {theme === "light" ? <Sun size={15} /> : theme === "dark" ? <Moon size={15} /> : <Monitor size={15} />}
            Theme: {theme[0].toUpperCase() + theme.slice(1)}
          </button>
          <button className="co-sidebar-action co-hub-link" onClick={() => openHub("today")}>
            <Settings size={15} />
            Company settings
          </button>
        </div>
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
              {tab === "home"
                ? "Home"
                : tab === "company"
                  ? "Company conversation"
                  : tab === "employee"
                    ? employee?.name || "Conversation unavailable"
                    : tab === "meetings"
                      ? "Meetings"
                      : tab === "departments"
                        ? "Company"
                        : tab === "employees"
                          ? "People"
                          : tab[0].toUpperCase() + tab.slice(1)}
            </h1>
            {chat && (
              <small>
                {tab === "company"
                  ? "Company context · owner control"
                  : employee
                    ? roleName(d.roles, employee.roleId) +
                      " · " +
                      d.departments.find((x) => x.id === employee.departmentId)?.name +
                      " · private conversation"
                    : ""}
              </small>
            )}
          </div>
          {chat && (
            <nav className="fe-tabs fe-mode co-mode" aria-label="Conversation mode">
              <button aria-current={mode === "chat" ? "page" : undefined} onClick={() => setMode("chat")}>
                Chat
              </button>
              <button aria-current={mode === "work" ? "page" : undefined} onClick={() => setMode("work")}>
                Work <span className="fe-count">{activeWork}</span>
              </button>
            </nav>
          )}
          <span className="fe-topbar-spacer" />
          {/* A badge only when something is unusual (owner walkthrough, 2026-10-19). */}
          {(snapshot.capabilities.modelExecution || !snapshot.capabilities.measuredOneJob) && (
            <span className="co-status">
              {snapshot.capabilities.modelExecution ? "Synthetic test provider" : "Replies unavailable"}
            </span>
          )}
          {!docked && (
            <button
              className="fe-icon-button co-cockpit-toggle"
              aria-label={"Open cockpit" + (needs.length ? ", " + needs.length + " need you" : "")}
              title="Cockpit"
              onClick={() => (roomy ? showCockpit(true) : setSheet(true))}
            >
              <PanelRightOpen size={17} />
              {needs.length > 0 && <span className="fe-count attn">{needs.length}</span>}
            </button>
          )}
        </header>
        {error && (
          <div className="co-error" role="alert">
            {error}
            <button onClick={() => setError("")}>Dismiss</button>
          </div>
        )}
        <div className={chat ? "co-conversation-body" : "co-management"}>
          {tab === "home" && (
            <Home
              snapshot={snapshot}
              needs={needs}
              onOpenWork={openWork}
              onOpenNeed={openNeed}
              onChat={chooseEmployee}
              onWork={(e) =>
                navigate({
                  tab: "employee",
                  selected: e.id,
                  thread:
                    d.threads.find((t) => t.employeeId === e.id && (t.scope ?? "employee") === "employee")?.id || "",
                  mode: "work",
                  work: "",
                })
              }
              onHire={() => setTab("employees")}
            />
          )}
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
          {chat && mode === "work" && !workTask && (
            <WorkBoard
              snapshot={snapshot}
              tasks={boardTasks}
              everyone={tab === "company"}
              open={route.work ?? ""}
              onOpen={openWork}
            />
          )}
          {chat && workTask && (mode === "work" || !splitWide) && workWindow}
          {chat && mode === "chat" && (!workTask || splitWide) && (
            <div className={workTask ? "co-chat-and-work" : "co-chat-only"}>
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
                openWork={workTask && splitWide ? workTask.id : undefined}
                onShowWork={() => document.querySelector<HTMLElement>(".co-work-window")?.focus()}
                onOpenSetup={() => openHub("integrations")}
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
              {workTask && workWindow}
            </div>
          )}
          {tab === "departments" && (
            <nav className="co-hub-tabs" aria-label="Company sections">
              {hubSections.map(([key, label]) => (
                <button key={key} aria-current={section === key ? "page" : undefined} onClick={() => openHub(key)}>
                  {label}
                </button>
              ))}
            </nav>
          )}
          {tab === "departments" && section === "today" && <Today open version={d.cursor ?? 0} busy={busy} run={run} />}
          {tab === "departments" && section === "budgets" && (
            <Budgets
              open
              budgets={d.budgets}
              labels={d.budgetLabels}
              departments={d.departments}
              employees={d.employees}
              spend={d.spend}
              busy={busy}
              run={run}
            />
          )}
          {tab === "departments" && section === "members" && (
            <Members open role={memberRole} version={d.cursor ?? 0} busy={busy} run={run} />
          )}
          {tab === "departments" && section === "integrations" && (
            <Integrations open integrations={snapshot.capabilities.integrations ?? []} refresh={refresh} />
          )}
          {tab === "departments" && section === "audit" && <Audit open role={memberRole} version={d.cursor ?? 0} />}
          {tab === "departments" && section === "companies" && (
            <Companies expanded role={memberRole} version={d.cursor ?? 0} />
          )}
          {tab === "departments" && section === "departments" && (
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
                      <ContextEditor
                        snapshot={snapshot}
                        departmentId={dept.id}
                        fields={deptRole.onboardingFields.map((id) => ({ id, help: deptRole.onboarding?.help[id] }))}
                        drafter={guide}
                        busy={busy}
                        run={run}
                        refresh={refresh}
                      />
                    </>
                  )}
                </section>
              </div>
              {/* Company-wide facts every employee inherits, whatever their role (real-use batch, 2026-10-20). */}
              <section className="co-company-facts" aria-label="Company facts">
                <h2>Company facts</h2>
                <p className="co-muted">
                  What every employee may rely on, whatever their department. Approved facts go into every employee’s
                  context; drafts change nothing until you approve them.
                </p>
                <ContextEditor
                  snapshot={snapshot}
                  departmentId={null}
                  fields={d.companyFields ?? []}
                  drafter={d.employees.find((e) => e.state === "active") ?? d.employees[0]}
                  busy={busy}
                  run={run}
                  refresh={refresh}
                />
              </section>
            </>
          )}
          {tab === "employees" && (
            <>
              {/* The team first, hiring below it; while a hire is unfinished, finishing it comes first (UX audit,
                  2026-10-20). */}
              {!hiring && teamCards}
              {hireGuide}
              {hiring && teamCards}
              <section>
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
                  needsCeo("A CEO proposes company work for your approval. Hire and onboard one to start.")
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
                <h2>New meeting</h2>
                <p className="co-muted">
                  Only selected approved evidence and the shared meeting transcript reach participants.
                </p>
                {ceos.length === 0 ? (
                  needsCeo("A CEO chairs every meeting. Hire and onboard one to hold the first.")
                ) : (
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
                      <select
                        required
                        disabled={ceos.length === 0}
                        value={chair}
                        onChange={(e) => setChair(e.target.value)}
                      >
                        <option value="">{ceos.length ? "Choose the CEO who chairs" : "No CEO hired yet"}</option>
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
                )}
              </section>
              <section>
                <h2>Meeting record</h2>
                {d.meetings.length === 0 && <p className="co-muted">No meetings yet.</p>}
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
      {docked && (
        <div className="fe-cockpit-dock">
          <div
            className="fe-cockpit-resize"
            role="separator"
            aria-orientation="vertical"
            aria-label="Resize cockpit"
            aria-valuemin={240}
            aria-valuemax={480}
            aria-valuenow={cockpitWidth}
            tabIndex={0}
            title="Drag to resize · double-click to reset"
            onPointerDown={(event) => {
              if (event.button !== 0) return;
              drag.current = { x: event.clientX, width: cockpitWidth };
              event.currentTarget.setPointerCapture(event.pointerId);
            }}
            onPointerMove={(event) => {
              if (drag.current) resizeCockpit(drag.current.width + drag.current.x - event.clientX);
            }}
            onPointerUp={() => {
              drag.current = null;
            }}
            onPointerCancel={() => {
              drag.current = null;
            }}
            onDoubleClick={() => resizeCockpit(320)}
            onKeyDown={(event) => {
              const next =
                event.key === "ArrowLeft"
                  ? cockpitWidth + 24
                  : event.key === "ArrowRight"
                    ? cockpitWidth - 24
                    : event.key === "Home"
                      ? 240
                      : event.key === "End"
                        ? 480
                        : null;
              if (next === null) return;
              event.preventDefault();
              resizeCockpit(next);
            }}
          />
          {cockpit}
        </div>
      )}
      {help && <ShortcutHelp onClose={() => setHelp(false)} />}
      {!docked && sheet && (
        <div className="fe-sheet" role="dialog" aria-label="Cockpit">
          <button className="fe-sheet-scrim" aria-label="Close cockpit" onClick={() => setSheet(false)} />
          {cockpit}
        </div>
      )}
    </div>
  );
}
