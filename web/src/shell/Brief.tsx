import { useCallback, useEffect, useState } from "react";
import { ExternalLink } from "lucide-react";
import { ApiError, api } from "./api";

type BriefItem = { kind: string; title: string; detail: string; requestId: string | null; url: string | null };
type Digest = {
  sessionId: string;
  app: "codex" | "claude";
  title: string;
  repository: string | null;
  branch: string | null;
  prUrl: string | null;
  status: string;
  firstAt: string;
  lastAt: string;
  turns: number;
  files: string[];
  commands: number;
  failedCommands: number;
  failedSamples: string[];
  tests: string[];
  endedWithQuestion: boolean;
  narrative: string | null;
  narrativeBy: string | null;
  flags: string[] | null;
};
type Settings = { codexNarratives: boolean; claudeNarratives: boolean };
type Brief = {
  day: string;
  timeZone: string;
  needsYou: BriefItem[];
  sessions: Digest[];
  inbox: { received: number; act: number; maybe: number; ignored: number; held: number };
  agent: { done: number; failed: number; proposed: number };
  settings: Settings;
};

const isoDay = (date: Date) =>
  date.getFullYear() +
  "-" +
  String(date.getMonth() + 1).padStart(2, "0") +
  "-" +
  String(date.getDate()).padStart(2, "0");
const clock = (value: string) => new Date(value).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" });
const plural = (count: number, word: string) => count + " " + word + (count === 1 ? "" : "s");

function DigestCard({ digest }: { digest: Digest }) {
  const facts = [
    plural(digest.turns, "ask"),
    digest.files.length ? plural(digest.files.length, "file") + " edited" : "",
    digest.commands
      ? plural(digest.commands, "command") + (digest.failedCommands ? " (" + digest.failedCommands + " failed)" : "")
      : "",
  ].filter(Boolean);
  return (
    <article className="fe-section dev-digest">
      <div className="fe-section-head">
        <span className={"dev-app-badge " + digest.app}>{digest.app === "codex" ? "Codex" : "Claude"}</span>
        <strong>{digest.title}</strong>
        {digest.prUrl && (
          <a
            className="fe-icon-button"
            href={digest.prUrl}
            target="_blank"
            rel="noopener noreferrer"
            aria-label="Open the PR"
          >
            <ExternalLink size={13} />
          </a>
        )}
      </div>
      <p className="fe-muted">
        {[digest.repository, digest.branch].filter(Boolean).join(" · ") || "—"} · {clock(digest.firstAt)}–
        {clock(digest.lastAt)} · {facts.join(" · ")}
      </p>
      {digest.narrative ? (
        <p>
          {digest.narrative}
          {digest.flags?.includes("possible-injection") && (
            <small
              className="dev-flag"
              title="This session contained text that looked like instructions; the summary treated it as data."
            >
              {" "}
              flagged
            </small>
          )}
        </p>
      ) : null}
      {digest.endedWithQuestion && <p className="dev-digest-question">Ended with a question for you.</p>}
      {!!digest.tests.length && <p className="fe-muted">Tests: {digest.tests.join(" · ")}</p>}
      {!!digest.failedSamples.length && !digest.tests.length && (
        <p className="fe-muted">Failed: {digest.failedSamples.join(" · ")}</p>
      )}
      {!!digest.files.length && (
        <details>
          <summary className="fe-muted">Files</summary>
          <ul className="dev-digest-files">
            {digest.files.map((file) => (
              <li key={file}>
                <code>{file}</code>
              </li>
            ))}
          </ul>
        </details>
      )}
    </article>
  );
}

