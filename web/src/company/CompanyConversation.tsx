import { Fragment, useEffect, useRef, useState, type ReactNode } from "react";
import { ArrowUp, Info, MessageSquareText, Paperclip, Plus, SlidersHorizontal, X, Zap } from "lucide-react";
import {
  attachmentAccept,
  attachmentBytes,
  readConversationAttachment,
  type ConversationAttachment,
} from "./conversationAttachments";
import { api } from "../lib/api";
import { skillInput } from "../roles/company";
import { EmployeeActions } from "./EmployeeActions";
import type { Employee, Event, Snapshot, ThreadDetail, Work } from "./api";
import { ConversationJob } from "./ConversationJob";
import { MeasuredJob } from "./MeasuredJob";
import { ConnectCard } from "./ConnectCard";
import { DraftMarkdown } from "./DraftMarkdown";
import { avatarStyle, initials } from "./Home";
import { employeeState, humanize, taskState, workName } from "./labels";
import { useMemberAccess } from "./MemberAccess";
import { ModelChoice } from "./ModelChoice";
import { readableReply, replyFields, stoppedReply } from "./replyText";
import { defaultAllowance, SpendLimit } from "./allowance";
import { Fold, isLong } from "./Fold";

export type ConversationDraft = {
  body: string;
  skill: string;
  brief: Record<string, string>;
  evidence: string[];
  recipient: string;
  delivery?: string;
  allowance?: string;
  modelRoute?: string;
  attachments?: ConversationAttachment[];
};
const emptyDraft = (): ConversationDraft => ({
  body: "",
  skill: "chat",
  brief: {},
  evidence: [],
  recipient: "",
  delivery: "reply",
});
type Props = {
  memberRole?: string;
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
  // The work shown in the work window beside this chat: the chat shows a placeholder for it rather than a second,
  // separately loading copy of the same card (UX round 5).
  openWork?: string;
  // Conversation replies open their review here, rather than a work window with no reply controls.
  focusReply?: string;
  onShowWork?: (task: Work) => void;
  onOpenPlans?: () => void;
  onRememberResult?: (artifactId: string) => void;
  onRepeatWork?: (taskId: string) => void;
  // Opens Company settings → Integrations, for a Connect card whose integration is set up there.
  onOpenSetup?: () => void;
  // Home's desk: controls for the conversation's toolbar, and items after the last message (the employee's note,
  // a shift's latest round, work running elsewhere).
  toolbar?: ReactNode;
  trailing?: ReactNode;
};

