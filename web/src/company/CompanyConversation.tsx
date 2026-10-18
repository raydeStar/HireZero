import { Fragment, useEffect, useRef, useState } from "react";
import { ArrowUp, MessageSquareText } from "lucide-react";
import { api } from "../lib/api";
import { skillInput } from "../roles/company";
import { EmployeeActions } from "./EmployeeActions";
import type { Employee, Event, Snapshot, ThreadDetail, Work } from "./api";
import { ConversationJob } from "./ConversationJob";
import { MeasuredJob } from "./MeasuredJob";

export type ConversationDraft = {
  body: string;
  skill: string;
  brief: Record<string, string>;
  evidence: string[];
  recipient: string;
  delivery?: string;
  allowance?: string;
};
const emptyDraft = (): ConversationDraft => ({
  body: "",
  skill: "chat",
  brief: {},
  evidence: [],
  recipient: "",
  delivery: "reply",
  allowance: "",
});
type Props = {
  snapshot: Snapshot;
  events: Event[];
  employee?: Employee;
  company: boolean;
  thread: string;
  detail: ThreadDetail | null;
  busy: boolean;
  drafts: Map<string, ConversationDraft>;
  updateDraft: (scope: string, update: (current: ConversationDraft | undefined) => ConversationDraft) => void;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  refresh: () => Promise<void>;
  open: (employeeId: string, threadId: string) => void;
  createThread: () => Promise<void>;
};

