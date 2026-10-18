import { applyTheme } from "../lib/api";
import { useCallback, useEffect, useMemo, useRef, useState, type CSSProperties } from "react";
import {
  Columns2,
  Folder,
  Keyboard,
  LockKeyhole,
  Maximize2,
  MessageSquareText,
  PanelLeftClose,
  PanelLeftOpen,
  PanelRightOpen,
  MessagesSquare,
  Plus,
  Search,
  Settings,
  Users,
} from "lucide-react";
import { BrandMark } from "../components/BrandMark";
import { ViewBoundary } from "../components/ErrorBoundary";
import { hiredRoles } from "../roles";
import type { RoleActivity } from "../roles/types";
import {
  api,
  ApiError,
  initials,
  preference,
  savePreference,
  type AgentState,
  type ReplyStatus,
  type ShiftSchedule,
  type Detail,
  type Person,
  type ReplyState,
  type Session,
  type SharedWork,
  type WorkRequest,
  titleOf,
} from "./api";
import { Cockpit } from "./Cockpit";
import { Conversation } from "./Conversation";
import { WorkView, RequestWindow } from "./Work";
import {
  CommandPalette,
  Dialog,
  NewRequestDialog,
  ProjectsView,
  SettingsView,
  TeamView,
  type ThemeChoice,
} from "./Panels";
import { WaitingForAccess } from "./Access";
import { ThreadName, Threads } from "./Threads";

