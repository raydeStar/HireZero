import { lazy, Suspense, useEffect, useRef, useState, type CSSProperties, type FormEvent } from "react";
import { api, ApiError, applyTheme, company, preference, savePreference, type ThemeChoice } from "../lib/api";
import { ShortcutHelp, useShortcuts } from "./Shortcuts";
import { Modal } from "./Modal";
import { BrandMark } from "../components/BrandMark";
import { panels } from "../roles/company";
import { MemberAccess, useMemberAccess } from "./MemberAccess";
import { command, type Snapshot, type Employee, type ThreadDetail, type Event, type TaskSpec, type Work } from "./api";
import { Cockpit, useNeeds, workGroups, workState, type Need } from "./Cockpit";
import { optionalDraftReview } from "./readyDrafts";
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
  LogOut,
} from "lucide-react";
import { avatarStyle, Home, initials, isWorking, teamStatus } from "./Home";
import { LiveStrip } from "./LiveStrip";
import { BusinessBrief } from "./BusinessBrief";
import { Documents } from "./Documents";
import { Memory } from "./Memory";
import { Schedules } from "./Schedules";
import { Inbox, InboxButton } from "./Inbox";
import { HireOnboarding } from "./HireOnboarding";
import { Integrations } from "./Integrations";
import { Today } from "./Today";
import { Budgets } from "./Budgets";
import { Usage } from "./Usage";
const Billing = lazy(() => import("./Billing").then((module) => ({ default: module.Billing })));
import { Progress } from "./Progress";
import { Members } from "./Members";
import { Companies } from "./Companies";
import { ContextEditor } from "./ContextEditor";
import { Audit } from "./Audit";
import "@fontsource-variable/inter/wght.css";
import "./company.css";
import "./refresh.css";
import "./studio.css";
import { employeeState, humanize, roleName, taskState } from "./labels";
// A conversation's mode (Chat or Work) and its open work are part of the address (docs/adr/0040-the-muse-layout.md).
const hubSections = [
  ["inbox", "Inbox"],
  ["business", "Business brief"],
  ["documents", "Documents"],
  ["memory", "Memory"],
  ["schedules", "Recurring work"],
  ["today", "Today"],
  ["departments", "Departments"],
  ["budgets", "Budgets"],
  ["usage", "Usage"],
  ["billing", "Billing"],
  ["progress", "Progress"],
  ["members", "Members"],
  ["integrations", "Integrations"],
  ["audit", "Audit log"],
  ["companies", "Companies"],
] as const;
type HubSection = (typeof hubSections)[number][0];
const knowledgeSections: HubSection[] = ["business", "documents", "memory"];
const settingsSections: HubSection[] = [
  "integrations",
  "members",
  "billing",
  "budgets",
  "usage",
  "audit",
  "progress",
  "companies",
  "departments",
  "today",
];
const basicSettings: HubSection[] = ["integrations", "members", "billing"];
type ConversationRoute = {
  tab: string;
  selected: string;
  thread: string;
  mode?: "chat" | "work";
  work?: string;
  section?: HubSection;
  noticeTarget?: string;
  memoryId?: string;
  memoryWorkId?: string;
  memoryMeetingId?: string;
  repeatWorkId?: string;
  planId?: string;
  meetingId?: string;
};
function readRoute(): ConversationRoute {
  const query = new URLSearchParams(location.search),
    view = query.get("view") || "home",
    section = query.get("section");
  return {
    tab: ["home", "company", "employee", "departments", "employees", "plans", "meetings"].includes(view)
      ? view
      : "home",
    selected: query.get("employee") || "",
    thread: query.get("thread") || "",
    mode: query.get("mode") === "work" ? "work" : "chat",
    work: query.get("work") || "",
    section: hubSections.find(([key]) => key === section)?.[0] ?? "business",
    noticeTarget: query.get("schedule") || undefined,
    memoryId: query.get("memory") || undefined,
    memoryWorkId: query.get("memoryWork") || undefined,
    memoryMeetingId: query.get("memoryMeeting") || undefined,
    repeatWorkId: query.get("repeatWork") || undefined,
    planId: query.get("plan") || undefined,
    meetingId: query.get("meeting") || undefined,
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
export function CompanyWorkspace({ memberRole, onSignOut }: { memberRole?: string; onSignOut?: () => void } = {}) {
  return (
    <MemberAccess role={memberRole}>
      <CompanyWorkspaceContent memberRole={memberRole} onSignOut={onSignOut} />
    </MemberAccess>
  );
}

function CompanyWorkspaceContent({ memberRole, onSignOut }: { memberRole?: string; onSignOut?: () => void }) {
  const { canManage, canWork } = useMemberAccess();
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null),
    [events, setEvents] = useState<Event[]>([]),
    [error, setError] = useState(""),
    [loadError, setLoadError] = useState(""),
    [threadError, setThreadError] = useState(""),
    [busy, setBusy] = useState(false);
  const [route, setRoute] = useState<ConversationRoute>(readRoute),
    [detail, setDetail] = useState<ThreadDetail | null>(null),
    [showConversations, setShowConversations] = useState(false);
  const { tab, selected, thread, section = "today" } = route;
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
    if (company()) query.set("company", company());
    query.set("view", next.tab);
    if (next.tab === "employee") {
      query.set("employee", next.selected);
      query.set("thread", next.thread);
    }
    if (next.tab === "company" || next.tab === "employee") {
      if (next.mode === "work") query.set("mode", "work");
      if (next.work) query.set("work", next.work);
    }
    if (next.tab === "departments") query.set("section", next.section ?? "today");
    if (next.tab === "departments" && next.section === "schedules" && next.noticeTarget)
      query.set("schedule", next.noticeTarget);
    if (next.tab === "departments" && next.section === "schedules" && next.repeatWorkId)
      query.set("repeatWork", next.repeatWorkId);
    if (next.tab === "departments" && next.section === "memory" && next.memoryId) query.set("memory", next.memoryId);
    if (next.tab === "departments" && next.section === "memory" && next.memoryWorkId)
      query.set("memoryWork", next.memoryWorkId);
    if (next.tab === "departments" && next.section === "memory" && next.memoryMeetingId)
      query.set("memoryMeeting", next.memoryMeetingId);
    if (next.tab === "plans" && next.planId) query.set("plan", next.planId);
    if (next.tab === "meetings" && next.meetingId) query.set("meeting", next.meetingId);
    history.pushState(null, "", "?" + query);
  }
  function setTab(tab: string) {
    navigate({ ...routeRef.current, tab, work: "", planId: undefined, meetingId: undefined });
  }
  // One address, one visible section: Back should retrace our steps, not rearrange the furniture.
  function openHub(section: HubSection) {
    navigate({
      ...routeRef.current,
      tab: "departments",
      section,
      work: "",
      noticeTarget: undefined,
      memoryId: undefined,
      memoryWorkId: undefined,
      memoryMeetingId: undefined,
      repeatWorkId: undefined,
      planId: undefined,
      meetingId: undefined,
    });
  }
  function openConversation(employeeId: string, threadId: string, mode = routeRef.current.mode) {
    navigate({ tab: "employee", selected: employeeId, thread: threadId, mode, work: "" });
  }
  const rememberResult =
    !memberRole || memberRole === "owner"
      ? (artifactId: string) =>
          navigate({
            ...routeRef.current,
            tab: "departments",
            section: "memory",
            work: "",
            memoryId: undefined,
            memoryWorkId: artifactId,
            memoryMeetingId: undefined,
          })
      : undefined;
  const repeatWork =
    !memberRole || memberRole === "owner"
      ? (taskId: string) =>
          navigate({
            ...routeRef.current,
            tab: "departments",
            section: "schedules",
            work: "",
            noticeTarget: undefined,
            repeatWorkId: taskId,
          })
      : undefined;
  function setMode(mode: "chat" | "work") {
    navigate({ ...routeRef.current, mode, ...(mode === "chat" && !splitWide ? { work: "" } : {}) });
  }
  function closeWork() {
    navigate({ ...routeRef.current, work: "" });
  }
  // The cockpit: docked on wide screens, resizable and hideable, otherwise a sheet (docs/adr/0040-the-muse-layout.md).
  const roomy = useWide("(min-width: 1280px)"),
    // Chat and work side by side only when both get a readable column beside the cockpit (UX round 10).
    splitWide = useWide("(min-width: 1600px)"),
    compactHub = useWide("(max-width: 640px)");
  const visibleMode =
    !splitWide && snapshot?.data.tasks.some((task) => task.id === route.work && task.kind !== "conversation")
      ? "work"
      : (route.mode ?? "chat");
  const hubNavigation = useRef<HTMLElement>(null);
  const knowledgeNavigation = useRef<HTMLElement>(null);
  const advancedNavigation = useRef<HTMLElement>(null);
  const loaded = !!snapshot;
  useEffect(() => {
    if (compactHub && tab === "departments")
      for (const navigation of [hubNavigation, knowledgeNavigation, advancedNavigation])
        navigation.current
          ?.querySelector('[aria-current="page"]')
          ?.scrollIntoView({ block: "nearest", inline: "center" });
  }, [tab, section, compactHub, loaded]);
  const [cockpitOpen, setCockpitOpen] = useState(() => preference("cockpit-open", true)),
    [cockpitWidth, setCockpitWidth] = useState(() => Math.max(240, Math.min(480, preference("cockpit-width", 320)))),
    [sheet, setSheet] = useState(false);
  useEffect(() => {
    if (roomy) setSheet(false);
  }, [roomy]);
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
  const { needs, error: needsError, loading: needsLoading, retry: retryNeeds } = useNeeds(snapshot?.data.cursor ?? 0);
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
    go: (place) => (place === "settings" ? openHub("integrations") : setTab(place)),
    toggleMode: () => {
      const r = routeRef.current;
      if (r.tab === "company" || r.tab === "employee") setMode(visibleMode === "chat" ? "work" : "chat");
    },
    toggleCockpit: () => (roomy ? showCockpit(!cockpitOpen) : setSheet((open) => !open)),
    focusMessage: () => document.querySelector<HTMLTextAreaElement>(".fe-composer textarea")?.focus(),
    escape: () => {
      if (help) setHelp(false);
      else if (sheet) setSheet(false);
      else if (showConversations) setShowConversations(false);
      else if (routeRef.current.work) closeWork();
      else return false;
      return true;
    },
    help: () => setHelp((open) => !open),
  });
  function openWork(task: Work, mode?: "chat" | "work") {
    const where = snapshot?.data.threads.find((t) => t.id === task.threadId);
    const company = !!where && (where.scope ?? "employee") !== "employee";
    navigate({
      tab: company ? "company" : "employee",
      selected: company ? "" : task.employeeId,
      thread: company ? "" : task.threadId,
      mode: task.kind === "conversation" ? "chat" : (mode ?? "work"),
      work: task.id,
    });
    setSheet(false);
  }
  function openNeed(need: Need) {
    const task = need.taskId ? snapshot?.data.tasks.find((t) => t.id === need.taskId) : undefined;
    setSheet(false);
    if (task) openWork(task);
    // A Connect card opens the conversation it lives in (docs/adr/0046-connect-cards.md).
    else if (need.kind === "connection" && need.employeeId && need.threadId)
      openConversation(need.employeeId, need.threadId);
    // An assistant's request has no conversation; its card waits on the Integrations page.
    else if (need.kind === "connection") openHub("integrations");
    else if (need.kind === "business-freshness") openHub("business");
    else if (need.kind === "schedule")
      navigate({
        ...routeRef.current,
        tab: "departments",
        section: "schedules",
        noticeTarget: need.targetId,
        work: "",
      });
    else if (need.kind === "memory")
      navigate({ ...routeRef.current, tab: "departments", section: "memory", memoryId: need.taskId, work: "" });
    else if (need.kind === "plan") navigate({ tab: "plans", selected: "", thread: "", planId: need.targetId });
    else if (need.kind === "meeting") navigate({ tab: "meetings", selected: "", thread: "", meetingId: need.targetId });
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
  const hasSnapshot = snapshot !== null;
  useEffect(() => {
    const target = route.tab === "plans" ? route.planId : route.tab === "meetings" ? route.meetingId : undefined;
    if (!target || !hasSnapshot) return;
    const card = document.getElementById(`co-${route.tab}-${target}`);
    card?.focus({ preventScroll: true });
    card?.scrollIntoView({ block: "start" });
  }, [route.tab, route.planId, route.meetingId, hasSnapshot]);
  const [requestedHireRole, setRequestedHireRole] = useState("");
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
    try {
      const next = await api<Snapshot>("/company");
      if (!alive.current || request !== snapshotRequest.current) return;
      setSnapshot(next);
      let cursor = feedCursor.current;
      const found: Event[] = [];
      while (cursor < next.data.cursor) {
        const page = await api<Event[]>("/company/feed?after=" + cursor + "&limit=100");
        if (!alive.current || request !== snapshotRequest.current) return;
        if (!page.length) break;
        found.push(...page);
        cursor = page[page.length - 1].sequence;
      }
      feedCursor.current = Math.max(feedCursor.current, cursor);
      setEvents((previous) =>
        Array.from(new Map([...previous, ...found].map((event) => [event.sequence, event])).values())
          .sort((a, b) => a.sequence - b.sequence)
          .slice(-300),
      );
      setLoadError("");
    } catch (e) {
      if (alive.current && request === snapshotRequest.current) setLoadError((e as Error).message);
      throw e;
    }
    const target = current.current;
    if (routeRef.current.tab === "employee" && target.selected && target.thread)
      await refreshThread(target.selected, target.thread);
  }
  async function refreshThread(employee: string, id: string) {
    const request = ++threadRequest.current;
    const stillCurrent = () =>
      alive.current &&
      request === threadRequest.current &&
      current.current.selected === employee &&
      current.current.thread === id;
    try {
      const next = await api<ThreadDetail>("/company/employees/" + employee + "/threads/" + id);
      if (stillCurrent()) {
        setDetail(next);
        setThreadError("");
      }
    } catch (e) {
      if (stillCurrent()) setThreadError((e as Error).message);
      throw e;
    }
  }
  useEffect(() => {
    alive.current = true;
    void refresh().catch(() => {}); // The read owns its recoverable notice; commands keep their own receipts.
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") void refresh().catch(() => {});
    }, 3000);
    return () => {
      alive.current = false;
      clearInterval(timer);
    };
  }, []);
  useEffect(() => {
    setDetail(null);
    setThreadError("");
    if (selected && thread && tab === "employee") void refreshThread(selected, thread).catch(() => {});
  }, [selected, thread, tab]);
  const replyTarget = snapshot?.data.tasks.find((task) => task.id === route.work && task.kind === "conversation");
  useEffect(() => {
    if (tab !== "company" || !replyTarget) return;
    updateDraft("company", (previous) => ({
      ...(previous ?? { body: "", skill: "chat", brief: {}, evidence: [], recipient: "" }),
      recipient: replyTarget.employeeId,
    }));
  }, [tab, replyTarget?.id, replyTarget?.employeeId]);
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
      const message = (e as Error).message;
      if (e instanceof ApiError && e.status === 409) {
        try {
          await refresh();
          setError(message + " Latest details loaded. Review them before trying again.");
        } catch {
          setError(message + " The latest details could not load; refresh this page before trying again.");
        }
      } else setError(message);
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
        <p>{loadError || error || "Opening company workspace…"}</p>
        <button onClick={() => void refresh().catch(() => {})}>Retry</button>
      </main>
    );
  const d = snapshot.data,
    employee = d.employees.find((e) => e.id === selected),
    dept = d.departments.find((x) => x.id === department),
    guide = d.employees.find((e) => e.id === dept?.guide),
    deptRole = d.roles.find((r) => r.id === guide?.roleId);
  const attentionCount = needs.filter((need) => !optionalDraftReview(snapshot, need)).length;
  const readyCount = needs.length - attentionCount;
  const approved = d.artifacts.filter((a) => a.state === "approved"),
    ceos = d.employees.filter(
      (e) => e.state === "active" && d.roles.find((r) => r.id === e.roleId)?.skills.includes("company-plan"),
    );
  function chooseEmployee(e: Employee, mode = routeRef.current.mode) {
    openConversation(
      e.id,
      d.threads.find((t) => t.employeeId === e.id && (t.scope ?? "employee") === "employee")?.id || "",
      mode,
    );
  }
  const chooseEvidence = (
    <fieldset disabled={busy}>
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
              {e.state === "onboarding" && canManage ? (
                <button className="co-primary" onClick={() => setResumeHire(e.id)}>
                  Continue onboarding {e.name}
                </button>
              ) : (
                <button className="co-list" onClick={() => chooseEmployee(e, "chat")}>
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
  const hireGuide = canManage ? (
    <HireOnboarding
      snapshot={snapshot}
      refresh={refresh}
      open={openConversation}
      openWork={openWork}
      resumeId={resumeHire}
      onResumeHandled={() => setResumeHire("")}
      requestedRole={requestedHireRole}
      onRoleHandled={() => setRequestedHireRole("")}
      onActive={setHireActive}
    />
  ) : (
    <p role="status">
      An owner manages hiring and onboarding. Open an employee below to read their conversations and work.
    </p>
  );
  const needsCeo = (what: string) => (
    <div className="co-home-empty co-needs-ceo">
      <p>{what}</p>
      {canManage ? (
        <button
          className="co-primary"
          onClick={() => {
            // Hire the chair we asked for; a marketing detour belongs in a different meeting.
            setRequestedHireRole(d.roles.find((role) => role.skills.includes("company-plan"))?.id ?? "");
            setResumeHire("");
            setTab("employees");
          }}
        >
          <UserPlus size={15} /> Hire a CEO
        </button>
      ) : (
        <p className="co-muted">Ask a company owner to hire and onboard a CEO.</p>
      )}
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
  const workTask =
    chat && route.work ? d.tasks.find((t) => t.id === route.work && t.kind !== "conversation") : undefined;
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
      onExpand={
        mode === "chat" && splitWide
          ? () => {
              setMode("work");
              requestAnimationFrame(() => document.querySelector<HTMLElement>(".co-work-window")?.focus());
            }
          : undefined
      }
      onClose={closeWork}
      onOpenResult={(task) => openWork(task, "work")}
      onOpenPlans={() => setTab("plans")}
      onRememberResult={rememberResult}
      onRepeatWork={repeatWork}
      events={events}
    />
  );
  const cockpit = (
    <Cockpit
      snapshot={snapshot}
      needs={needs}
      needsError={needsError}
      needsLoading={needsLoading}
      onRetryNeeds={retryNeeds}
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
  const teamWorkspace =
    d.employees.filter((person) => person.state !== "retired").length > 1 ||
    d.employees.some((person) => d.roles.find((role) => role.id === person.roleId)?.skills.includes("company-plan"));
  const inSettings = tab === "departments" && settingsSections.includes(section);
  const navigation = [
    ["home", "Home", LayoutDashboard],
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
          {navigation
            .filter(([view]) => !["plans", "meetings"].includes(view) || teamWorkspace || tab === view)
            .map(([view, label, Icon]) => (
              <button
                className="fe-rail-button"
                aria-label={label}
                title={label}
                aria-current={tab === view && !inSettings ? "page" : undefined}
                onClick={() => (view === "departments" ? openHub("business") : setTab(view))}
                key={view}
              >
                <Icon size={21} />
                <span className="fe-rail-caption">{label}</span>
              </button>
            ))}
        </nav>
        <button
          className="fe-rail-button co-settings-link"
          aria-label="Company settings"
          title="Settings"
          aria-current={inSettings ? "page" : undefined}
          onClick={() => openHub("integrations")}
        >
          <Settings size={21} />
          <span className="fe-rail-caption">Settings</span>
        </button>
        <small className="co-owner-label">{humanize(memberRole || "owner")}</small>
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
              <span className="co-avatar" aria-hidden="true" style={avatarStyle(person.name)}>
                {initials(person.name)}
                <i className={"co-presence " + person.state + (isWorking(snapshot, person) ? " working" : "")} />
              </span>
              <span className="co-person-text">
                <b>{person.name}</b>
                {/* What they are doing now, not only whether they are active (UX round 10). */}
                <small>
                  {roleName(snapshot.data.roles, person.roleId)} · {teamStatus(snapshot, needs, person, canWork)}
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
          {canManage && (
            <button className="co-sidebar-action" onClick={() => setTab("employees")}>
              <UserPlus size={15} />
              {d.employees.length ? "Hire an employee" : "Hire your first employee"}
            </button>
          )}
          <button
            className="co-sidebar-action"
            aria-label={"Theme: " + theme}
            title="Theme: System, Light or Dark"
            onClick={() => setTheme(theme === "system" ? "light" : theme === "light" ? "dark" : "system")}
          >
            {theme === "light" ? <Sun size={15} /> : theme === "dark" ? <Moon size={15} /> : <Monitor size={15} />}
            Theme: {theme[0].toUpperCase() + theme.slice(1)}
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
                        ? inSettings
                          ? "Settings"
                          : "Company"
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
              <button aria-current={visibleMode === "chat" ? "page" : undefined} onClick={() => setMode("chat")}>
                Chat
              </button>
              <button aria-current={visibleMode === "work" ? "page" : undefined} onClick={() => setMode("work")}>
                Work <span className="fe-count">{activeWork}</span>
              </button>
            </nav>
          )}
          <span className="fe-topbar-spacer" />
          <InboxButton version={d.cursor ?? 0} open={() => openHub("inbox")} />
          {onSignOut && (
            <button
              className="fe-icon-button co-signout"
              aria-label="Sign out of HireZero"
              title="Sign out of HireZero"
              onClick={onSignOut}
            >
              <LogOut size={17} />
            </button>
          )}
          {/* A badge only when something is unusual (owner walkthrough, 2026-10-19). */}
          {(snapshot.capabilities.modelExecution || !snapshot.capabilities.measuredOneJob) && (
            <span className="co-status">
              {snapshot.capabilities.modelExecution ? "Synthetic test provider" : "Replies unavailable"}
            </span>
          )}
          {!docked && (
            <button
              className="fe-icon-button co-cockpit-toggle"
              aria-label={
                "Open cockpit" +
                (needsError ? ", needs unavailable" : attentionCount ? ", " + attentionCount + " need you" : "") +
                (readyCount ? ", " + readyCount + " drafts ready" : "")
              }
              title="Cockpit"
              onClick={() => (roomy ? showCockpit(true) : setSheet(true))}
            >
              <PanelRightOpen size={17} />
              {(needsError || attentionCount > 0) && (
                <span className="fe-count attn">{needsError ? "?" : attentionCount}</span>
              )}
            </button>
          )}
        </header>
        <LiveStrip
          snapshot={snapshot}
          needs={needs.filter((need) => !optionalDraftReview(snapshot, need))}
          showNeeds={!docked && !needsError && tab !== "home"}
          onOpenWork={(task) => openWork(task)}
          onOpenNeed={openNeed}
        />
        {(loadError || threadError) && (
          <div className="co-error co-refresh-error" role="status">
            {loadError ? "Workspace updates are unavailable. " : "This conversation could not refresh. "}
            {loadError || threadError} Your saved work is kept.
            <button onClick={() => void refresh().catch(() => {})}>Refresh workspace</button>
          </div>
        )}
        {error && (
          <div className="co-error" role="alert">
            {error}
            <button onClick={() => setError("")}>Dismiss</button>
          </div>
        )}
        <div className={chat ? "co-conversation-body" : "co-management"}>
          {d.workControl?.paused && (
            <div className="co-work-paused" role="status">
              <strong>New work is paused for this company.</strong> Previously admitted work may still finish.
              {memberRole === "owner" && (
                <button type="button" onClick={() => openHub("companies")}>
                  Manage work pause
                </button>
              )}
            </div>
          )}
          {tab === "home" && (
            <Home
              snapshot={snapshot}
              needs={needs}
              needsError={needsError}
              needsLoading={needsLoading}
              onRetryNeeds={retryNeeds}
              onOpenWork={(task) => openWork(task, "work")}
              onOpenNeed={openNeed}
              onChat={(employee) => chooseEmployee(employee, "chat")}
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
              onBusiness={() => openHub("business")}
              refresh={refresh}
              onOpenConversation={openConversation}
              canPrepare={!memberRole || memberRole === "owner" || memberRole === "approver"}
              onResumeHire={(employee) => {
                setResumeHire(employee.id);
                setTab("employees");
              }}
            />
          )}
          {tab === "company" && !d.employees.length && canManage && (
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
                memberRole={memberRole}
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
                focusReply={replyTarget?.id}
                onShowWork={(task) => {
                  openWork(task);
                  requestAnimationFrame(() => document.querySelector<HTMLElement>(".co-work-window")?.focus());
                }}
                onOpenPlans={() => setTab("plans")}
                onRememberResult={rememberResult}
                onRepeatWork={repeatWork}
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
            <>
              <nav
                ref={hubNavigation}
                className="co-hub-tabs"
                aria-label={inSettings ? "Settings sections" : "Company sections"}
              >
                {(inSettings
                  ? hubSections.filter(([key]) => basicSettings.includes(key))
                  : ([
                      ["business", "Company knowledge"],
                      ["schedules", "Recurring work"],
                      ["inbox", "Inbox"],
                    ] as const)
                ).map(([key, label]) => (
                  <button
                    key={key}
                    aria-current={
                      section === key || (key === "business" && knowledgeSections.includes(section))
                        ? "page"
                        : undefined
                    }
                    onClick={() => openHub(key)}
                  >
                    {label}
                  </button>
                ))}
              </nav>
              {knowledgeSections.includes(section) && (
                <nav ref={knowledgeNavigation} className="co-hub-tabs co-knowledge-tabs" aria-label="Company knowledge">
                  {hubSections
                    .filter(([key]) => knowledgeSections.includes(key))
                    .map(([key, label]) => (
                      <button
                        key={key}
                        aria-current={section === key ? "page" : undefined}
                        onClick={() => openHub(key)}
                      >
                        {label}
                      </button>
                    ))}
                </nav>
              )}
              {inSettings && (
                <details className="co-advanced-settings" open={!basicSettings.includes(section)}>
                  <summary>Advanced settings</summary>
                  <nav ref={advancedNavigation} className="co-hub-tabs" aria-label="Advanced settings">
                    {hubSections
                      .filter(([key]) => settingsSections.includes(key) && !basicSettings.includes(key))
                      .map(([key, label]) => (
                        <button
                          key={key}
                          aria-current={section === key ? "page" : undefined}
                          onClick={() => openHub(key)}
                        >
                          {label}
                        </button>
                      ))}
                    {!teamWorkspace && (
                      <>
                        <button onClick={() => setTab("plans")}>Plans</button>
                        <button onClick={() => setTab("meetings")}>Meetings</button>
                      </>
                    )}
                  </nav>
                </details>
              )}
            </>
          )}
          {tab === "departments" && section === "today" && (
            <Today snapshot={snapshot} open version={d.cursor ?? 0} busy={busy} run={run} onOpenNeed={openNeed} />
          )}
          {tab === "departments" && section === "inbox" && (
            <Inbox
              role={memberRole}
              refresh={refresh}
              onOpenWork={openWork}
              onSources={() => openHub("business")}
              onSchedule={(id) =>
                navigate({ ...routeRef.current, tab: "departments", section: "schedules", noticeTarget: id, work: "" })
              }
              onMemory={(id) =>
                navigate({ ...routeRef.current, tab: "departments", section: "memory", memoryId: id, work: "" })
              }
              onPlan={(id) => navigate({ tab: "plans", selected: "", thread: "", planId: id })}
              onMeeting={(id) => navigate({ tab: "meetings", selected: "", thread: "", meetingId: id })}
            />
          )}
          {tab === "departments" && section === "business" && (
            <BusinessBrief
              snapshot={snapshot}
              refresh={refresh}
              role={memberRole}
              onWork={(task) => openWork(task, "work")}
              onHire={(role) => {
                setRequestedHireRole(role ?? "");
                setTab("employees");
              }}
              onEmployee={(employee) => {
                if (employee.state === "onboarding") {
                  setResumeHire(employee.id);
                  setTab("employees");
                } else {
                  const thread = d.threads.find(
                    (t) => t.employeeId === employee.id && (t.scope ?? "employee") === "employee",
                  );
                  if (thread) openConversation(employee.id, thread.id);
                }
              }}
            />
          )}
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
          {tab === "departments" && section === "usage" && (
            <Usage
              role={memberRole}
              onBilling={() => openHub("billing")}
              employees={d.employees}
              onOpenWork={async (id) => {
                const result = await api<{ task: Work }>(`/company/tasks/${id}`);
                await refresh();
                openWork(result.task);
              }}
            />
          )}
          {tab === "departments" && section === "documents" && <Documents snapshot={snapshot} role={memberRole} />}
          {tab === "departments" && section === "billing" && (
            <Suspense fallback={<p role="status">Loading billing…</p>}>
              <Billing role={memberRole} onUsage={() => openHub("usage")} />
            </Suspense>
          )}
          {tab === "departments" && section === "progress" && (
            <Progress
              onBusiness={() => openHub("business")}
              onUsage={() => openHub("usage")}
              onOpenWork={async (id) => {
                const result = await api<{ task: Work }>(`/company/tasks/${id}`);
                await refresh();
                openWork(result.task, "work");
              }}
            />
          )}
          {tab === "departments" && section === "memory" && (
            <Memory
              snapshot={snapshot}
              role={memberRole}
              initialId={route.memoryId}
              initialWorkId={route.memoryWorkId}
              initialMeetingId={route.memoryMeetingId}
              refresh={refresh}
              onOpenWork={openWork}
              onOpenMeeting={(id) => navigate({ tab: "meetings", selected: "", thread: "", meetingId: id })}
            />
          )}
          {tab === "departments" && section === "schedules" && (
            <Schedules
              key={route.repeatWorkId || "schedules"}
              initialTaskId={route.repeatWorkId}
              onReviewSchedule={(id) => navigate({ ...routeRef.current, repeatWorkId: undefined, noticeTarget: id })}
              initialId={route.noticeTarget}
              snapshot={snapshot}
              role={memberRole}
              refresh={refresh}
              onOpenWork={openWork}
              onSources={() => openHub("business")}
            />
          )}
          {tab === "departments" && section === "members" && (
            <Members open role={memberRole} version={d.cursor ?? 0} busy={busy} run={run} />
          )}
          {tab === "departments" && section === "integrations" && (
            <Integrations
              role={memberRole}
              open
              integrations={snapshot.capabilities.integrations ?? []}
              refresh={refresh}
              snapshot={snapshot}
              run={run}
            />
          )}
          {tab === "departments" && section === "audit" && <Audit open role={memberRole} version={d.cursor ?? 0} />}
          {tab === "departments" && section === "companies" && (
            <Companies
              expanded
              role={memberRole}
              version={d.cursor ?? 0}
              workControl={d.workControl}
              onWorkControl={(control) => {
                snapshotRequest.current++;
                setSnapshot((previous) =>
                  previous && (!previous.data.workControl || control.version >= previous.data.workControl.version)
                    ? { ...previous, data: { ...previous.data, workControl: control } }
                    : previous,
                );
              }}
            />
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
                  {canManage ? (
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
                  ) : (
                    <p className="co-muted">An owner creates departments and edits shared facts.</p>
                  )}
                </section>
                <section>
                  <h2>{dept?.name || "Select a department"}</h2>
                  {dept && !guide && (
                    <p>
                      {canManage ? "Hire its first employee" : "Ask an owner to hire its first employee"} to begin
                      department onboarding.
                    </p>
                  )}
                  {dept && deptRole && (
                    <>
                      <p className="co-muted">
                        {guide?.name} guides onboarding. {canWork ? "You can approve" : "An approver or owner approves"}{" "}
                        the knowledge inherited by later hires.
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
                  context; drafts change nothing until an approver or owner approves them.
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
              {canManage && (
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
              )}
            </>
          )}
          {tab === "plans" && (
            <div className="co-columns">
              <section>
                <h2>CEO delegation</h2>
                {panels(d.roles, "plan-guidance").map(({ roleId, Panel }) => (
                  <Panel key={roleId} />
                ))}
                {!canWork ? (
                  <p className="co-muted">
                    An approver or owner prepares and approves plans. You can read company plans below.
                  </p>
                ) : ceos.length === 0 ? (
                  needsCeo("A CEO proposes company work for your approval. Hire and onboard one to start.")
                ) : (
                  <form
                    aria-label="Propose company plan"
                    onSubmit={form(async () => {
                      const created = (await run("plan.create", {
                        employeeId: ceos[0].id,
                        body: planText,
                        tasks: planTasks,
                        budget: Math.max(22000, planTasks.length * 22000),
                      })) as { id: string } | undefined;
                      // An unanswered knock is not a receipt. Keep the draft until its save is confirmed.
                      if (!created?.id) return;
                      setPlanTasks([]);
                      setPlanText("");
                    })}
                  >
                    <label>
                      Objective
                      <textarea
                        required
                        disabled={busy}
                        value={planText}
                        onChange={(e) => setPlanText(e.target.value)}
                      />
                    </label>
                    {planTasks.length === 0 && (
                      <button
                        type="button"
                        className="co-primary"
                        disabled={busy || !planText.trim()}
                        onClick={() =>
                          void (async () => {
                            const target = d.threads.find(
                              (t) => t.employeeId === ceos[0].id && (t.scope ?? "employee") === "employee",
                            );
                            if (!target) return;
                            const work = (await run("task.create", {
                              employeeId: ceos[0].id,
                              threadId: target.id,
                              skill: "company-plan",
                              body: planText,
                            })) as Work | undefined;
                            if (work?.id) openWork(work);
                          })()
                        }
                      >
                        Ask CEO to draft tasks
                      </button>
                    )}
                    {planTasks.map((task, index) => (
                      <fieldset key={index} disabled={busy}>
                        <legend>Task {index + 1}</legend>
                        <label>
                          Assignee
                          <select
                            required
                            value={task.employeeId}
                            onChange={(e) => {
                              const tasks = [...planTasks];
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
                        <button
                          type="button"
                          aria-label={"Remove task " + (index + 1)}
                          onClick={() =>
                            setPlanTasks((tasks) =>
                              tasks
                                .filter((_, taskIndex) => taskIndex !== index)
                                .map((remaining) => ({
                                  ...remaining,
                                  dependsOn: remaining.dependsOn
                                    .filter((dependency) => dependency !== index)
                                    .map((dependency) => (dependency > index ? dependency - 1 : dependency)),
                                })),
                            )
                          }
                        >
                          Remove task
                        </button>
                      </fieldset>
                    ))}
                    {planTasks.length === 0 && (
                      <p className="co-muted">
                        Ask the coordinator for a draft, or add tasks yourself. Drafting starts only after you review
                        and authorize its model job.
                      </p>
                    )}
                    {planTasks.length > 0 && (
                      <p className="co-muted">
                        Shared ceiling: {(planTasks.length * 22000).toLocaleString()} planning tokens. Approval allows
                        seven days for this work. Each measured job needs separate authorization.
                      </p>
                    )}
                    <button
                      type="button"
                      disabled={busy || planTasks.length >= 4}
                      onClick={() =>
                        setPlanTasks([
                          ...planTasks,
                          { employeeId: "", threadId: "", skill: "chat", input: "", evidence: [], dependsOn: [] },
                        ])
                      }
                    >
                      Add task
                    </button>
                    <Submit busy={busy || planTasks.length === 0 || !planText.trim()}>Propose plan</Submit>
                  </form>
                )}
              </section>
              <section>
                <h2>Company plans</h2>
                {route.planId && !d.plans.some((p) => p.id === route.planId) && (
                  <p role="alert">This plan is unavailable in this company. Choose a current plan below.</p>
                )}
                {d.plans.map((p) => (
                  <article
                    key={p.id}
                    id={`co-plans-${p.id}`}
                    tabIndex={-1}
                    className={`co-card${route.planId === p.id ? " co-notice-target" : ""}`}
                    data-plan-id={p.id}
                  >
                    <b>{p.objective}</b>
                    <p>
                      {humanize(p.state)} · shared ceiling {p.budget.toLocaleString()} tokens
                    </p>
                    <p className="co-muted">
                      {p.state === "proposed"
                        ? "Approval expires"
                        : p.state === "completed"
                          ? "Original deadline"
                          : "Execution deadline"}
                      : {new Date(p.deadline).toLocaleString()}
                      {p.state === "proposed" && p.executionHours
                        ? ` · ${p.executionHours / 24} days to perform the approved work`
                        : ""}
                      .
                    </p>
                    {p.source && (
                      <p className="co-muted">
                        Based on an approved coordinator proposal.
                        {p.state !== "completed" &&
                          p.state !== "canceled" &&
                          " Revoking that proposal stops new or pending execution."}
                      </p>
                    )}
                    {p.tasks.map((t, i) => {
                      const work = d.tasks.find((task) => task.id === p.taskIds[i]);
                      return (
                        <div key={i} className="co-plan-task">
                          <p>
                            <b>
                              {i + 1}. {d.employees.find((e) => e.id === t.employeeId)?.name}
                            </b>
                            : {t.input}
                          </p>
                          {t.dependsOn.length > 0 && (
                            <small>
                              After approved completion of {t.dependsOn.map((n) => `task ${n + 1}`).join(", ")}. Its
                              approved results will be included.
                            </small>
                          )}
                          {work && (
                            <button onClick={() => openWork(work)}>
                              Open task {i + 1} · {taskState(workState(snapshot, work))}
                            </button>
                          )}
                        </div>
                      );
                    })}
                    {canWork && p.state === "proposed" && (
                      <button
                        disabled={busy || Date.parse(p.deadline) <= Date.now()}
                        onClick={() =>
                          void run("plan.approve", { id: p.id, expectedVersion: p.version, digest: p.digest })
                        }
                      >
                        Approve exact plan
                      </button>
                    )}
                    {canWork && !["completed", "canceled"].includes(p.state) && (
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
                {!canWork ? (
                  <p className="co-muted">
                    An approver or owner manages meetings. You can read saved meeting records below.
                  </p>
                ) : ceos.length === 0 ? (
                  needsCeo("A CEO chairs every meeting. Hire and onboard one to hold the first.")
                ) : (
                  <form
                    aria-label="Prepare company meeting"
                    onSubmit={form(async () => {
                      const created = (await run("meeting.create", {
                        name: meetingTitle,
                        body: agenda,
                        participants,
                        chairId: chair,
                        evidence,
                        budget: 50000,
                      })) as { id: string } | undefined;
                      if (!created?.id) return;
                      setMeetingTitle("");
                      setAgenda("");
                    })}
                  >
                    <label>
                      Title
                      <input
                        required
                        disabled={busy}
                        value={meetingTitle}
                        onChange={(e) => setMeetingTitle(e.target.value)}
                      />
                    </label>
                    <label>
                      Agenda
                      <textarea required disabled={busy} value={agenda} onChange={(e) => setAgenda(e.target.value)} />
                    </label>
                    <fieldset disabled={busy}>
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
                        disabled={busy || ceos.length === 0}
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
                {route.meetingId && !d.meetings.some((m) => m.id === route.meetingId) && (
                  <p role="alert">This meeting is unavailable in this company. Choose a current meeting below.</p>
                )}
                {d.meetings.length === 0 && <p className="co-muted">No meetings yet.</p>}
                {d.meetings.map((m) => (
                  <article
                    className={`co-card${route.meetingId === m.id ? " co-notice-target" : ""}`}
                    key={m.id}
                    id={`co-meetings-${m.id}`}
                    data-meeting-id={m.id}
                    tabIndex={-1}
                  >
                    <h3>{m.title}</h3>
                    <p>{m.agenda}</p>
                    <p>
                      {m.state === "outcome_proposed"
                        ? "Ready for minutes"
                        : m.state === "awaiting_owner"
                          ? "Minutes need approval"
                          : m.state === "closed"
                            ? "Minutes accepted"
                            : humanize(m.state)}{" "}
                      · {m.turns.length}/{m.participants.length * 2} turns
                    </p>
                    {m.turns.map((t) => (
                      <details key={t.taskId}>
                        <summary>{d.employees.find((e) => e.id === t.employeeId)?.name}</summary>
                        <pre>{t.body}</pre>
                      </details>
                    ))}
                    {canWork && (
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
                    )}
                    {canWork && m.state === "outcome_proposed" && (
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
                    {canManage && m.state === "closed" && (
                      <button
                        onClick={() =>
                          navigate({
                            tab: "departments",
                            section: "memory",
                            selected: "",
                            thread: "",
                            memoryMeetingId: m.id,
                          })
                        }
                      >
                        Remember a decision
                      </button>
                    )}
                    {canWork && m.state === "awaiting_owner" && (
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
        <Modal label="Cockpit" onClose={() => setSheet(false)}>
          {cockpit}
        </Modal>
      )}
    </div>
  );
}