export function CompanyConversation({
  snapshot,
  events,
  employee,
  company,
  thread,
  detail,
  busy,
  drafts,
  updateDraft,
  run,
  refresh,
  open,
  createThread,
}: Props) {
  const scope = company ? "company" : (employee?.id || "missing") + ":" + thread,
    draft = drafts.get(scope) ?? emptyDraft(),
    d = snapshot.data;
  const [notice, setNotice] = useState(""),
    [localError, setLocalError] = useState(""),
    [companyDetail, setCompanyDetail] = useState<ThreadDetail | null>(null);
  const sending = useRef(false),
    scroll = useRef<HTMLDivElement>(null),
    stick = useRef(true),
    input = useRef<HTMLTextAreaElement>(null),
    mounted = useRef(true),
    companyRequest = useRef(0);
  const target = company ? d.employees.find((e) => e.id === draft.recipient) : employee,
    role = d.roles.find((r) => r.id === employee?.roleId),
    structured = company ? undefined : skillInput(role, draft.skill);
  const companyThread = company
    ? d.threads.find((t) => t.employeeId === target?.id && t.scope === "company")
    : undefined;
  const privateReady =
    !!employee &&
    detail?.thread.id === thread &&
    detail.thread.employeeId === employee.id &&
    (detail.thread.scope ?? "employee") === "employee";
  const companyReady =
    companyDetail?.thread.id === companyThread?.id &&
    companyDetail?.thread.employeeId === target?.id &&
    companyDetail?.thread.scope === "company";
  const shown = company ? (companyReady ? companyDetail : null) : privateReady ? detail : null,
    shownThread = company ? companyThread?.id : thread;
  const tasks = d.tasks.filter((t) => t.employeeId === target?.id && t.threadId === shownThread),
    artifacts = d.artifacts.filter((a) => tasks.some((t) => t.id === a.taskId));
  const messages =
      shown?.messages.filter((m) => m.author === "owner" || !artifacts.some((a) => a.body === m.body)) ?? [],
    orphaned = tasks.filter((t) => !messages.some((m) => m.sequence === t.watermark));
  const eligible =
      !!snapshot.capabilities.conversationalReplies &&
      target?.state === "active" &&
      !!d.roles.find((r) => r.id === target.roleId)?.skills.includes("chat") &&
      (company || draft.skill === "chat"),
    reply = eligible && draft.delivery !== "save";
  const held = d.measured?.some((a) =>
    ["provider-accounting-unconfirmed", "provider-termination-unconfirmed"].includes(a.error || ""),
  );
  const pending = d.tasks.some(
    (t) =>
      t.kind === "conversation" &&
      t.employeeId === target?.id &&
      d.measured?.some((a) => a.taskId === t.id && ["proposed", "dispatched"].includes(a.job.state)),
  );
  function update(value: Partial<ConversationDraft>) {
    updateDraft(scope, (previous) => ({ ...(previous ?? emptyDraft()), ...value }));
  }
  useEffect(() => {
    mounted.current = true;
    return () => {
      mounted.current = false;
    };
  }, []);
  useEffect(() => {
    const request = ++companyRequest.current;
    setCompanyDetail(null);
    if (company && companyThread)
      void api<ThreadDetail>("/company/employees/" + companyThread.employeeId + "/threads/" + companyThread.id)
        .then((next) => {
          if (mounted.current && request === companyRequest.current) setCompanyDetail(next);
        })
        .catch((e) => {
          if (mounted.current && request === companyRequest.current) setLocalError(e.message);
        });
  }, [company, companyThread?.id, d.cursor]);
  useEffect(() => {
    if (scroll.current && stick.current) scroll.current.scrollTop = scroll.current.scrollHeight;
  }, [messages.length, tasks.length, artifacts.length, events.length, !!shown]);
  async function send() {
    if (sending.current || busy || (!company && (!privateReady || employee?.state !== "active"))) return;
    const sent = draft,
      sentInput = company ? undefined : skillInput(role, sent.skill),
      body = sentInput ? JSON.stringify(sent.brief) : sent.body;
    if (!body.trim()) return;
    if (sentInput && sentInput.fields.some((field) => !sent.brief[field]?.trim())) {
      setLocalError("Complete every " + sentInput.label.toLowerCase() + " field.");
      return;
    }
    if (reply && (held || pending)) {
      setLocalError(
        held
          ? "Replies are paused for accounting or runtime reconciliation. No automatic retry is available."
          : "Finish or cancel this employee’s prepared reply before preparing another.",
      );
      return;
    }
    if (reply && !(Number(sent.allowance) > 0 && Number(sent.allowance) <= 1000)) {
      setLocalError(
        "Enter a positive intended existing-credit allowance, at most 1,000. It is not a provider-enforced cap.",
      );
      return;
    }
    sending.current = true;
    setLocalError("");
    setNotice("");
    try {
      let result: unknown;
      if (reply)
        result = await run("conversation.prepare", {
          employeeId: target!.id,
          threadId: company ? companyThread?.id : thread,
          scope: company ? "company" : "employee",
          body,
          creditAllowance: Number(sent.allowance),
          evidence: company ? [] : sent.evidence,
        });
      else if (company) result = await run("company.message", { body });
      else
        result = await run("message.send", {
          employeeId: employee!.id,
          threadId: thread,
          body,
          skill: sent.skill === "chat" ? undefined : sent.skill,
          evidence: sent.evidence,
        });
      if (!result) return;
      updateDraft(scope, (current) =>
        JSON.stringify(current ?? sent) === JSON.stringify(sent) ? { ...sent, body: "", brief: {} } : (current ?? sent),
      );
      if (mounted.current) {
        setNotice(
          reply
            ? "Message prepared without a model call. Review its context, then Send & authorize one reply."
            : company || sent.skill === "chat"
              ? "Message saved only. No AI reply or autonomous work was authorized."
              : "Work saved. Its execution requires separate approval.",
        );
        input.current?.focus();
      }
    } finally {
      sending.current = false;
    }
  }
  const job = (task: Work) =>
    task.kind === "conversation" ? (
      <ReplyTurn key={task.id} task={task} snapshot={snapshot} busy={busy} run={run} refresh={refresh} />
    ) : (
      <ConversationJob
        key={task.id}
        task={task}
        snapshot={snapshot}
        busy={busy}
        run={run}
        refresh={refresh}
        open={open}
      />
    );
  return (
    <section
      className="fe-chat co-conversation"
      aria-label={company ? "Company conversation" : "Private employee conversation"}
    >
      <div className="co-conversation-context">
        <p>
          {company
            ? "Company context only. Select an employee to reply here; private employee notes, department facts and private conversations are excluded. The human owner retains final authority."
            : employee?.responsibility || "Choose an employee conversation from the sidebar."}
        </p>
        <small>
          {snapshot.capabilities.conversationalReplies
            ? "One text-only Luna reply can be prepared, reviewed and explicitly authorized. Each turn needs its own approval; no tools, coding or autonomous follow-up."
            : "Live replies unavailable. Messages remain saved locally; eligible draft jobs retain separate approval."}
        </small>
        {employee && (
          <>
            <div className="co-actions">
              <label>
                Conversation
                <select
                  aria-label="Employee conversation"
                  value={thread}
                  onChange={(e) => open(employee.id, e.target.value)}
                >
                  {d.threads
                    .filter((t) => t.employeeId === employee.id && (t.scope ?? "employee") === "employee")
                    .map((t) => (
                      <option key={t.id} value={t.id}>
                        {t.title}
                      </option>
                    ))}
                </select>
              </label>
              <button disabled={busy || employee.state === "retired"} onClick={() => void createThread()}>
                New conversation
              </button>
            </div>
            {privateReady && <PrivateContext employee={employee} detail={detail!} busy={busy} run={run} />}
          </>
        )}
      </div>
      <div
        className="fe-chat-scroll"
        ref={scroll}
        onScroll={(e) => {
          const el = e.currentTarget;
          stick.current = el.scrollHeight - el.scrollTop - el.clientHeight < 100;
        }}
      >
        <div className="fe-thread">
          {company && (
            <>
              {events
                .filter((e) => e.type === "owner.note")
                .map((event) => (
                  <article key={event.sequence} className="fe-msg user">
                    <div className="fe-msg-body">
                      <div className="fe-msg-meta">
                        <strong>You · saved company note</strong>
                      </div>
                      <div className="fe-msg-content">
                        <p>{event.text}</p>
                      </div>
                    </div>
                  </article>
                ))}
              <details className="co-company-updates">
                <summary>Company updates ({events.filter((e) => e.type !== "owner.note").length})</summary>
                {events
                  .filter((e) => e.type !== "owner.note")
                  .map((event) => (
                    <article key={event.sequence}>
                      <small>{new Date(event.at).toLocaleString()}</small>
                      <p>{event.text}</p>
                    </article>
                  ))}
              </details>
              {target && (
                <p className="co-muted">
                  Company conversation with {target.name}. Only this company thread enters its reviewed reply.
                </p>
              )}
            </>
          )}
          {!company && !privateReady ? (
            <p role="status">
              {employee && thread
                ? "Opening this conversation."
                : "Choose a conversation or create one for this employee."}
            </p>
          ) : (
            <>
              {!messages.length && !tasks.length && (
                <div className="fe-hello">
                  <MessageSquareText size={24} />
                  <h2>{company ? "What should we work on?" : "Conversation with " + employee!.name}</h2>
                  <p>
                    {snapshot.capabilities.conversationalReplies
                      ? company
                        ? "Save a company note, or select an employee to ask a question. Review the context before authorizing one reply."
                        : "Ask a question below, review the context, then authorize one reply. You can also save a note without requesting a reply."
                      : company
                        ? "Save a company note below. AI replies are unavailable."
                        : "Save a note or brief below to prepare for later. AI replies are unavailable; saving sends no model request."}
                  </p>
                </div>
              )}
              {messages.map((message) => (
                <Fragment key={message.id}>
                  <article
                    className={"fe-msg " + (message.author === "owner" ? "user" : "assistant")}
                    data-message-id={message.id}
                  >
                    <div className="fe-msg-body">
                      <div className="fe-msg-meta">
                        <strong>{message.author === "owner" ? "You" : target?.name + " · AI reply"}</strong>
                      </div>
                      <div className="fe-msg-content">
                        <p>{message.body}</p>
                      </div>
                      {message.author !== "owner" && (
                        <small>Informational reply. Work and decisions still need owner approval.</small>
                      )}
                    </div>
                  </article>
                  {tasks.filter((t) => t.watermark === message.sequence).map(job)}
                </Fragment>
              ))}
              {orphaned.map(job)}
            </>
          )}
        </div>
      </div>
      <div className="fe-composer-wrap co-conversation-composer">
        {localError && <p role="alert">{localError}</p>}
        {notice && (
          <p role="status" className="co-send-status">
            {notice}
          </p>
        )}
        <form
          onSubmit={(e) => {
            e.preventDefault();
            void send();
          }}
        >
          {company ? (
            <label>
              Reply as
              <select value={draft.recipient} onChange={(e) => update({ recipient: e.target.value })}>
                <option value="">Save owner note only</option>
                {d.employees
                  .filter((e) => e.state === "active")
                  .map((e) => (
                    <option key={e.id} value={e.id}>
                      {e.name} · company context
                    </option>
                  ))}
              </select>
            </label>
          ) : (
            <label>
              Work type
              <select
                value={draft.skill}
                disabled={!privateReady || busy}
                onChange={(e) => update({ skill: e.target.value })}
              >
                {role?.skills
                  .filter((s) => s !== "meeting-turn" && s !== "meeting-chair")
                  .map((s) => (
                    <option key={s} value={s}>
                      {s === "chat" ? "Conversation" : s}
                    </option>
                  ))}
              </select>
            </label>
          )}
          {eligible && (
            <label>
              Message action
              <select value={draft.delivery ?? "reply"} onChange={(e) => update({ delivery: e.target.value })}>
                <option value="reply">Prepare one reply for review</option>
                <option value="save">Save message only</option>
              </select>
            </label>
          )}
          {reply && (
            <>
              <label>
                Intended reply credit allowance
                <input
                  type="number"
                  min="0.01"
                  max="1000"
                  step="0.01"
                  required
                  value={draft.allowance ?? ""}
                  onChange={(e) => update({ allowance: e.target.value })}
                />
              </label>
              <small className="co-reply-risk">
                Preparing sends no model request. The final Send authorizes one native turn. Token, time and credit
                stops are soft; the provider may exceed the intended allowance. Included usage must be available; no
                purchases or API fallback.
              </small>
            </>
          )}
          {structured ? (
            <details open className="co-brief-composer">
              <summary>{structured.label}</summary>
              <structured.Editor value={draft.brief} onChange={(brief) => update({ brief })} />
            </details>
          ) : null}
          {!company && employee && (
            <EmployeeActions
              role={role}
              employeeId={employee.id}
              threadId={thread}
              disabled={busy || employee.state !== "active"}
              refresh={refresh}
            />
          )}
          {!company && (
            <details className="co-evidence">
              <summary>Attach approved evidence ({draft.evidence.length})</summary>
              {d.artifacts
                .filter((a) => a.state === "approved")
                .map((a) => (
                  <label className="co-check" key={a.id}>
                    <input
                      type="checkbox"
                      checked={draft.evidence.includes(a.id)}
                      onChange={(e) =>
                        update({
                          evidence: e.target.checked
                            ? [...draft.evidence, a.id]
                            : draft.evidence.filter((id) => id !== a.id),
                        })
                      }
                    />
                    {d.employees.find((e) => e.id === a.employeeId)?.name}: {a.body.slice(0, 80)}
                  </label>
                ))}
            </details>
          )}
          <div className="fe-composer">
            {!structured && (
              <textarea
                ref={input}
                aria-label={company ? "Company message" : "Message to " + (employee?.name || "employee")}
                rows={2}
                maxLength={4000}
                required
                value={draft.body}
                disabled={busy || (!company && (!privateReady || employee?.state !== "active"))}
                onChange={(e) => update({ body: e.target.value })}
                onKeyDown={(e) => {
                  if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) {
                    e.preventDefault();
                    void send();
                  }
                }}
                placeholder={company ? "Message your company." : "Message " + (employee?.name || "this employee") + "."}
              />
            )}
            <button
              className="fe-send"
              type="submit"
              aria-label={
                reply
                  ? "Prepare reply for review"
                  : company
                    ? "Save company note"
                    : draft.skill === "chat"
                      ? "Save message"
                      : "Assign work"
              }
              title={
                reply
                  ? "Prepare reply for review without a model call"
                  : draft.skill === "chat"
                    ? "Save message only"
                    : "Assign work"
              }
              disabled={
                busy ||
                (!company && (!privateReady || employee?.state !== "active")) ||
                (!structured && !draft.body.trim())
              }
            >
              <ArrowUp size={19} />
            </button>
          </div>
        </form>
        <p className="fe-composer-hint">
          {reply
            ? "Prepare → inspect context → Send & authorize one reply"
            : "Enter to save · Shift+Enter for a new line · No reply is authorized by saving"}
        </p>
      </div>
    </section>
  );
}

