import { useEffect, useLayoutEffect, useRef, useState } from "react";
import {
  ArrowUp,
  BookOpen,
  Repeat,
  Check,
  Copy,
  ListChecks,
  LoaderCircle,
  MessageSquareText,
  RotateCcw,
  X,
} from "lucide-react";
import Markdown, { defaultUrlTransform } from "react-markdown";
import { tablesToLists } from "../components/markdownTables";
import type { RoleUi } from "../roles/types";
import {
  api,
  ApiError,
  when,
  type Detail,
  type Draft,
  type Message,
  type ReplyState,
  type Session,
  type WorkRequest,
  titleOf,
} from "./api";
import { TriageCard } from "./TriageCard";
import { copyText } from "../secureContext";
import { AccessCard } from "./Access";
import { ThreadActivity } from "./ThreadActivity";
import { HomeShiftProgress } from "./ShiftProgress";

interface ConversationProps {
  detail: Detail | null;
  session: Session;
  roles: RoleUi[];
  requests: WorkRequest[];
  project: string;
  onProject: (value: string) => void;
  onCreate: (text: string) => Promise<WorkRequest>;
  onRefresh: (id: string) => Promise<void>;
  onOpen: (id: string) => void;
  onActivity: (id: string | null) => void;
  onMakeRequest: (text: string) => void;
  onForbidden: () => void;
  onAnswer: (id: string, retry?: boolean) => Promise<void>;
  onMessage: (message: Message) => void;
  replyState?: ReplyState;
  onDismissReplyError: () => void;
}
/** Uses marketing-hire's conversation structure and QoL behavior with the development API. */
export function Conversation({
  detail,
  session,
  roles,
  requests,
  project,
  onProject,
  onCreate,
  onRefresh,
  onOpen,
  onActivity,
  onAnswer,
  onMessage,
  replyState,
  onDismissReplyError,
  onMakeRequest,
  onForbidden,
}: ConversationProps) {
  const [draft, setDraft] = useState<Draft>({ body: "", clientRequestId: crypto.randomUUID() });
  const [loaded, setLoaded] = useState(!detail),
    [sending, setSending] = useState(false),
    [answering, setAnswering] = useState(false);
  const [saving, setSaving] = useState(false),
    [liveStep, setLiveStep] = useState<string | null>(null);
  const [retryClock, setRetryClock] = useState(Date.now);
  const [outgoing, setOutgoing] = useState<Message | null>(null),
    [error, setError] = useState(""),
    [saveError, setSaveError] = useState(false),
    [copied, setCopied] = useState<string | null>(null);
  const [mention, setMention] = useState<{ query: string; start: number } | null>(null),
    [pick, setPick] = useState(0);
  const input = useRef<HTMLTextAreaElement>(null),
    scroll = useRef<HTMLDivElement>(null),
    content = useRef<HTMLDivElement>(null),
    stick = useRef(true),
    caret = useRef<number | null>(null);
  const saved = useRef<Draft | null>(null),
    attempt = useRef<Draft | null>(null),
    adopting = useRef(false);
  const latest = useRef({ draft, loaded, sending }),
    saveChain = useRef<Promise<unknown>>(Promise.resolve());
  latest.current = { draft, loaded, sending };
  const requestId = detail?.request.id;
  const queued = replyState?.status === "queued",
    waiting = replyState?.status === "responding" || queued || (answering && replyState?.status !== "failed"),
    replyError = error || replyState?.error || "";
  // While the agent finishes other work you can keep adding to the thread; the queued reply reads all of it.
  const blocked = waiting && !queued;
  const waitText = queued
    ? `Got it. I'm finishing “${replyState?.current}” first, then I'll answer this.`
    : replyState?.step || liveStep || "OpenClaw is preparing a response…";
  const retryIn = Math.max(0, Math.ceil(((replyState?.retryAt || 0) - retryClock) / 1000));
  useEffect(() => {
    setRetryClock(Date.now());
    if (!replyState?.retryAt) return;
    const timer = setInterval(() => setRetryClock(Date.now()), 1000);
    return () => clearInterval(timer);
  }, [replyState?.retryAt]);
  useEffect(() => {
    if (!requestId) {
      setLoaded(true);
      return;
    }
    let active = true;
    // The first send adopts its new request without replacing this conversation or its draft.
    if (adopting.current) {
      adopting.current = false;
      return;
    }
    api<Draft>("/requests/" + requestId + "/draft")
      .then((value) => {
        if (active) {
          const next = { body: value.body, clientRequestId: value.clientRequestId || crypto.randomUUID() };
          saved.current = next;
          setDraft(next);
          setLoaded(true);
        }
      })
      .catch((cause) => {
        if (active) {
          if (cause instanceof ApiError && cause.status === 403) onForbidden();
          setError("Your draft could not load. Retry before typing.");
        }
      });
    return () => {
      active = false;
    };
  }, [requestId]);
  useEffect(() => {
    if (!requestId || !loaded || sending || saved.current === draft) return;
    setSaving(true);
    const timer = setTimeout(() => {
      saveChain.current = saveChain.current
        .catch(() => {})
        .then(() => api("/requests/" + requestId + "/draft", "PUT", draft))
        .then(() => {
          saved.current = draft;
          setSaveError(false);
          setSaving(false);
        })
        .catch((cause) => {
          setSaving(false);
          setSaveError(true);
          if (cause instanceof ApiError && cause.status === 403) onForbidden();
        });
    }, 700);
    return () => clearTimeout(timer);
  }, [draft, requestId, loaded, sending]);
  useEffect(
    () => () => {
      const value = latest.current;
      if (requestId && value.loaded && !value.sending && saved.current !== value.draft) {
        void saveChain.current
          .catch(() => {})
          .then(() => api("/requests/" + requestId + "/draft", "PUT", value.draft))
          .catch(() => {});
      }
    },
    [requestId],
  );
  useEffect(() => {
    if (!requestId) return;
    let active = true;
    const check = async () => {
      try {
        const state = await api<{ running: boolean; executing: boolean; step?: string | null }>(
          "/requests/" + requestId + "/agent-status",
        );
        if (!active) return;
        setAnswering(state.running);
        setLiveStep(state.step ?? null);
        onActivity(state.executing ? requestId : null);
        if (!latest.current.sending) await onRefresh(requestId);
      } catch (cause) {
        if (!active) return;
        setAnswering(false);
        onActivity(null);
        if (cause instanceof ApiError && cause.status === 403) {
          onForbidden();
          return;
        }
      }
    };
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") void check();
    }, 4000);
    void check();
    return () => {
      active = false;
      clearInterval(timer);
      onActivity(null);
    };
  }, [requestId]);
  useEffect(() => {
    if (replyState?.status !== "responding") setAnswering(false);
  }, [replyState?.status]);
  useLayoutEffect(() => {
    const el = input.current;
    if (el) {
      el.style.height = "auto";
      el.style.height = Math.min(el.scrollHeight, 220) + "px";
      if (caret.current !== null) {
        el.focus();
        el.setSelectionRange(caret.current, caret.current);
        caret.current = null;
      }
    }
  }, [draft.body]);
  useLayoutEffect(() => {
    if (scroll.current && stick.current) scroll.current.scrollTop = scroll.current.scrollHeight;
  }, [detail?.messages.length, outgoing, waiting, sending]);
  useLayoutEffect(() => {
    if (!content.current) return;
    const observer = new ResizeObserver(() => {
      if (scroll.current && stick.current) scroll.current.scrollTop = scroll.current.scrollHeight;
    });
    observer.observe(content.current);
    return () => observer.disconnect();
  }, [requestId]);
  const matches = mention
    ? requests
        .filter(
          (item) =>
            item.id !== requestId &&
            (titleOf(item) + " " + item.title).toLowerCase().includes(mention.query.toLowerCase()),
        )
        .slice(0, 6)
    : [];
  function change(body: string, position = body.length) {
    setDraft({
      body,
      clientRequestId: attempt.current?.body === body ? attempt.current.clientRequestId : crypto.randomUUID(),
    });
    const match = /(^|\s)@([^@\n]{0,40})$/.exec(body.slice(0, position));
    setMention(match ? { query: match[2] || "", start: position - (match[2] || "").length - 1 } : null);
    setPick(0);
  }
  function tag(item: WorkRequest) {
    if (!mention) return;
    const text = `[${titleOf(item)}](request:${item.id}) `;
    const body = draft.body.slice(0, mention.start) + text + draft.body.slice(mention.start + mention.query.length + 1);
    caret.current = mention.start + text.length;
    change(body);
    setMention(null);
  }
  async function send(text = draft.body) {
    if (!text.trim() || sending || blocked || !loaded) return;
    setSending(true);
    setError("");
    setMention(null);
    stick.current = true;
    const value =
      attempt.current?.body === text ? attempt.current : { body: text, clientRequestId: draft.clientRequestId };
    attempt.current = value;
    setOutgoing({
      id: value.clientRequestId,
      requestId: requestId || "",
      author: session.login,
      body: text,
      createdAt: new Date().toISOString(),
    });
    try {
      let item = detail?.request;
      if (!item) {
        adopting.current = true;
        item = await onCreate(text);
      } else {
        onMessage(await api<Message>("/requests/" + item.id + "/messages", "POST", value));
      }
      attempt.current = null;
      setOutgoing(null);
      const empty = { body: "", clientRequestId: crypto.randomUUID() };
      saved.current = empty;
      setDraft(empty);
      setSaveError(false);
      setSaving(false);
      setSending(false);
      // Clear the persisted draft after any earlier autosave so reopening cannot restore sent text.
      saveChain.current = saveChain.current
        .catch(() => {})
        .then(() => api("/requests/" + item.id + "/draft", "PUT", empty))
        .catch((cause) => {
          setSaveError(true);
          if (cause instanceof ApiError && cause.status === 403) onForbidden();
        });
      await onAnswer(item.id);
    } catch (cause) {
      setError((cause as Error).message);
      setDraft(value);
      if (cause instanceof ApiError && cause.status === 403) onForbidden();
    } finally {
      setSending(false);
      input.current?.focus();
    }
  }
  // A thread opens with its request; Home has no opening request, just the running conversation.
  const messages: Message[] = detail
    ? [
        ...(detail.home
          ? []
          : [
              {
                id: detail.request.id + ":intake",
                requestId: detail.request.id,
                author: detail.request.requester,
                body: detail.request.description,
                createdAt: detail.request.createdAt,
              },
            ]),
        ...detail.messages,
      ]
    : [];
  const shown =
    outgoing &&
    !messages.some(
      (note) =>
        note.author === outgoing.author &&
        note.body === outgoing.body &&
        Date.parse(note.createdAt) >= Date.parse(outgoing.createdAt) - 1000,
    )
      ? [...messages, outgoing]
      : messages;
  const refs = [...draft.body.matchAll(/\[([^\]]+)\]\(request:([^\)]+)\)/g)].filter((match) =>
    requests.some((item) => item.id === match[2]),
  );
  return (
    <section
      className="fe-chat"
      aria-label={detail ? "Discussion for " + titleOf(detail.request) : "Conversation with Workspace agent"}
    >
      <div
        className="fe-chat-scroll"
        ref={scroll}
        onScroll={(event) => {
          const el = event.currentTarget;
          stick.current = el.scrollHeight - el.scrollTop - el.clientHeight < 80;
        }}
      >
        <div ref={content}>
          {detail?.home && session.role === "owner" && (
            <>
              <HomeShiftProgress />
              {!session.chatOnly &&
                roles.map((role) =>
                  role.homeActions ? <div key={role.id}>{role.homeActions({ session, onForbidden })}</div> : null,
                )}
            </>
          )}
          {!detail && !outgoing && (
            <div className="fe-hello">
              <h2>What should we work on?</h2>
              <p>
                Bring a bug, a feature, or a question. The Workspace agent helps clarify it; you decide when development
                starts.
              </p>
              <label className="dev-project-input">
                Project or repository
                <input
                  value={project}
                  maxLength={100}
                  onChange={(event) => onProject(event.target.value)}
                  placeholder="General development"
                />
              </label>
              <div className="fe-suggestions">
                {[
                  "Help me scope a bug fix.",
                  "Turn an idea into acceptance criteria.",
                  "What information is missing from this request?",
                ].map((text) => (
                  <button key={text} className="fe-suggestion" onClick={() => change(text)}>
                    <MessageSquareText size={17} />
                    <span>{text}</span>
                  </button>
                ))}
              </div>
              <RecentConversations requests={requests} onOpen={onOpen} />
            </div>
          )}
          {detail &&
            !messages.length &&
            !outgoing &&
            (detail.home ? (
              <div className="fe-hello">
                <h2>Home</h2>
                <p>
                  Your running conversation with the Workspace agent. Ask anything, ask how work is going, or hand it
                  work: real work, like fixing a PR, gets its own thread and reports back here.
                </p>
              </div>
            ) : (
              <p className="fe-muted">Discuss this request with the agent or add a clarification.</p>
            ))}
          <div className="fe-thread">
            {shown.map((note) => (
              <article
                className={
                  "fe-msg " +
                  (note.author === "workspace-agent" || note.author === "fix-loop" || note.visibility === "owner"
                    ? "assistant"
                    : "user")
                }
                key={note.id}
              >
                {note.author === "fix-loop" ? (
                  <span className="fe-avatar" aria-hidden="true">
                    <Repeat size={16} />
                  </span>
                ) : note.author === "wiki-update" ? (
                  <span className="fe-avatar" aria-hidden="true">
                    <BookOpen size={17} />
                  </span>
                ) : (
                  note.author === "workspace-agent" && (
                    <span className="fe-avatar" aria-hidden="true">
                      WA
                    </span>
                  )
                )}
                <div className="fe-msg-body">
                  <div className="fe-msg-meta">
                    <strong>
                      {note.author === "fix-loop"
                        ? "Fix loop · next round"
                        : note.author === "wiki-update"
                          ? "Wiki update · Private"
                          : note.author === "workspace-agent"
                            ? note.visibility === "owner"
                              ? "Workspace agent · Private"
                              : "Workspace agent · AI"
                            : note.author === session.login
                              ? "You"
                              : note.author}
                    </strong>
                    <time>{when(note.createdAt)}</time>
                    {note === outgoing && <small>{sending ? "Sending…" : attempt.current ? "Not sent" : "Sent"}</small>}
                  </div>
                  <div className="fe-msg-content">
                    <Markdown
                      urlTransform={(url) => (/^request:[a-z0-9]+$/.test(url) ? url : defaultUrlTransform(url))}
                      components={{
                        a: ({ href, children }) =>
                          href?.startsWith("request:") ? (
                            <button className="fe-link" onClick={() => onOpen(href.slice(8))}>
                              {children}
                            </button>
                          ) : (
                            <a href={href} target="_blank" rel="noopener noreferrer">
                              {children}
                            </a>
                          ),
                      }}
                    >
                      {tablesToLists(note.body)}
                    </Markdown>
                  </div>
                  {note.author === "workspace-agent" && (
                    <div className="fe-msg-actions">
                      <button
                        className="fe-ghost"
                        onClick={() =>
                          void copyText(note.body)
                            .then(() => setCopied(note.id))
                            .catch(() => setError("Copy failed. Select the reply to copy it."))
                        }
                      >
                        {copied === note.id ? <Check size={14} /> : <Copy size={14} />}{" "}
                        {copied === note.id ? "Copied" : "Copy"}
                      </button>
                      <button className="fe-ghost" onClick={() => onMakeRequest(note.body)}>
                        <ListChecks size={14} /> Make a request
                      </button>
                    </div>
                  )}
                </div>
              </article>
            ))}
            {(waiting || sending) && (
              <article className="fe-msg assistant">
                <span className="fe-avatar">WA</span>
                <div className="fe-msg-body">
                  <div className="fe-msg-meta">
                    <strong>Workspace agent · AI</strong>
                    <span>{waiting ? waitText : "Your message is being saved…"}</span>
                  </div>
                  <LoaderCircle size={18} className="fe-spin" aria-hidden="true" />
                </div>
              </article>
            )}
            {detail && session.role === "owner" && detail.access && (
              <AccessCard record={detail.access} onChanged={() => void onRefresh(detail.request.id)} />
            )}
            {detail && session.chatOnly && (
              <p className="fe-muted">
                Fresh messages receive text-only replies. Prior tasks and approvals remain inactive.
              </p>
            )}
            {detail && !session.chatOnly && session.role === "owner" && detail.agentUpgradeReviewRequired && (
              <div className="fe-alert">
                <p>
                  This conversation predates durable consent records. Review its history before allowing new agent work.
                  Prior workflow approvals remain invalid; request and approve a fresh proposal for further execution.
                </p>
                <button
                  className="primary"
                  onClick={() =>
                    void api("/requests/" + detail.request.id + "/allow-agent", "POST", {})
                      .then(() => onRefresh(detail.request.id))
                      .catch((cause) => setError((cause as Error).message))
                  }
                >
                  Allow new agent work after review
                </button>
              </div>
            )}
            {detail && session.role === "owner" && detail.triage && (
              <TriageCard
                requestId={detail.request.id}
                requester={detail.request.requester}
                triage={detail.triage}
                agentAllowed={detail.agentAllowed ?? false}
                onChanged={() => void onRefresh(detail.request.id)}
              />
            )}
            {detail &&
              session.role === "owner" &&
              !detail.triage &&
              detail.requesterRole === "visitor" &&
              detail.agentAllowed === false && (
                <div className="fe-alert dev-visitor-gate">
                  <p>
                    <strong>{detail.request.requester}</strong> is a visitor. No agent effort is spent on their requests
                    until you allow it.
                  </p>
                  <button
                    className="primary"
                    onClick={() =>
                      void api("/requests/" + detail.request.id + "/allow-agent", "POST", {})
                        .then(() => onRefresh(detail.request.id))
                        .catch(() => {})
                    }
                  >
                    Let the agent work on this
                  </button>
                </div>
              )}
            {detail &&
              roles.map((role) =>
                role.conversationActions ? (
                  <div key={role.id}>
                    {role.conversationActions({
                      requestId: detail.request.id,
                      messageCount: detail.messages.length,
                      session,
                      onForbidden,
                    })}
                  </div>
                ) : null,
              )}
          </div>
        </div>
      </div>
      <div className="fe-composer-wrap">
        {detail && session.role === "owner" && (
          <ThreadActivity key={detail.request.id} requestId={detail.request.id} onForbidden={onForbidden} />
        )}
        {(sending || waiting) && (
          <div className="fe-notice dev-chat-progress" role="status" aria-live="polite">
            <LoaderCircle size={18} className="fe-spin" aria-hidden="true" />
            <span>
              <strong>{sending ? "Sending your message…" : "Message sent"}</strong>
              {waiting ? waitText : "It will appear in this conversation."}
            </span>
          </div>
        )}
        {replyError && (
          <div className="fe-notice attn" role="alert">
            <span>
              <strong>{attempt.current ? "Message not confirmed" : "No reply came back"}</strong>
              {replyError}{" "}
              {attempt.current
                ? "Your words are preserved. Retrying uses the same request ID."
                : "Your saved message is still in the conversation."}
            </span>
            <button
              onClick={() => {
                setError("");
                if (attempt.current) void send(attempt.current.body);
                else if (requestId) void onAnswer(requestId, true);
              }}
              disabled={sending || waiting || retryIn > 0}
            >
              <RotateCcw size={14} /> {retryIn > 0 ? "Try again in " + retryIn + "s" : "Try again"}
            </button>
            <button
              className="fe-icon-button"
              aria-label="Dismiss error"
              onClick={() => {
                setError("");
                onDismissReplyError();
              }}
            >
              <X size={14} />
            </button>
          </div>
        )}
        {refs.length > 0 && (
          <div className="fe-chat-refs" aria-label="Tagged">
            {refs.map((match) => (
              <span className="fe-chat-ref" key={match[2]}>
                @{match[1]}
                <button aria-label={"Untag " + match[1]} onClick={() => change(draft.body.replace(match[0], ""))}>
                  <X size={12} />
                </button>
              </span>
            ))}
          </div>
        )}
        <form
          className="fe-composer"
          onSubmit={(event) => {
            event.preventDefault();
            void send();
          }}
        >
          <textarea
            ref={input}
            aria-label="Message to Workspace agent"
            placeholder="Message the Workspace agent…"
            rows={1}
            maxLength={4000}
            disabled={!loaded || sending}
            value={draft.body}
            onChange={(event) => change(event.target.value, event.target.selectionStart)}
            onKeyDown={(event) => {
              if (event.nativeEvent.isComposing) return;
              if (mention && matches.length) {
                if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                  event.preventDefault();
                  setPick((value) => (value + (event.key === "ArrowDown" ? 1 : -1) + matches.length) % matches.length);
                  return;
                }
                if (event.key === "Enter" || event.key === "Tab") {
                  event.preventDefault();
                  tag(matches[pick]);
                  return;
                }
              }
              if (event.key === "Escape") {
                setMention(null);
                return;
              }
              if (event.key === "Enter" && !event.shiftKey) {
                event.preventDefault();
                void send();
              }
            }}
          />
          {mention && matches.length > 0 && (
            <ul className="fe-mention-list" role="listbox" aria-label="Tag something">
              {matches.map((item, index) => (
                <li role="option" aria-selected={index === pick} key={item.id}>
                  <button
                    type="button"
                    onMouseDown={(event) => {
                      event.preventDefault();
                      tag(item);
                    }}
                  >
                    <span className="fe-mention-kind">Request</span>
                    {titleOf(item)}
                  </button>
                </li>
              ))}
            </ul>
          )}
          <button
            className="fe-send"
            type="submit"
            aria-label="Send"
            disabled={!loaded || !draft.body.trim() || sending || blocked}
          >
            {sending || blocked ? <LoaderCircle size={18} className="fe-spin" /> : <ArrowUp size={19} />}
          </button>
        </form>
        <p className="fe-composer-hint">
          {saveError
            ? "Draft could not save. Keep this page open."
            : requestId && draft.body
              ? saving
                ? "Saving draft… "
                : "Draft saved for your login. "
              : ""}
          Enter to send · Shift+Enter for a new line · @ to tag a visible request
        </p>
      </div>
    </section>
  );
}

