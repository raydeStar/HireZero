import { useCallback, useEffect, useState } from "react";
import { ExternalLink } from "lucide-react";
import { ApiError, api, when } from "./api";

export type AgentSession = {
  id: string;
  app: "codex" | "claude";
  externalId: string;
  title: string;
  cwd: string | null;
  repository: string | null;
  branch: string | null;
  status: string;
  origin: string | null;
  prUrl: string | null;
  prNumber: number | null;
  updatedAt: string;
  flags: string[];
  lastEvent: string | null;
  lastEventAt: string | null;
};
type SessionList = { sessions: AgentSession[]; health: Record<string, string> };
type Turn = {
  id?: string;
  state: string;
  threadId?: string | null;
  model?: string | null;
  steps?: string[];
  summary?: string | null;
  error?: string | null;
};
const order = ["needs-you", "working", "waiting", "open", "idle", "ended"];
const labels: Record<string, string> = {
  "needs-you": "Needs you",
  working: "Working",
  waiting: "Waiting on you",
  open: "Open",
  idle: "Idle",
  ended: "Ended",
};
const rank = (session: AgentSession) => {
  const index = order.indexOf(session.status);
  return index < 0 ? order.length : index;
};
export const sortSessions = (items: AgentSession[]) =>
  [...items].sort((a, b) => rank(a) - rank(b) || b.updatedAt.localeCompare(a.updatedAt));
const running = (turn: Turn | null) => !!turn && ["queued", "running"].includes(turn.state);

export function useSessions(onForbidden: () => void, interval = 15000) {
  const [data, setData] = useState<SessionList | null>(null);
  const load = useCallback(async () => {
    try {
      setData(await api<SessionList>("/sessions"));
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 403) onForbidden();
    }
  }, [onForbidden]);
  useEffect(() => {
    void load();
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") void load();
    }, interval);
    return () => clearInterval(timer);
  }, [load, interval]);
  return data;
}

function Row({ session, compact }: { session: AgentSession; compact?: boolean }) {
  return (
    <li className={"dev-session " + session.status} title={session.cwd || undefined}>
      <span className={"dev-app-badge " + session.app}>{session.app === "codex" ? "Codex" : "Claude"}</span>
      <span className="dev-session-main">
        <span className="dev-session-title">
          {session.title}
          {session.flags.includes("possible-injection") && (
            <small
              className="dev-flag"
              title="This title contains text that looks like instructions; it is shown as data only."
            >
              {" "}
              flagged
            </small>
          )}
        </span>
        <small className="fe-muted">
          {[session.repository, session.branch].filter(Boolean).join(" · ") || "—"}
          {compact ? "" : " · " + when(session.updatedAt)}
        </small>
      </span>
      <span className={"fe-pill dev-session-status " + session.status}>{labels[session.status] || session.status}</span>
      {session.prUrl && (
        <a
          className="fe-icon-button"
          href={session.prUrl}
          target="_blank"
          rel="noopener noreferrer"
          aria-label={"Open PR #" + session.prNumber}
          onClick={(event) => event.stopPropagation()}
        >
          <ExternalLink size={13} />
        </a>
      )}
    </li>
  );
}

function TurnStatus({ turn, label, onStop }: { turn: Turn; label: string; onStop?: () => void }) {
  return (
    <div className="dev-steer-turn">
      <strong>{running(turn) ? label : "Last turn: " + turn.state}</strong>
      {!!turn.steps?.length && (
        <ol className="dev-pr-steps">
          {turn.steps.map((step, index) => (
            <li key={index}>{step}</li>
          ))}
        </ol>
      )}
      {turn.summary && <div className="dev-steer-answer">{turn.summary}</div>}
      {turn.error && <p className="fe-alert">{turn.error}</p>}
      {running(turn) && onStop && (
        <button className="fe-ghost" onClick={onStop}>
          Stop
        </button>
      )}
    </div>
  );
}

/** Cockpit: the few sessions that matter now. */
export function SessionsCockpit({ onForbidden, onAll }: { onForbidden: () => void; onAll: () => void }) {
  const data = useSessions(onForbidden);
  const recent = sortSessions(
    (data?.sessions ?? []).filter(
      (item) => item.status !== "ended" && Date.now() - Date.parse(item.updatedAt) < 3 * 86400000,
    ),
  ).slice(0, 5);
  return (
    <section className="fe-cockpit-section" aria-label="Your sessions">
      <h3>Your sessions {!!data && <span className="fe-count">{recent.length}</span>}</h3>
      {!data ? (
        <p className="fe-muted">Looking for Codex and Claude sessions…</p>
      ) : recent.length ? (
        <ol className="dev-sessions">
          {recent.map((session) => (
            <Row key={session.id} session={session} compact />
          ))}
        </ol>
      ) : (
        <p className="fe-muted">No Codex or Claude sessions in the last three days.</p>
      )}
      <button className="fe-link" onClick={onAll}>
        All sessions
      </button>
    </section>
  );
}