function ReplyTurn({
  task,
  snapshot,
  busy,
  run,
  refresh,
}: {
  task: Work;
  snapshot: Snapshot;
  busy: boolean;
  run: Props["run"];
  refresh: Props["refresh"];
}) {
  const entry = snapshot.data.measured?.find((a) => a.taskId === task.id),
    thread = snapshot.data.threads.find((t) => t.id === task.threadId),
    employee = snapshot.data.employees.find((e) => e.id === task.employeeId);
  const active = entry && ["proposed", "dispatched"].includes(entry.job.state),
    contents = (
      <MeasuredJob
        conversational
        task={task}
        entry={entry}
        available={!!snapshot.capabilities.conversationalReplies}
        busy={busy}
        prepare={run}
        refresh={refresh}
      />
    );
  return (
    <article className="co-reply-turn" data-reply-task={task.id}>
      <small>
        {employee?.name} · {thread?.scope === "company" ? "Company" : "Private employee"} context · one informational
        reply
      </small>
      {active && (
        <p className="co-muted">
          This reply uses its attached message and the saved context. Later messages are excluded until a new turn is
          prepared.
        </p>
      )}
      {active ? (
        contents
      ) : (
        <details>
          <summary>Reply status and measured usage · {task.state}</summary>
          {contents}
        </details>
      )}
      {entry?.job.state === "dispatched" && (
        <p role="status" aria-live="polite">
          Reply pending. Waiting for complete text and confirmed usage; no partial answer is published.
        </p>
      )}
      {["queued", "blocked", "running", "unknown", "failed"].includes(task.state) && (
        <button disabled={busy} onClick={() => void run("task.cancel", { id: task.id, expectedVersion: task.version })}>
          {entry?.job.state === "dispatched" ? "Stop reply" : "Cancel reply"}
        </button>
      )}
      {entry?.error && (
        <p role="alert">
          Reply stopped. {entry.error}. Accounting or runtime reconciliation may be required; this turn cannot be
          replayed.
        </p>
      )}
    </article>
  );
}