export function CompanyConversation({
  memberRole,
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
  openWork,
  focusReply,
  onShowWork,
  onOpenPlans,
  onRememberResult,
  onRepeatWork,
  onOpenSetup,
  toolbar,
  trailing,
}: Props) {
  const { canWork } = useMemberAccess();
  const scope = company ? "company" : (employee?.id || "missing") + ":" + thread,
    draft = drafts.get(scope) ?? emptyDraft(),
    d = snapshot.data;
  const [notice, setNotice] = useState(""),
    [readingFile, setReadingFile] = useState(false),
    [localError, setLocalError] = useState(""),
    [companyDetail, setCompanyDetail] = useState<ThreadDetail | null>(null);
  const sending = useRef(false),
    scroll = useRef<HTMLDivElement>(null),
    stick = useRef(true),
    input = useRef<HTMLTextAreaElement>(null),
    mounted = useRef(true),
    companyRequest = useRef(0);
  const filePicker = useRef<HTMLInputElement>(null),
    fileRequest = useRef(0),
    reading = useRef(false),
    section = useRef<HTMLElement>(null),
    options = useRef<HTMLDetailsElement>(null);
  // The + menu closes on a click outside it or on Escape, like any menu.
  useEffect(() => {
    const close = (event: PointerEvent | KeyboardEvent) => {
      const menu = options.current;
      if (!menu?.open) return;
      if (event instanceof KeyboardEvent ? event.key === "Escape" : !menu.contains(event.target as Node))
        menu.open = false;
    };
    document.addEventListener("pointerdown", close);
    document.addEventListener("keydown", close);
    return () => {
      document.removeEventListener("pointerdown", close);
      document.removeEventListener("keydown", close);
    };
  }, []);
  const closeOptions = () => {
    if (options.current) options.current.open = false;
  };
  const openOptions = () => {
    if (options.current) options.current.open = true;
  };
  // Tools keep their own panel above the message box (its recovery state lives there); the + menu opens it.
  const openTools = () => {
    closeOptions();
    const strip = section.current?.querySelector<HTMLDetailsElement>(".co-action-strip");
    if (!strip) return;
    strip.open = true;
    strip.querySelector("summary")?.focus();
  };
  useEffect(() => {
    fileRequest.current++;
    reading.current = false;
    setReadingFile(false);
    return () => {
      fileRequest.current++;
    };
  }, [scope]);
  const attached = draft.attachments ?? [];
  async function attach(file?: File) {
    if (!file || reading.current || busy || company || draft.skill !== "chat") return;
    if (attached.length >= 3) {
      setLocalError("Attach up to three files per message.");
      return;
    }
    reading.current = true;
    const request = ++fileRequest.current;
    setReadingFile(true);
    setLocalError("");
    try {
      const result = await readConversationAttachment(file);
      if (request !== fileRequest.current) return;
      updateDraft(scope, (previous) => ({
        ...(previous ?? emptyDraft()),
        attachments: [...(previous?.attachments ?? []), result],
      }));
    } catch (error) {
      if (request === fileRequest.current) setLocalError((error as Error).message);
    } finally {
      if (request === fileRequest.current) {
        reading.current = false;
        setReadingFile(false);
      }
    }
  }
  const target = company ? d.employees.find((e) => e.id === draft.recipient) : employee,
    role = d.roles.find((r) => r.id === employee?.roleId),
    structured = company ? undefined : skillInput(role, draft.skill),
    hasTools = canWork && !company && !!employee && !!role?.actions?.length;
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
    shown?.messages.filter((m) => m.author === "owner" || !artifacts.some((a) => a.body === m.body)) ?? [];
  // Work sits after the latest shown message at its watermark, so a revision whose watermark is a hidden result
  // message does not drop below later conversation (dogfood, 2026-10-30).
  // A finished reply's details sit under the reply it wrote, not between the question and its answer (UX round 11).
  const replyAfter = (t: Work) => {
    if (t.kind !== "conversation" || t.state !== "completed") return undefined;
    const next = messages.find((m) => m.sequence > t.watermark);
    return next && next.author !== "owner" ? next.sequence : undefined;
  };
  const anchorOf = (t: Work) =>
    replyAfter(t) ?? messages.reduce((found, m) => (m.sequence <= t.watermark ? m.sequence : found), Number.NaN);
  const orphaned = tasks.filter((t) => Number.isNaN(anchorOf(t)));
  const latestReply = messages.filter((m) => m.author !== "owner").at(-1)?.id;
  // An API model enforces the reply allowance as a cap in US cents; included usage has no per-job limit, so its
  // planning default is sent unasked (docs/adr/0177-plain-spend-limits.md).
  const cents =
    (snapshot.capabilities.modelChoices?.find((option) => option.route === draft.modelRoute)?.allowanceUnit ??
      snapshot.capabilities.allowanceUnit) === "usd-cents";
  const allowanceValue = cents ? (draft.allowance ?? defaultAllowance(true)) : defaultAllowance(false);
  // "Message prepared… Send" stops being true once the prepared reply is sent, cancelled or expires.
  const replyWaiting = tasks.some((t) => d.measured?.some((m) => m.taskId === t.id && m.job.state === "proposed"));
  const waitingBefore = useRef(false);
  useEffect(() => {
    if (waitingBefore.current && !replyWaiting)
      setNotice((current) => (current.startsWith("Reply ready") ? "" : current));
    waitingBefore.current = replyWaiting;
  }, [replyWaiting]);
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
  const focusedReply = useRef<string | undefined>(undefined);
  useEffect(() => {
    if (!focusReply) {
      focusedReply.current = undefined;
      return;
    }
    if (focusedReply.current === focusReply || !shown) return;
    const card = Array.from(scroll.current?.querySelectorAll<HTMLElement>("[data-reply-task]") ?? []).find(
      (element) => element.dataset.replyTask === focusReply,
    );
    if (!card) return;
    focusedReply.current = focusReply;
    const details = card.querySelector<HTMLDetailsElement>(":scope > details");
    if (details) details.open = true;
    card.scrollIntoView({ block: "center" });
    card.focus({ preventScroll: true });
  }, [focusReply, shown, tasks.length]);
  async function send() {
    if (sending.current || reading.current || busy || (!company && (!privateReady || employee?.state !== "active")))
      return;
    const sent = draft,
      sentInput = company ? undefined : skillInput(role, sent.skill),
      body = sentInput ? JSON.stringify(sent.brief) : sent.body;
    if (!body.trim()) return;
    if (attached.some((file) => !file.text.trim()) || attachmentBytes(attached) > 6000) {
      setLocalError("Keep the relevant text in each attachment, within 6,000 bytes total. Nothing has been sent.");
      return;
    }
    if (attached.length && (company || sent.skill !== "chat")) {
      setLocalError("Use Conversation for these attachments, or remove them before changing work type.");
      return;
    }
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
    const sentAllowance = cents ? (sent.allowance ?? defaultAllowance(true)) : defaultAllowance(false);
    if (reply && !(Number(sentAllowance) > 0 && Number(sentAllowance) <= 1000)) {
      setLocalError("Enter a spend limit between $0.01 and $10.00.");
      openOptions();
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
          creditAllowance: Number(sentAllowance),
          modelRoute: sent.modelRoute || null,
          evidence: company ? [] : sent.evidence,
          attachments: sent.attachments,
        });
      else if (company) result = await run("company.message", { body });
      else
        result = await run("message.send", {
          employeeId: employee!.id,
          threadId: thread,
          body,
          skill: sent.skill === "chat" ? undefined : sent.skill,
          evidence: sent.evidence,
          attachments: sent.attachments,
        });
      if (!result) return;
      updateDraft(scope, (current) =>
        JSON.stringify(current ?? sent) === JSON.stringify(sent)
          ? { ...sent, body: "", brief: {}, attachments: [] }
          : (current ?? sent),
      );
      if (mounted.current) {
        setNotice(
          reply
            ? "Reply ready to review below. Nothing has been sent to the model."
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
    task.id === openWork ? (
      <button key={task.id} className="co-work-placeholder" onClick={() => onShowWork?.(task)}>
        {workName(role, task.skill)} is open in the work window beside this chat.
      </button>
    ) : task.kind === "conversation" ? (
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
        onOpenResult={onShowWork}
        onOpenPlans={onOpenPlans}
        onRememberResult={onRememberResult}
        onRepeatWork={onRepeatWork}
      />
    );
  return (
    <section
      ref={section}
      className="fe-chat co-conversation"
      aria-label={company ? "Company conversation" : "Private employee conversation"}
    >
      <div className="co-conversation-context">
        <div className="co-context-line">
          {/* An employee's responsibility is in its header's tooltip and its settings; the toolbar keeps one row. */}
          {(company || !employee) && (
            <p className="co-context-summary">
              {company
                ? "Notes for the whole company. To get an answer, choose who replies below the message box."
                : "Choose an employee conversation from the sidebar."}
            </p>
          )}
          {employee && (
            <>
              <label className="co-thread-picker">
                <span className="co-hidden-label">Conversation</span>
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
              {canWork && (
                <button
                  className="co-ghost"
                  disabled={busy || employee.state === "retired"}
                  onClick={() => void createThread()}
                >
                  <Plus size={14} aria-hidden="true" />
                  <span className="co-toolbar-text">New conversation</span>
                </button>
              )}
            </>
          )}
          {employee && privateReady && <PrivateContext employee={employee} detail={detail!} busy={busy} run={run} />}
          {/* How a reply is authorized, one click away rather than in every header (UX audit, 2026-10-20). */}
          <details className="co-about">
            <summary aria-label="How replies work" title="How replies work">
              <Info size={15} aria-hidden="true" />
            </summary>
            <p>
              {snapshot.capabilities.conversationalReplies
                ? "One text-only reply can be prepared, reviewed and explicitly authorized. Each turn needs its own approval; no tools, coding or autonomous follow-up."
                : "Live replies unavailable. Messages remain saved locally; eligible draft jobs retain separate approval."}
            </p>
            <p>
              {cents
                ? "Preparing sends no model request. The final Send authorizes one reply, refused before sending if the most it could cost is over your spend limit."
                : "Preparing sends no model request. The final Send authorizes one reply from your plan’s included usage, which has no per-job limit. Nothing is bought, and no API key is used instead."}
            </p>
            <p>Replies are informational. Work and decisions still need your approval.</p>
            {company && <p>Private employee notes, department facts and private conversations are excluded.</p>}
          </details>
          {toolbar}
        </div>
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
                  <h2>
                    {company
                      ? canWork
                        ? "What should we work on?"
                        : "Company conversation"
                      : "Conversation with " + employee!.name}
                  </h2>
                  <p>
                    {!canWork
                      ? "No messages have been saved here yet. An approver or owner can begin this conversation."
                      : snapshot.capabilities.conversationalReplies
                        ? company
                          ? "Save a company note, or select an employee to ask a question. Review the context before authorizing one reply."
                          : "Ask a question below, review the context, then authorize one reply. You can also save a note without requesting a reply."
                        : company
                          ? "Save a company note below. AI replies are unavailable."
                          : "Save a note or brief below to prepare for later. AI replies are unavailable; saving sends no model request."}
                  </p>
                </div>
              )}
              {messages.map((message) => {
                const fields = message.author === "owner" ? undefined : replyFields(message.body);
                return (
                  <Fragment key={message.id}>
                    <article
                      className={"fe-msg " + (message.author === "owner" ? "user" : "assistant")}
                      data-message-id={message.id}
                    >
                      {message.author !== "owner" && target && (
                        <span className="co-avatar co-msg-avatar" aria-hidden="true" style={avatarStyle(target.name)}>
                          {initials(target.name)}
                        </span>
                      )}
                      <div className="fe-msg-body">
                        {/* Your messages are the right-hand bubbles; a reply names its employee and that it is AI. */}
                        <div className={"fe-msg-meta" + (message.author === "owner" ? " co-hidden-label" : "")}>
                          <strong>{message.author === "owner" ? "You" : target?.name}</strong>
                          {message.author !== "owner" && <span className="co-ai-tag">AI reply</span>}
                        </div>
                        <div className="fe-msg-content">
                          {/* A long request the app wrote from a goal opens with its first lines; the exact text stays
                            one click away (dogfood, 2026-10-30). */}
                          {message.author === "owner" && message.body.length > 400 ? (
                            <>
                              <p>{message.body.split(/\r?\n/).slice(0, 2).join(" ").slice(0, 300)}…</p>
                              <details>
                                <summary>Show the full request</summary>
                                <p style={{ whiteSpace: "pre-wrap" }}>{message.body}</p>
                              </details>
                            </>
                          ) : message.author === "owner" ? (
                            <p>{message.body}</p>
                          ) : fields ? (
                            <dl className="co-reply-fields">
                              {fields.map(([name, value]) => (
                                <div key={name}>
                                  <dt>{humanize(name)}</dt>
                                  <dd>{value}</dd>
                                </div>
                              ))}
                            </dl>
                          ) : (
                            // Replies are written in Markdown; show the formatting, not its asterisks (dogfood).
                            <Fold long={isLong(message.body)} initiallyOpen={message.id === latestReply}>
                              <DraftMarkdown>{readableReply(message.body)}</DraftMarkdown>
                            </Fold>
                          )}
                        </div>
                        {message.attachments?.map((file, index) => (
                          <details className="co-message-attachment" key={index}>
                            <summary>
                              <Paperclip size={13} aria-hidden="true" /> {file.name} ·{" "}
                              {file.kind === "image-ocr" ? "recognized text" : "document text"}
                            </summary>
                            <p style={{ whiteSpace: "pre-wrap" }}>{file.text}</p>
                          </details>
                        ))}
                      </div>
                    </article>
                    {tasks.filter((t) => anchorOf(t) === message.sequence).map(job)}
                  </Fragment>
                );
              })}
              {orphaned.map(job)}
              {/* Connect cards this conversation raised (docs/adr/0046-connect-cards.md). */}
              {!company &&
                (d.connections ?? [])
                  .filter((c) => c.threadId === thread && c.state !== "dismissed")
                  .map((c) => (
                    <ConnectCard
                      key={c.id}
                      memberRole={memberRole}
                      request={c}
                      snapshot={snapshot}
                      run={run}
                      refresh={refresh}
                      onOpenSetup={() => onOpenSetup?.()}
                    />
                  ))}
              {trailing}
            </>
          )}
        </div>
      </div>
      {canWork && !company && employee && (
        <EmployeeActions
          key={employee.id + ":" + thread}
          role={role}
          employeeName={employee.name}
          employeeId={employee.id}
          threadId={thread}
          disabled={busy || employee.state !== "active"}
          refresh={refresh}
        />
      )}
      {canWork ? (
        <div className="fe-composer-wrap co-conversation-composer">
          {localError && <p role="alert">{localError}</p>}
          {notice && (
            <p role="status" className="co-send-status">
              {notice}
            </p>
          )}
          <form
            className="co-composer-form"
            onInvalidCapture={(event) => {
              const options = (event.target as HTMLElement).closest(
                ".co-composer-options",
              ) as HTMLDetailsElement | null;
              if (options) options.open = true;
            }}
            onSubmit={(e) => {
              e.preventDefault();
              void send();
            }}
          >
            {structured ? (
              <details open className="co-brief-composer">
                <summary>{structured.label}</summary>
                <structured.Editor value={draft.brief} onChange={(brief) => update({ brief })} />
              </details>
            ) : null}
            {attached.length > 0 && (
              <div className="co-attachments" aria-label="Attached text">
                <small>Only this text goes with your message. Check recognition and remove anything private.</small>
                {attached.map((file, index) => (
                  <details key={index} open className="co-attachment">
                    <summary>
                      {file.name} · {file.kind === "image-ocr" ? "screenshot text" : "document text"}
                    </summary>
                    <label>
                      {file.kind === "image-ocr" ? "Recognized text (editable)" : "Extracted text (editable)"}
                      <textarea
                        rows={4}
                        value={file.text}
                        onChange={(e) =>
                          update({
                            attachments: attached.map((item, i) =>
                              i === index ? { ...item, text: e.target.value } : item,
                            ),
                          })
                        }
                      />
                    </label>
                    <button
                      type="button"
                      aria-label={"Remove " + file.name}
                      onClick={() => update({ attachments: attached.filter((_, i) => i !== index) })}
                    >
                      <X size={14} /> Remove
                    </button>
                  </details>
                ))}
                <small>
                  {attachmentBytes(attached).toLocaleString()} / 6,000 bytes · Images and formatting are not sent; only
                  the text you see here.
                </small>
              </div>
            )}
            {!company && draft.skill === "chat" && (
              <input
                ref={filePicker}
                type="file"
                hidden
                aria-label="Choose attachment"
                accept={attachmentAccept}
                onChange={(e) => {
                  void attach(e.target.files?.[0]);
                  e.target.value = "";
                }}
              />
            )}
            {/* One box, as in other chat apps: the message, then a + that holds attaching, tools and reply options,
                and the send button (owner feedback, 2026-10-31). */}
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
                  onPaste={(e) => {
                    const file = Array.from(e.clipboardData.files).find((file) => file.type.startsWith("image/"));
                    if (file && !company && draft.skill === "chat") {
                      e.preventDefault();
                      void attach(file);
                    }
                  }}
                  onKeyDown={(e) => {
                    if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) {
                      e.preventDefault();
                      void send();
                    }
                  }}
                  placeholder={
                    company ? "Message your company…" : "Message " + (employee?.name || "this employee") + "…"
                  }
                />
              )}
              <div className="co-composer-bar">
                <details className="co-composer-options" ref={options}>
                  <summary aria-label="Attach, tools and options" title="Attach, tools and options">
                    <Plus size={18} aria-hidden="true" />
                  </summary>
                  <div className="co-composer-menu">
                    {!company && draft.skill === "chat" && (
                      <button
                        type="button"
                        className="co-composer-item"
                        disabled={busy || readingFile || !privateReady || attached.length >= 3}
                        onClick={() => {
                          closeOptions();
                          filePicker.current?.click();
                        }}
                      >
                        <Paperclip size={16} aria-hidden="true" />
                        <span>
                          Attach a file
                          <small>
                            A screenshot, PDF, Word, text or Markdown file. You can also paste a screenshot.
                          </small>
                        </span>
                      </button>
                    )}
                    {hasTools && (
                      <button type="button" className="co-composer-item" onClick={openTools}>
                        <Zap size={16} aria-hidden="true" />
                        <span>
                          Tools
                          <small>Read-only lookups {employee?.name} can run, with results back for your review.</small>
                        </span>
                      </button>
                    )}
                    <div className="co-composer-tools">
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
                                  {workName(role, s)}
                                </option>
                              ))}
                          </select>
                        </label>
                      )}
                      {eligible && (
                        <label>
                          Message action
                          <select
                            value={draft.delivery ?? "reply"}
                            onChange={(e) => update({ delivery: e.target.value })}
                          >
                            <option value="reply">Prepare one reply for review</option>
                            <option value="save">Save message only</option>
                          </select>
                        </label>
                      )}
                      {reply && (
                        <ModelChoice
                          options={snapshot.capabilities.modelChoices}
                          value={draft.modelRoute ?? ""}
                          onChange={(modelRoute) => update({ modelRoute, allowance: undefined })}
                        />
                      )}
                      {reply && (
                        <SpendLimit
                          cents={cents}
                          value={allowanceValue}
                          onChange={(allowance) => update({ allowance })}
                        />
                      )}
                      {!company && (
                        <details className="co-evidence">
                          <summary>Evidence ({draft.evidence.length})</summary>
                          {!d.artifacts.some((a) => a.state === "approved") && (
                            <p className="co-muted">
                              No approved results yet. Approve a result to include it as evidence here.
                            </p>
                          )}
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
                    </div>
                    <p className="co-composer-disclosure">
                      {reply
                        ? "Prepare reply makes no model call. You review the reply before anything is sent or spent."
                        : "Saves your message without an AI reply."}
                    </p>
                  </div>
                </details>
                {/* What the message will do is never hidden: a setting changed from the plain default shows here. */}
                {readingFile && <span className="co-composer-chip">Reading the file…</span>}
                {(draft.skill !== "chat" || draft.delivery === "save") && (
                  <button type="button" className="co-composer-chip" onClick={openOptions}>
                    {draft.skill !== "chat" ? workName(role, draft.skill) : "Save only"}
                  </button>
                )}
                <span className="co-composer-spacer" />
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
                      : company
                        ? "Save company note"
                        : draft.skill === "chat"
                          ? "Save message only"
                          : "Assign work"
                  }
                  disabled={
                    busy ||
                    readingFile ||
                    (!company && (!privateReady || employee?.state !== "active")) ||
                    (!structured && !draft.body.trim())
                  }
                >
                  <ArrowUp size={17} aria-hidden="true" />
                  <span>
                    {reply
                      ? "Prepare reply"
                      : company
                        ? "Save company note"
                        : draft.skill === "chat"
                          ? "Save message"
                          : "Assign work"}
                  </span>
                </button>
              </div>
            </div>
          </form>
          {reply && !allowanceValue && (
            <p className="fe-composer-hint">
              Set a spend limit for this reply under +. You review it before anything is sent.
            </p>
          )}
        </div>
      ) : (
        <p className="co-muted" role="status">
          You have read-only access. An approver or owner can send messages, assign work and review results.
        </p>
      )}
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
  const { canWork } = useMemberAccess();
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
        cents={snapshot.capabilities.allowanceUnit === "usd-cents"}
        modelChoices={snapshot.capabilities.modelChoices}
        busy={busy}
        prepare={run}
        refresh={refresh}
      />
    );
  return (
    // A finished turn is one quiet line; a turn that waits on you is a card (UX round 7).
    <article className={"co-reply-turn" + (active ? "" : " finished")} data-reply-task={task.id} tabIndex={-1}>
      <small>
        Reply from {employee?.name} · {thread?.scope === "company" ? "company" : "private"} conversation
      </small>
      {active ? (
        contents
      ) : (
        <details>
          <summary>
            {task.state === "completed" ? "Usage and receipt" : "Reply status · " + taskState(task.state)}
          </summary>
          {contents}
        </details>
      )}
      {entry?.job.state === "dispatched" && (
        <p role="status" aria-live="polite">
          Reply pending. Waiting for complete text and confirmed usage; no partial answer is published.
        </p>
      )}
      {canWork && ["queued", "blocked", "running", "unknown", "failed"].includes(task.state) && (
        <button disabled={busy} onClick={() => void run("task.cancel", { id: task.id, expectedVersion: task.version })}>
          {entry?.job.state === "dispatched" ? "Stop reply" : "Cancel reply"}
        </button>
      )}
      {entry?.error && <p role="alert">{stoppedReply(entry.error)}</p>}
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
  const { canManage, canWork } = useMemberAccess();
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
      <summary title="Context & settings">
        <SlidersHorizontal size={14} aria-hidden="true" />
        <span className="co-toolbar-text">Context & settings</span>
      </summary>
      <p>
        Replies use this conversation, your attached text, approved facts and selected evidence. Attachments stay
        reference material in this conversation. They do not become shared company knowledge.
      </p>
      {canWork ? (
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
      ) : (
        <>
          <h4>Private notes</h4>
          <p>{detail.notes.body || "No private notes saved."}</p>
        </>
      )}
      <small>
        {employeeState(employee.state)} · {humanize(employee.roleId)}. Unsaved note edits stay only in this open view.
      </small>
      {canManage && employee.state !== "retired" && (
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
        {canManage && employee.state !== "retired" && (
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