/** Send a follow-up into one idle session and watch HireZero's turn. The host enforces one driver at a time. */
function SteerPanel({ session, onForbidden }: { session: AgentSession; onForbidden: () => void }) {
  const [turn, setTurn] = useState<Turn | null>(null),
    [message, setMessage] = useState(""),
    [edits, setEdits] = useState(false),
    [network, setNetwork] = useState(false),
    [busy, setBusy] = useState(false),
    [error, setError] = useState("");
  const base = "/sessions/" + encodeURIComponent(session.id),
    appName = session.app === "codex" ? "Codex" : "Claude";
  const load = useCallback(async () => {
    try {
      setTurn(await api<Turn>(base + "/turn"));
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 403) onForbidden();
    }
  }, [base, onForbidden]);
  useEffect(() => {
    void load();
  }, [load]);
  useEffect(() => {
    if (!running(turn)) return;
    const timer = setInterval(() => void load(), 2500);
    return () => clearInterval(timer);
  }, [turn?.state, load]);
  async function send() {
    setBusy(true);
    setError("");
    try {
      setTurn(await api<Turn>(base + "/send", "POST", { message, allowEdits: edits, allowNetwork: edits && network }));
      setMessage("");
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="dev-steer">
      <p className="fe-muted">
        {session.cwd} · {appName}. HireZero only sends while it is idle. Your own messages in {appName} always win.
      </p>
      {turn && turn.state !== "none" && (
        <TurnStatus
          turn={turn}
          label="HireZero is sending…"
          onStop={() =>
            void api(base + "/turn/cancel", "POST", {})
              .then(load)
              .catch((cause) => setError((cause as Error).message))
          }
        />
      )}
      <textarea
        aria-label={"Message to " + session.title}
        rows={3}
        maxLength={4000}
        value={message}
        disabled={running(turn)}
        placeholder={"Follow up in “" + session.title + "”…"}
        onChange={(event) => setMessage(event.target.value)}
      />
      <div className="dev-shift-actions">
        <label className="dev-check">
          <input
            type="checkbox"
            checked={edits}
            onChange={(event) => {
              setEdits(event.target.checked);
              if (!event.target.checked) setNetwork(false);
            }}
          />
          Allow edits in this folder
        </label>
        {session.app === "codex" && (
          <label className="dev-check">
            <input
              type="checkbox"
              checked={network}
              disabled={!edits}
              onChange={(event) => setNetwork(event.target.checked)}
            />
            Allow network (installs, audits)
          </label>
        )}
        <button className="primary" disabled={busy || running(turn) || !message.trim()} onClick={() => void send()}>
          {busy ? "Sending…" : "Send"}
        </button>
      </div>
      {error && (
        <p className="fe-alert" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}

/** Hand a new job to Codex or Claude in a folder; it appears in that app so you can keep an eye on it there. */
function StartSession({ folders }: { folders: string[] }) {
  const [open, setOpen] = useState(false),
    [app, setApp] = useState<"codex" | "claude">("codex"),
    [folder, setFolder] = useState(""),
    [title, setTitle] = useState(""),
    [message, setMessage] = useState(""),
    [edits, setEdits] = useState(false),
    [network, setNetwork] = useState(false);
  const [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [started, setStarted] = useState<Turn | null>(null);
  useEffect(() => {
    if (!started?.id || !running(started)) return;
    const id = started.id;
    const timer = setInterval(
      () =>
        void api<Turn>("/sessions/runs/" + app + "/" + id)
          .then(setStarted)
          .catch(() => {}),
      2500,
    );
    return () => clearInterval(timer);
  }, [started?.id, started?.state, app]);
  async function start() {
    setBusy(true);
    setError("");
    try {
      setStarted(
        await api<Turn>("/sessions/start", "POST", {
          app,
          folder,
          title,
          message,
          allowEdits: edits,
          allowNetwork: edits && network,
        }),
      );
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy(false);
    }
  }
  if (!open) return <button onClick={() => setOpen(true)}>Start a session</button>;
  return (
    <section className="fe-section fe-stack dev-start-session">
      <h3>Start a session</h3>
      <label className="dev-check">
        App
        <select value={app} onChange={(event) => setApp(event.target.value as "codex" | "claude")}>
          <option value="codex">Codex (shows in the Codex app)</option>
          <option value="claude">Claude Code</option>
        </select>
      </label>
      <label>
        Project folder
        <input
          list="dev-session-folders"
          value={folder}
          maxLength={400}
          onChange={(event) => setFolder(event.target.value)}
          placeholder="C:\path\to\repository"
        />
      </label>
      <datalist id="dev-session-folders">
        {folders.map((value) => (
          <option key={value} value={value} />
        ))}
      </datalist>
      <label>
        Title
        <input
          value={title}
          maxLength={80}
          onChange={(event) => setTitle(event.target.value)}
          placeholder="What this session is for"
        />
      </label>
      <label>
        First message
        <textarea rows={3} maxLength={4000} value={message} onChange={(event) => setMessage(event.target.value)} />
      </label>
      <div className="dev-shift-actions">
        <label className="dev-check">
          <input
            type="checkbox"
            checked={edits}
            onChange={(event) => {
              setEdits(event.target.checked);
              if (!event.target.checked) setNetwork(false);
            }}
          />
          Allow edits in this folder
        </label>
        {app === "codex" && (
          <label className="dev-check">
            <input
              type="checkbox"
              checked={network}
              disabled={!edits}
              onChange={(event) => setNetwork(event.target.checked)}
            />
            Allow network
          </label>
        )}
        <button className="primary" disabled={busy || !folder.trim() || !message.trim()} onClick={() => void start()}>
          {busy ? "Starting…" : "Start"}
        </button>
        <button className="fe-ghost" onClick={() => setOpen(false)}>
          Close
        </button>
      </div>
      {error && (
        <p className="fe-alert" role="alert">
          {error}
        </p>
      )}
      {started && (
        <>
          <TurnStatus turn={started} label="Running…" />
          <p className="fe-muted">
            It appears under Sessions within a minute{app === "codex" ? ", and in the Codex app" : ""}.
          </p>
        </>
      )}
    </section>
  );
}

/** Work tab: every recent session across apps, metadata only, with follow-ups and hand-offs. */
export function SessionsView({ onForbidden }: { onForbidden: () => void }) {
  const data = useSessions(onForbidden, 10000),
    [app, setApp] = useState("all"),
    [status, setStatus] = useState("active"),
    [openId, setOpenId] = useState<string | null>(null);
  const items = sortSessions(
    (data?.sessions ?? []).filter(
      (item) =>
        (app === "all" || item.app === app) &&
        (status === "all" || (status === "active" ? !["idle", "ended"].includes(item.status) : item.status === status)),
    ),
  );
  const folders = [
    ...new Set((data?.sessions ?? []).map((item) => item.cwd).filter((value): value is string => !!value)),
  ].slice(0, 30);
  return (
    <div className="fe-stack">
      <p className="fe-muted">
        Your Codex threads and Claude Code sessions. HireZero keeps only titles, folders, branches and PR links, never
        transcript content, and titles are cleaned and checked for instructions before they are shown. Click one to
        follow up.
      </p>
      <div className="dev-shift-actions">
        <label className="dev-check">
          App
          <select value={app} onChange={(event) => setApp(event.target.value)}>
            <option value="all">All</option>
            <option value="codex">Codex</option>
            <option value="claude">Claude</option>
          </select>
        </label>
        <label className="dev-check">
          Show
          <select value={status} onChange={(event) => setStatus(event.target.value)}>
            <option value="active">Needs you / working / waiting</option>
            <option value="all">Everything recent</option>
            {order.map((value) => (
              <option key={value} value={value}>
                {labels[value]}
              </option>
            ))}
          </select>
        </label>
      </div>
      <StartSession folders={folders} />
      {data &&
        Object.entries(data.health).map(([name, text]) => (
          <p key={name} className="fe-muted">
            {name === "codex" ? "Codex" : "Claude"}: {text.replace(/ at \S+$/, "")}
          </p>
        ))}
      {!data ? (
        <p role="status">Loading sessions…</p>
      ) : items.length ? (
        <ol className="dev-sessions wide">
          {items.map((session) => (
            <li key={session.id} className="dev-session-wrap">
              <button
                className="dev-session-toggle"
                aria-expanded={openId === session.id}
                onClick={() => setOpenId((current) => (current === session.id ? null : session.id))}
              >
                <ol className="dev-sessions">
                  <Row session={session} />
                </ol>
              </button>
              {openId === session.id && <SteerPanel session={session} onForbidden={onForbidden} />}
            </li>
          ))}
        </ol>
      ) : (
        <p className="fe-empty">
          {status === "active"
            ? "Nothing is working or waiting on you. Live status needs the session hooks (tools/Install-SessionHooks.ps1); without them sessions show as idle. Choose Everything recent to see all."
            : "No sessions match."}
        </p>
      )}
    </div>
  );
}