function PrivateContext({
  employee,
  detail,
  busy,
  run,
}: {
  employee: Employee;
  detail: ThreadDetail;
  busy: boolean;
  run: Props["run"];
}) {
  const [notes, setNotes] = useState(detail.notes.body),
    [version, setVersion] = useState(detail.notes.version),
    [dirty, setDirty] = useState(false);
  useEffect(() => {
    if (!dirty) {
      setNotes(detail.notes.body);
      setVersion(detail.notes.version);
    }
  }, [detail.notes.body, detail.notes.version, dirty]);
  return (
    <details className="co-private-context">
      <summary>Conversation context and employee settings</summary>
      <p>
        Only this employee's approved department facts, private notes, bounded conversation history and selected
        approved artifacts can enter a task. Review the exact packet before authorizing a manual job.
      </p>
      <form
        onSubmit={async (e) => {
          e.preventDefault();
          const saved = await run("notes.save", { employeeId: employee.id, body: notes, expectedVersion: version });
          if (saved) setDirty(false);
        }}
      >
        <label>
          Private notes
          <textarea
            value={notes}
            disabled={busy || employee.state === "retired"}
            onChange={(e) => {
              setDirty(true);
              setNotes(e.target.value);
            }}
          />
        </label>
        <button disabled={busy || !dirty || employee.state === "retired"} type="submit">
          Save private notes
        </button>
      </form>
      <small>
        {employee.state} · {employee.roleId}. Unsaved note edits stay only in this open view.
      </small>
      {employee.state !== "retired" && (
        <form
          key={employee.id + ":" + employee.version}
          onSubmit={async (e) => {
            e.preventDefault();
            const values = new FormData(e.currentTarget);
            await run("employee.update", {
              id: employee.id,
              expectedVersion: employee.version,
              name: String(values.get("name")),
              responsibility: String(values.get("responsibility")),
            });
          }}
        >
          <label>
            Name
            <input name="name" required defaultValue={employee.name} />
          </label>
          <label>
            Responsibility
            <textarea name="responsibility" required defaultValue={employee.responsibility} />
          </label>
          <button disabled={busy} type="submit">
            Update employee
          </button>
        </form>
      )}
      <div className="co-actions">
        {employee.state !== "retired" && (
          <>
            <button
              disabled={busy}
              onClick={() =>
                void run("employee.lifecycle", {
                  id: employee.id,
                  expectedVersion: employee.version,
                  decision: employee.state === "paused" ? "resume" : "pause",
                })
              }
            >
              {employee.state === "paused" ? "Resume" : "Pause"}
            </button>
            <button
              disabled={busy}
              onClick={() =>
                void run("employee.lifecycle", {
                  id: employee.id,
                  expectedVersion: employee.version,
                  decision: "retire",
                })
              }
            >
              Retire employee
            </button>
          </>
        )}
      </div>
    </details>
  );
}