/** Work tab: the day at a glance, built locally from records. Summaries are optional and written by the session's own app. */
export function BriefView({ onOpen, onForbidden }: { onOpen: (id: string) => void; onForbidden: () => void }) {
  const [day, setDay] = useState(isoDay(new Date())),
    [data, setData] = useState<{ brief: Brief; writing: boolean } | null>(null),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(false);
  const load = useCallback(async () => {
    try {
      setData(await api<{ brief: Brief; writing: boolean }>("/brief?day=" + day));
      setError("");
    } catch (cause) {
      if (cause instanceof ApiError && cause.status === 403) onForbidden();
      else setError((cause as Error).message);
    }
  }, [day, onForbidden]);
  useEffect(() => {
    setData(null);
    void load();
  }, [load]);
  useEffect(() => {
    if (!data?.writing) return;
    const timer = setInterval(() => void load(), 4000);
    return () => clearInterval(timer);
  }, [data?.writing, load]);
  async function save(settings: Settings) {
    try {
      await api("/brief/settings", "PUT", settings);
      await load();
    } catch (cause) {
      setError((cause as Error).message);
    }
  }
  const [saved, setSaved] = useState("");
  async function remember() {
    setBusy(true);
    setError("");
    try {
      await api("/brief/memory", "POST", { day });
      setSaved(day);
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy(false);
    }
  }
  async function narrate() {
    setBusy(true);
    setError("");
    try {
      await api("/brief/narratives", "POST", { day });
      await load();
    } catch (cause) {
      setError((cause as Error).message);
    } finally {
      setBusy(false);
    }
  }
  const oldest = isoDay(new Date(Date.now() - 13 * 86400000)),
    today = isoDay(new Date());
  const brief = data?.brief,
    settings = brief?.settings,
    enabled = !!settings && (settings.codexNarratives || settings.claudeNarratives);
  const missing =
    brief?.sessions.filter(
      (item) => !item.narrative && (item.app === "codex" ? settings?.codexNarratives : settings?.claudeNarratives),
    ).length ?? 0;
  return (
    <div className="fe-stack dev-brief">
      <div className="dev-shift-actions">
        <label className="dev-check">
          Day
          <input
            type="date"
            value={day}
            min={oldest}
            max={today}
            onChange={(event) => event.target.value && setDay(event.target.value)}
          />
        </label>
        <button className="fe-ghost" onClick={() => void load()}>
          Refresh
        </button>
        <button
          className="fe-ghost"
          disabled={busy}
          title="Write this day as a note in the agent's memory. Yesterday is saved automatically."
          onClick={() => void remember()}
        >
          {saved === day ? "Saved to memory" : "Save day to memory"}
        </button>
      </div>
      {error && (
        <p className="fe-alert" role="alert">
          {error}
        </p>
      )}
      {!brief ? (
        <p role="status">Putting the day together…</p>
      ) : (
        <>
          <section className="fe-section">
            <h3>Needs you {!!brief.needsYou.length && <span className="fe-count">{brief.needsYou.length}</span>}</h3>
            {brief.needsYou.length ? (
              <ul className="dev-brief-needs">
                {brief.needsYou.map((item, index) => (
                  <li key={index}>
                    <strong>{item.title}</strong> <span className="fe-muted">{item.detail}</span>
                    {item.requestId && (
                      <button className="fe-link" onClick={() => onOpen(item.requestId!)}>
                        Open →
                      </button>
                    )}
                    {item.url && (
                      <a
                        className="fe-icon-button"
                        href={item.url}
                        target="_blank"
                        rel="noopener noreferrer"
                        aria-label={"Open " + item.title}
                      >
                        <ExternalLink size={13} />
                      </a>
                    )}
                  </li>
                ))}
              </ul>
            ) : (
              <p className="fe-muted">Nothing is waiting on you.</p>
            )}
          </section>
          <section className="fe-stack">
            <h3>Sessions {!!brief.sessions.length && <span className="fe-count">{brief.sessions.length}</span>}</h3>
            {brief.sessions.length ? (
              brief.sessions.map((digest) => <DigestCard key={digest.sessionId} digest={digest} />)
            ) : (
              <p className="fe-muted">No Codex or Claude activity on this day.</p>
            )}
          </section>
          <section className="fe-section">
            <h3>Came in</h3>
            <p className="fe-muted">
              {brief.inbox.received
                ? `${plural(brief.inbox.received, "message")}: ${brief.inbox.act} to act on, ${brief.inbox.maybe} maybe, ${brief.inbox.ignored} ignored${brief.inbox.held ? `, ${brief.inbox.held} held` : ""}.`
                : "Nothing came in."}
            </p>
            <h3>Agent</h3>
            <p className="fe-muted">
              {brief.agent.done || brief.agent.failed
                ? `${plural(brief.agent.done, "task")} done${brief.agent.failed ? `, ${brief.agent.failed} didn't finish` : ""}.`
                : "No agent tasks finished."}
            </p>
          </section>
          <section className="fe-section fe-stack">
            <h3>Summaries</h3>
            <p className="fe-muted">
              Off by default. When on, each session is summarized by the app that ran it (Codex by Codex, Claude by
              Claude) with no tools. Only the short summary is kept; session text is cleaned and treated as data, never
              instructions.
            </p>
            <div className="dev-shift-actions">
              <label className="dev-check">
                <input
                  type="checkbox"
                  checked={settings!.codexNarratives}
                  onChange={(event) => void save({ ...settings!, codexNarratives: event.target.checked })}
                />
                Codex sessions
              </label>
              <label className="dev-check">
                <input
                  type="checkbox"
                  checked={settings!.claudeNarratives}
                  onChange={(event) => void save({ ...settings!, claudeNarratives: event.target.checked })}
                />
                Claude sessions
              </label>
              <button
                className="primary"
                disabled={!enabled || busy || data!.writing || !missing}
                onClick={() => void narrate()}
              >
                {data!.writing
                  ? "Writing summaries…"
                  : missing
                    ? "Summarize " + plural(missing, "session")
                    : "All summarized"}
              </button>
            </div>
          </section>
        </>
      )}
    </div>
  );
}