const statusLabels: Record<string, string> = { queued: "awaiting review", ready: "in progress", archived: "archived" };
/** Past conversations on the empty chat: newest first, archived ones on request. */
function RecentConversations({ requests, onOpen }: { requests: WorkRequest[]; onOpen: (id: string) => void }) {
  const [archived, setArchived] = useState(false);
  const visible = [...requests]
    .filter((item) => archived || item.status !== "archived")
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
    .slice(0, 15);
  const hidden = requests.filter((item) => item.status === "archived").length;
  if (!requests.length) return null;
  return (
    <section className="fe-recent" aria-label="Recent conversations">
      <div className="fe-recent-head">
        <h3>Recent conversations</h3>
        {hidden > 0 && (
          <label className="dev-check">
            <input type="checkbox" checked={archived} onChange={(event) => setArchived(event.target.checked)} />
            Show archived ({hidden})
          </label>
        )}
      </div>
      {visible.length ? (
        <ol>
          {visible.map((item) => (
            <li key={item.id}>
              <button className="fe-recent-item" onClick={() => onOpen(item.id)}>
                <span className="fe-recent-title">{titleOf(item)}</span>
                <small className="fe-muted">
                  {item.project} · {statusLabels[item.status] ?? item.status} · {when(item.createdAt)}
                </small>
              </button>
            </li>
          ))}
        </ol>
      ) : (
        <p className="fe-muted">No open conversations. Tick Show archived to see older ones.</p>
      )}
    </section>
  );
}