type Route = {
  view: "home" | "projects" | "team" | "settings";
  pane: "chat" | "work";
  open: string | null;
  inspect: "chat" | "work" | null;
  fresh?: boolean;
};
function readRoute(): Route {
  const params = new URLSearchParams(location.search),
    view = params.get("view"),
    inspect = params.get("inspect");
  return {
    view: view === "projects" || view === "team" || view === "settings" ? view : "home",
    pane: params.get("pane") === "work" ? "work" : "chat",
    open: params.get("open"),
    inspect: inspect === "chat" || inspect === "work" ? inspect : null,
    fresh: params.get("new") === "1",
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
export { applyTheme };

/** marketing-hire's rail, Chat/Work shell and cockpit, bound to scoped development records. */
export function HireZeroWorkspace() {
  const [session, setSession] = useState<Session | null>(null),
    [loading, setLoading] = useState(true),
    [error, setError] = useState("");
  const [requests, setRequests] = useState<WorkRequest[]>([]),
    [workload, setWorkload] = useState<SharedWork[]>([]),
    [people, setPeople] = useState<Person[]>([]),
    [order, setOrder] = useState<string[]>([]);
  const [route, setRoute] = useState<Route>(readRoute),
    [detail, setDetail] = useState<Detail | null>(null),
    [opening, setOpening] = useState(false),
    [observedActivity, setObservedActivity] = useState<string | null>(null),
    [project, setProject] = useState("");
  const [conversationKey, setConversationKey] = useState(() => route.open || crypto.randomUUID()),
    [replies, setReplies] = useState<Record<string, ReplyState>>({});
  const [roleActivity, setRoleActivity] = useState<RoleActivity[]>([]),
    [agent, setAgent] = useState<AgentState | null>(null);
  const [theme, setTheme] = useState<ThemeChoice>(() => preference("theme", "system")),
    [railWide, setRailWide] = useState(() => preference("rail-wide", false)),
    [cockpitOpen, setCockpitOpen] = useState(() => preference("cockpit-open", true)),
    [cockpitWidth, setCockpitWidth] = useState(() => Math.max(280, Math.min(720, preference("cockpit-width", 320))));
  const [sheet, setSheet] = useState(false),
    [menu, setMenu] = useState(false),
    [palette, setPalette] = useState(false),
    [shortcuts, setShortcuts] = useState(false),
    [newRequest, setNewRequest] = useState<{ prefill: string } | null>(null);
  const [threadsPinned, setThreadsPinned] = useState(() => preference("threads-open", true)),
    [threadsDrawer, setThreadsDrawer] = useState(false),
    [threadsVersion, setThreadsVersion] = useState(0);
  const roomy = useWide("(min-width: 1280px)"),
    wide = useWide("(min-width: 1100px)"),
    owner = session?.role === "owner" && !session.executionHeld,
    roles = useMemo(() => hiredRoles(session), [session]);
  const drag = useRef<{ x: number; width: number } | null>(null),
    currentRoute = useRef(route),
    currentDetail = useRef(detail),
    replyStates = useRef(replies),
    live = useRef(true),
    createAttempt = useRef<{ body: string; key: string } | null>(null);
  currentRoute.current = route;
  currentDetail.current = detail;
  const forbidden = useCallback(() => {
    live.current = false;
    setSession(null);
    setRequests([]);
    setWorkload([]);
    setPeople([]);
    setOrder([]);
    setDetail(null);
    setObservedActivity(null);
    replyStates.current = {};
    setReplies({});
    setNewRequest(null);
    setPalette(false);
    setError("Your access changed. Reload to verify your identity.");
  }, []);
  const fail = useCallback(
    (cause: unknown) => {
      if (cause instanceof ApiError && cause.status === 403) forbidden();
      else setError((cause as Error).message);
    },
    [forbidden],
  );
  const refresh = useCallback(async (current: Session) => {
    if (current.executionHeld) return;
    const [items, shared, members, queue] = await Promise.all([
      api<WorkRequest[]>("/requests"),
      current.role === "visitor" || current.role === "trusted" ? Promise.resolve([]) : api<SharedWork[]>("/workload"),
      current.role === "owner" ? api<Person[]>("/people") : Promise.resolve([]),
      current.role === "owner" ? api<string[]>("/intake-order") : Promise.resolve([]),
    ]);
    if (!live.current) return;
    setRequests(items);
    setWorkload(shared);
    setPeople(members);
    setOrder(queue);
    setError("");
  }, []);
  const refreshDetail = useCallback(async (id: string) => {
    const next = await api<Detail>("/requests/" + id);
    if (live.current && currentRoute.current.open === id) setDetail(next);
  }, []);
  function replyState(id: string, state?: ReplyState) {
    const next = { ...replyStates.current };
    if (state) next[id] = state;
    else delete next[id];
    replyStates.current = next;
    setReplies(next);
    if (state?.status !== "responding") setObservedActivity((current) => (current === id ? null : current));
  }
  // The host's agent queue answers; this follows the reply until it lands, showing the agent's acknowledgement when it's busy.
  async function answer(id: string, retry = false) {
    const now = replyStates.current[id]?.status;
    if (!live.current || now === "responding" || now === "queued") return;
    replyState(id, { status: "responding" });
    try {
      await api("/requests/" + id + "/agent-reply", "POST", { retry });
      for (;;) {
        if (!live.current) return;
        const status = await api<ReplyStatus>("/requests/" + id + "/agent-status");
        if (status.state === "failed") {
          replyState(id, { status: "failed", error: status.error || "The agent could not reply." });
          await refreshDetail(id).catch(fail);
          return;
        }
        if (status.state === "queued" && status.current)
          replyState(id, { status: "queued", current: status.current, ahead: status.ahead });
        else if (status.state === "queued" || status.state === "active")
          replyState(id, { status: "responding", step: status.step || undefined });
        else break;
        await new Promise((resolve) => setTimeout(resolve, 2000));
      }
      await refreshDetail(id).catch(fail);
      if (live.current) replyState(id);
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 403) {
        forbidden();
        return;
      }
      if (live.current)
        replyState(id, {
          status: "failed",
          error: (cause as Error).message,
          retryAt:
            cause instanceof ApiError && cause.retryAfterSeconds
              ? Date.now() + cause.retryAfterSeconds * 1000
              : undefined,
        });
    }
  }
  const loadAgent = useCallback(async () => {
    try {
      const value = await api<AgentState>("/agent");
      if (live.current) setAgent(value);
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 403) forbidden();
    }
  }, [forbidden]);
  async function agentAction(path: string, body: object = {}, method = "POST") {
    try {
      await api(path, method, body);
    } finally {
      await loadAgent();
    }
  }
  useEffect(() => {
    live.current = true;
    api<Session>("/session")
      .then(async (current) => {
        if (!live.current) return;
        setSession(current);
        if (current.role !== "pending") await refresh(current);
      })
      .catch(fail)
      .finally(() => setLoading(false));
    return () => {
      live.current = false;
    };
  }, [refresh, fail]);
  useEffect(() => {
    if (!session) return;
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") void refresh(session).catch(fail);
    }, 30000);
    return () => clearInterval(timer);
  }, [session, refresh, fail]);
  // Hired roles may report long-running work outside the agent queue (a coding run, for example).
  const roleKey = roles.map((role) => role.id).join(",");
  useEffect(() => {
    if (!owner) {
      setRoleActivity([]);
      return;
    }
    let active = true;
    async function load() {
      try {
        const found: RoleActivity[] = [];
        for (const role of roles) {
          if (!role.activity) continue;
          for (const item of await api<unknown[]>(role.activity.path)) {
            const value = role.activity.describe(item);
            if (value) found.push(value);
          }
        }
        if (active) setRoleActivity(found);
      } catch (cause) {
        if (active && cause instanceof ApiError && cause.status === 403) forbidden();
      }
    }
    void load();
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") void load();
    }, 8000);
    return () => {
      active = false;
      clearInterval(timer);
    };
  }, [owner, forbidden, roleKey]);
  useEffect(() => {
    if (!owner) {
      setAgent(null);
      return;
    }
    void loadAgent();
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") void loadAgent();
    }, 5000);
    return () => clearInterval(timer);
  }, [owner, loadAgent]);
  useEffect(() => {
    if (!session || session.executionHeld || !route.open) {
      setDetail(null);
      setOpening(false);
      return;
    }
    if (currentDetail.current?.request.id === route.open) {
      setOpening(false);
      return;
    }
    let active = true;
    setDetail(null);
    setOpening(true);
    api<Detail>("/requests/" + route.open)
      .then((next) => {
        if (active && live.current) setDetail(next);
      })
      .catch((cause) => {
        if (active) fail(cause);
      })
      .finally(() => {
        if (active) setOpening(false);
      });
    return () => {
      active = false;
    };
  }, [route.open, session, fail]);
  // Chat with nothing open is Home, the one running conversation; "New conversation" asks for a blank one explicitly.
  useEffect(() => {
    if (
      !session ||
      session.executionHeld ||
      session.role === "pending" ||
      route.view !== "home" ||
      route.pane !== "chat" ||
      route.open ||
      route.fresh
    )
      return;
    let active = true;
    api<WorkRequest>("/home")
      .then((home) => {
        if (!active || !live.current || currentRoute.current.open || currentRoute.current.fresh) return;
        const next = { ...currentRoute.current, open: home.id, inspect: null };
        currentRoute.current = next;
        setConversationKey(home.id);
        setRoute(next);
        const url = new URL(location.href);
        url.searchParams.set("open", home.id);
        history.replaceState(null, "", url);
      })
      .catch(fail);
    return () => {
      active = false;
    };
  }, [session, route.view, route.pane, route.open, route.fresh, fail]);
  useEffect(() => {
    applyTheme(theme);
    savePreference("theme", theme);
    const media = matchMedia("(prefers-color-scheme: dark)"),
      change = () => {
        if (theme === "system") applyTheme(theme);
      };
    media.addEventListener("change", change);
    return () => media.removeEventListener("change", change);
  }, [theme]);
  useEffect(() => {
    savePreference("rail-wide", railWide);
  }, [railWide]);
  useEffect(() => {
    savePreference("threads-open", threadsPinned);
  }, [threadsPinned]);
  useEffect(() => {
    savePreference("cockpit-open", cockpitOpen);
  }, [cockpitOpen]);
  useEffect(() => {
    const back = () => {
      const next = readRoute();
      if (next.open !== currentRoute.current.open) setConversationKey(next.open || crypto.randomUUID());
      currentRoute.current = next;
      setRoute(next);
    };
    addEventListener("popstate", back);
    return () => removeEventListener("popstate", back);
  }, []);
  useEffect(() => {
    const key = (event: KeyboardEvent) => {
      if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === "k") {
        event.preventDefault();
        setPalette((value) => !value);
      }
      if (event.key === "Escape") {
        setMenu(false);
        setSheet(false);
        setThreadsDrawer(false);
      }
    };
    addEventListener("keydown", key);
    return () => removeEventListener("keydown", key);
  }, []);
  useEffect(() => {
    document.title =
      (route.view === "home"
        ? route.pane === "chat"
          ? "Chat"
          : "Work"
        : route.view[0].toUpperCase() + route.view.slice(1)) + " · HireZero";
  }, [route.view, route.pane]);
  function go(next: Route, preserveConversation = false) {
    if (next.open !== currentRoute.current.open && !preserveConversation)
      setConversationKey(next.open || crypto.randomUUID());
    currentRoute.current = next;
    setRoute(next);
    setMenu(false);
    setSheet(false);
    const url = new URL(location.href);
    url.search = "";
    if (next.view !== "home") url.searchParams.set("view", next.view);
    if (next.pane === "work") url.searchParams.set("pane", "work");
    if (next.open) url.searchParams.set("open", next.open);
    if (next.inspect && next.open) url.searchParams.set("inspect", next.inspect);
    if (next.fresh && !next.open) url.searchParams.set("new", "1");
    history.pushState(null, "", url);
  }
  function open(id: string) {
    go({ view: "home", pane: route.pane === "work" || !wide ? "work" : "chat", open: id, inspect: route.pane });
  }
  async function changed() {
    if (session) await refresh(session);
    if (route.open) await refreshDetail(route.open);
  }
  async function create(name: string, title: string, description: string, key: string, inspect: boolean) {
    const item = await api<WorkRequest>("/requests", "POST", {
      project: name.trim() || "General development",
      title,
      description,
      clientRequestId: key,
    });
    if (!live.current) return item;
    go(
      { view: "home", pane: inspect && !wide ? "work" : "chat", open: item.id, inspect: inspect ? "chat" : null },
      !inspect,
    );
    setDetail({ request: item, messages: [] });
    setRequests((items) => [...items.filter((previous) => previous.id !== item.id), item]);
    if (session) void refresh(session).catch(fail);
    return item;
  }
  async function createFromChat(text: string) {
    const body = JSON.stringify([project, text]);
    if (createAttempt.current?.body !== body) createAttempt.current = { body, key: crypto.randomUUID() };
    const item = await create(
      project,
      text.trim().split("\n")[0].slice(0, 160),
      text,
      createAttempt.current.key,
      false,
    );
    createAttempt.current = null;
    return item;
  }
  function resize(value: number) {
    const next = Math.round(Math.max(280, Math.min(720, innerWidth * 0.5, value)));
    setCockpitWidth(next);
    savePreference("cockpit-width", next);
  }
  const work = () => go({ view: "home", pane: "work", open: null, inspect: null });
  if (loading) return <main className="fe-loading dev-entry">Opening your workspace…</main>;
  if (session?.executionHeld)
    return (
      <main className="fe-loading dev-entry">
        <LockKeyhole size={30} />
        <h1>Execution held for verification</h1>
        <p role="status">
          Your existing tasks and approvals are preserved. This HireZero host will not run background work, invoke
          models or dispatch task actions.
        </p>
        <p>The owner must approve resumption after verification. Expired strategies still need fresh approval.</p>
        <button onClick={() => location.reload()}>Check status</button>
      </main>
    );
  if (session?.role === "pending") return <WaitingForAccess login={session.login} />;
  if (!session)
    return (
      <main className="fe-loading dev-entry">
        <LockKeyhole size={30} />
        <h1>Private development workspace</h1>
        <p role="alert">{error || "An invited identity is required."}</p>
        <button onClick={() => location.reload()}>Reload</button>
      </main>
    );
  const activity = Object.keys(replies).find((id) => replies[id].status === "responding") || observedActivity;
  const agentBusy = owner
    ? !!agent?.runningIds.length || roleActivity.length > 0 || agent?.progress.state === "Scanning"
    : !!observedActivity;
  // Conversations the agent is on right now: queued or running agent tasks, role work (a coding run) and replies in flight.
  const busyIds = new Set<string>([
    ...(agent?.tasks ?? [])
      .filter((task) => task.requestId && (task.state === "active" || task.state === "queued"))
      .map((task) => task.requestId as string),
    ...roleActivity.map((item) => item.requestId),
    ...Object.keys(replies).filter((id) => replies[id].status === "responding" || replies[id].status === "queued"),
    ...(observedActivity ? [observedActivity] : []),
  ]);
  const threadsShown = route.view === "home" && route.pane === "chat" && (wide ? threadsPinned : threadsDrawer);
  const toggleThreads = () => (wide ? setThreadsPinned((value) => !value) : setThreadsDrawer((value) => !value));
  const showCockpit = route.view !== "settings",
    docked = showCockpit && roomy && cockpitOpen,
    split = route.view === "home" && route.pane === "chat" && !!route.inspect && !!detail && wide;
  const cockpit = (
    <Cockpit
      session={session}
      requests={requests}
      workload={workload}
      order={order}
      activity={activity}
      roleActivity={owner ? roleActivity[0] : undefined}
      roles={roles}
      agent={agent}
      onForbidden={forbidden}
      onOpen={open}
      onCancelTask={(id) => agentAction("/agent/tasks/" + id + "/cancel")}
      onArchive={async (id) => {
        await agentAction("/requests/" + id + "/archive");
        await refresh(session);
      }}
      onShift={(action, hours) => agentAction("/agent/shift", { action, hours })}
      onSchedule={(schedule: ShiftSchedule) => agentAction("/agent/schedule", schedule, "PUT")}
      onOrder={async (ids) => {
        try {
          setOrder(await api<string[]>("/intake-order", "PUT", { ids }));
        } catch (cause) {
          await refresh(session);
          throw cause;
        }
      }}
      onClose={() => (sheet ? setSheet(false) : setCockpitOpen(false))}
      onBoard={work}
      onSessions={() => {
        go({ view: "home", pane: "work", open: null, inspect: null });
        const url = new URL(location.href);
        url.searchParams.set("section", "sessions");
        history.replaceState(null, "", url);
      }}
    />
  );
  const requestWindow = detail && (
    <RequestWindow
      key={detail.request.id}
      detail={detail}
      session={session}
      roles={roles}
      workload={workload}
      onClose={() =>
        go({
          ...route,
          pane: route.inspect === "chat" ? "chat" : route.pane,
          open: route.pane === "chat" || route.inspect === "chat" ? route.open : null,
          inspect: null,
        })
      }
      onChat={() => go({ ...route, pane: "chat", inspect: null })}
      onRefresh={changed}
      onForbidden={forbidden}
    />
  );
  const chat = (
    <Conversation
      key={conversationKey}
      detail={detail}
      session={session}
      roles={roles}
      requests={requests}
      project={project}
      onProject={setProject}
      onCreate={createFromChat}
      onRefresh={refreshDetail}
      onOpen={open}
      onActivity={setObservedActivity}
      onAnswer={answer}
      onMessage={(note) => {
        if (live.current)
          setDetail((current) =>
            current?.request.id === note.requestId
              ? { ...current, messages: [...current.messages.filter((previous) => previous.id !== note.id), note] }
              : current,
          );
      }}
      replyState={route.open ? replies[route.open] : undefined}
      onDismissReplyError={() => {
        if (route.open) replyState(route.open);
      }}
      onMakeRequest={(text) => setNewRequest({ prefill: text.slice(0, 4000) })}
      onForbidden={forbidden}
    />
  );
  const listed = requests.filter((item) => item.status !== "home");
  let page =
    route.view === "projects" ? (
      <ProjectsView requests={listed} onOpen={open} />
    ) : route.view === "team" && owner ? (
      <TeamView people={people} session={session} onRefresh={changed} onForbidden={forbidden} />
    ) : route.view === "settings" ? (
      <SettingsView
        session={session}
        roles={roles}
        onForbidden={forbidden}
        theme={theme}
        onTheme={setTheme}
        railWide={railWide}
        onRail={() => setRailWide((value) => !value)}
        cockpitOpen={cockpitOpen}
        onCockpit={() => setCockpitOpen((value) => !value)}
      />
    ) : route.pane === "work" ? (
      detail && route.inspect ? (
        requestWindow
      ) : (
        <WorkView
          onRefresh={changed}
          onForbidden={forbidden}
          session={session}
          roles={roles}
          requests={listed}
          workload={workload}
          order={order}
          onOpen={open}
          onNew={() => setNewRequest({ prefill: "" })}
        />
      )
    ) : (
      chat
    );
  if (opening && (route.pane === "chat" || route.inspect)) page = <div className="fe-loading">Opening request…</div>;
  else if (route.view === "home" && route.pane === "chat")
    page = (
      <div className={split ? "fe-chat-and-work" : "dev-chat-only"}>
        {chat}
        {split && requestWindow}
      </div>
    );
  if (route.view === "home" && route.pane === "chat")
    page = (
      <div className={"dev-with-threads" + (threadsShown && !wide ? " drawer" : "")}>
        {threadsShown && (
          <>
            <Threads
              openId={route.open}
              version={threadsVersion}
              busyIds={busyIds}
              owner={owner}
              login={session.login}
              onForbidden={forbidden}
              onOpen={(id) => {
                setThreadsDrawer(false);
                go({ view: "home", pane: "chat", open: id, inspect: null });
              }}
              onNew={() => {
                setThreadsDrawer(false);
                go({ view: "home", pane: "chat", open: null, inspect: null, fresh: true });
              }}
              onClose={toggleThreads}
            />
            {!wide && (
              <button
                className="dev-threads-scrim"
                aria-label="Close conversations"
                onClick={() => setThreadsDrawer(false)}
              />
            )}
          </>
        )}
        {page}
      </div>
    );
  return (
    <div
      className={"fe-app dev-app" + (docked ? " with-cockpit" : "") + (railWide ? " rail-wide" : "")}
      style={{ "--fe-cockpit-w": cockpitWidth + "px" } as CSSProperties}
    >
      <aside className="fe-rail" aria-label="Navigation">
        <BrandMark className="fe-rail-mark" />
        <nav className="fe-rail-nav">
          <button
            className="fe-rail-button"
            data-tip="Workspace"
            aria-label="Workspace"
            aria-current={route.view === "home" ? "page" : undefined}
            onClick={() => go({ view: "home", pane: route.pane, open: null, inspect: null })}
          >
            <MessageSquareText size={21} />
            <span className="fe-rail-caption">Workspace</span>
          </button>
          <button
            className="fe-rail-button"
            data-tip="Search · Ctrl K"
            aria-label="Search workspace"
            onClick={() => setPalette(true)}
          >
            <Search size={21} />
            <span className="fe-rail-caption">Search</span>
          </button>
          <button
            className="fe-rail-button"
            data-tip="Projects"
            aria-label="Projects"
            aria-current={route.view === "projects" ? "page" : undefined}
            onClick={() => go({ view: "projects", pane: route.pane, open: null, inspect: null })}
          >
            <Folder size={21} />
            <span className="fe-rail-caption">Projects</span>
          </button>
          {owner && (
            <button
              className="fe-rail-button"
              data-tip="Team"
              aria-label="Team"
              aria-current={route.view === "team" ? "page" : undefined}
              onClick={() => go({ view: "team", pane: route.pane, open: null, inspect: null })}
            >
              <Users size={21} />
              <span className="fe-rail-caption">Team</span>
            </button>
          )}
        </nav>
        <button
          className="fe-rail-button"
          data-tip="New conversation"
          aria-label="New conversation"
          onClick={() => go({ view: "home", pane: "chat", open: null, inspect: null, fresh: true })}
        >
          <Plus size={21} />
          <span className="fe-rail-caption">New conversation</span>
        </button>
        <div className="fe-rail-foot">
          <button
            className="fe-rail-button"
            data-tip={railWide ? "Collapse navigation" : "Expand navigation"}
            aria-label={railWide ? "Collapse navigation" : "Expand navigation"}
            onClick={() => setRailWide((value) => !value)}
          >
            {railWide ? <PanelLeftClose size={19} /> : <PanelLeftOpen size={19} />}
          </button>
          <button
            className="fe-rail-button"
            aria-label="Account menu"
            aria-expanded={menu}
            onClick={() => setMenu((value) => !value)}
          >
            <span className="fe-avatar small">{initials(session.login)}</span>
            <span className="fe-rail-caption">{session.role}</span>
          </button>
          {menu && (
            <>
              <button className="fe-menu-scrim" aria-label="Close account menu" onClick={() => setMenu(false)} />
              <div className="fe-menu fe-account-menu">
                <div className="fe-menu-account">
                  <div>
                    <strong>{session.login}</strong>
                    <small>
                      {session.role} · {session.accessMode === "local" ? "Local preview" : "Invited access"}
                    </small>
                  </div>
                </div>
                <button onClick={() => go({ view: "settings", pane: route.pane, open: null, inspect: null })}>
                  <Settings size={16} />
                  Settings
                </button>
                <button
                  onClick={() => {
                    setShortcuts(true);
                    setMenu(false);
                  }}
                >
                  <Keyboard size={16} />
                  Keyboard shortcuts
                </button>
              </div>
            </>
          )}
        </div>
      </aside>
      <main className="fe-main">
        <header className="fe-topbar">
          {route.view === "home" && route.pane === "chat" && !threadsShown && (
            <button
              className="fe-icon-button"
              aria-label="Show conversations"
              title="Conversations"
              onClick={toggleThreads}
            >
              <MessagesSquare size={17} />
            </button>
          )}
          {route.view === "home" ? (
            <nav className="fe-tabs fe-mode" aria-label="Workspace mode">
              <button
                aria-pressed={route.pane === "chat"}
                title="Back to Home"
                onClick={() => go({ view: "home", pane: "chat", open: null, inspect: null })}
              >
                Chat
              </button>
              <button aria-pressed={route.pane === "work"} onClick={() => go({ ...route, pane: "work" })}>
                Work
              </button>
              {detail && (
                <ThreadName
                  key={detail.request.id}
                  request={detail.request}
                  canRename={!detail.home && (owner || detail.request.requester === session.login)}
                  onRenamed={(next) => {
                    setDetail((current) => (current?.request.id === next.id ? { ...current, request: next } : current));
                    setRequests((items) => items.map((item) => (item.id === next.id ? next : item)));
                    setThreadsVersion((value) => value + 1);
                  }}
                />
              )}
            </nav>
          ) : (
            <strong className="fe-topbar-title">{route.view[0].toUpperCase() + route.view.slice(1)}</strong>
          )}
          <span className="fe-topbar-spacer" />
          {detail && (
            <button
              className="fe-icon-button"
              aria-label={split ? "Expand work" : route.inspect ? "Show beside chat" : "Inspect request"}
              onClick={() =>
                go({
                  ...route,
                  pane: split ? "work" : route.inspect ? "chat" : wide ? route.pane : "work",
                  inspect: route.inspect || route.pane,
                })
              }
            >
              {split ? <Maximize2 size={16} /> : <Columns2 size={16} />}
            </button>
          )}
          <span className="fe-status-chip" title="Workspace agent · OpenClaw">
            <i className={"fe-dot " + (agentBusy ? "busy" : agent?.shift.on ? "live" : "off")} />
            {agentBusy
              ? "Agent · " +
                (roleActivity[0]?.label ??
                  (agent?.runningIds.length
                    ? "Writing a reply"
                    : agent?.progress.state === "Scanning"
                      ? "Checking sources"
                      : "Working…"))
              : agent?.shift.on
                ? "Agent · On shift"
                : "Agent · Off shift"}
          </span>
          {showCockpit && !docked && (
            <button
              className="fe-icon-button"
              aria-label="Show cockpit"
              onClick={() => (roomy ? setCockpitOpen(true) : setSheet(true))}
            >
              <PanelRightOpen size={17} />
            </button>
          )}
        </header>
        {error && (
          <div className="fe-banner" role="alert">
            <span>Couldn’t refresh: {error} Showing the last saved view.</span>
            <button onClick={() => void refresh(session).catch(fail)}>Retry</button>
          </div>
        )}
        <div className="fe-main-body">
          <ViewBoundary view={route.view + route.pane}>{page}</ViewBoundary>
        </div>
      </main>
      {docked && (
        <div className="fe-cockpit-dock">
          <div
            className="fe-cockpit-resize"
            role="separator"
            aria-orientation="vertical"
            aria-label="Resize cockpit"
            aria-valuemin={280}
            aria-valuemax={720}
            aria-valuenow={cockpitWidth}
            tabIndex={0}
            title="Drag to resize · double-click to reset"
            onPointerDown={(event) => {
              if (event.button !== 0) return;
              drag.current = { x: event.clientX, width: cockpitWidth };
              event.currentTarget.setPointerCapture(event.pointerId);
            }}
            onPointerMove={(event) => {
              if (drag.current) resize(drag.current.width + drag.current.x - event.clientX);
            }}
            onPointerUp={() => {
              drag.current = null;
            }}
            onPointerCancel={() => {
              drag.current = null;
            }}
            onDoubleClick={() => resize(320)}
            onKeyDown={(event) => {
              const next =
                event.key === "ArrowLeft"
                  ? cockpitWidth + 24
                  : event.key === "ArrowRight"
                    ? cockpitWidth - 24
                    : event.key === "Home"
                      ? 280
                      : event.key === "End"
                        ? 720
                        : null;
              if (next !== null) {
                event.preventDefault();
                resize(next);
              }
            }}
          />
          {cockpit}
        </div>
      )}
      {sheet && showCockpit && (
        <Dialog title="Cockpit" onClose={() => setSheet(false)} className="dev-cockpit-dialog">
          {cockpit}
        </Dialog>
      )}
      {palette && (
        <CommandPalette
          requests={listed}
          workload={workload}
          onOpen={open}
          onWork={work}
          onClose={() => setPalette(false)}
        />
      )}
      {newRequest && (
        <NewRequestDialog
          prefill={newRequest.prefill}
          project={detail?.request.project || project}
          onCreate={async (name, title, description, key) => {
            await create(name, title, description, key, true);
          }}
          onClose={() => setNewRequest(null)}
        />
      )}
      {shortcuts && (
        <Dialog title="Keyboard shortcuts" onClose={() => setShortcuts(false)}>
          <dl className="fe-dialog-body fe-shortcuts">
            <div>
              <dt>
                <kbd>Ctrl</kbd> <kbd>K</kbd>
              </dt>
              <dd>Search visible requests and approved work</dd>
            </div>
            <div>
              <dt>
                <kbd>Enter</kbd>
              </dt>
              <dd>Send a message</dd>
            </div>
            <div>
              <dt>
                <kbd>Shift</kbd> <kbd>Enter</kbd>
              </dt>
              <dd>New line in a message</dd>
            </div>
            <div>
              <dt>
                <kbd>Esc</kbd>
              </dt>
              <dd>Close a dialog or menu</dd>
            </div>
          </dl>
        </Dialog>
      )}
    </div>
  );
}
