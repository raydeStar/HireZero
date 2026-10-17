import { useEffect, useState } from "react";
import { Archive, ExternalLink, Info, Play, RotateCcw, X } from "lucide-react";
import { api, ApiError } from "./api";
import { shortRepo } from "./Threads";
import { ActionFeedbackNotice } from "../roles/developer/ActionFeedbackNotice";
import {
  trackedAction,
  loopOutcome,
  strategyOutcome,
  consentOutcome,
  mayStart,
} from "../roles/developer/actionFeedback";
import type { PullRequestWorkflow } from "../roles/developer/github";

export async function taskWorkflowBaseline(requestId: string, workflowId: string) {
  const workflows = await api<PullRequestWorkflow[]>(
    "/github/workflows/chat/" + encodeURIComponent(requestId),
    "GET",
    undefined,
    { timeoutMs: 15000 },
  );
  const workflow =
    Array.isArray(workflows) && workflows.find((item) => item.id === workflowId && item.requestId === requestId);
  if (!workflow)
    throw new ApiError(
      "The current workflow could not be verified. No execution decision was sent. Open the thread and check its status.",
      409,
    );
  return workflow;
}

interface OwnerTask {
  id: string;
  title: string;
  source: string;
  url: string | null;
  requestId: string | null;
  status: string;
  updatedAt: string;
  owner: string;
  priority: string;
  blockers: string[];
  nextAction: string;
  approval: string;
  localState: string;
  provenance: string[] | null;
  kind?: string | null;
  detail?: string | null;
  start?: string | null;
}

const age = (at: string) => {
  const minutes = Math.round((Date.now() - Date.parse(at)) / 60000);
  return !at || Number.isNaN(minutes)
    ? ""
    : minutes < 1
      ? "now"
      : minutes < 60
        ? minutes + "m"
        : minutes < 1440
          ? Math.round(minutes / 60) + "h"
          : Math.round(minutes / 1440) + "d";
};
const label = (value: string) => value.replaceAll("-", " ").replace(/^\w/, (letter) => letter.toUpperCase());
const sentence = (value: string) => {
  const text = value.trim().replace(/\.$/, "");
  return text.charAt(0).toUpperCase() + text.slice(1).toLowerCase() + ".";
};
const shown = 6;

/** Every task across local requests and recorded workflows, as a compact, scannable list. Resolve and dismiss affect this view only. */
export function OwnerTasks({ onOpen, onForbidden }: { onOpen: (id: string) => void; onForbidden: () => void }) {
  const [tasks, setTasks] = useState<OwnerTask[]>([]),
    [error, setError] = useState(""),
    [busy, setBusy] = useState(""),
    [closed, setClosed] = useState(false),
    [all, setAll] = useState(false),
    [note, setNote] = useState(""),
    [cardError, setCardError] = useState<{ id: string; text: string } | null>(null);
  function failed(cause: unknown) {
    setError((cause as Error).message);
    if (cause instanceof ApiError && cause.status === 403) onForbidden();
  }
  useEffect(() => {
    let active = true;
    async function load() {
      try {
        const value = await api<OwnerTask[]>("/owner-tasks");
        if (active) {
          setTasks(value);
          setError("");
        }
      } catch (cause) {
        if (active) failed(cause);
      }
    }
    void load();
    const timer = setInterval(() => {
      if (document.visibilityState === "visible") void load();
    }, 15000);
    return () => {
      active = false;
      clearInterval(timer);
    };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  async function change(task: OwnerTask, state: string, priority = task.priority) {
    setBusy(task.id);
    try {
      setTasks(await api<OwnerTask[]>("/owner-tasks/" + encodeURIComponent(task.id), "PUT", { state, priority }));
      setError("");
    } catch (cause) {
      failed(cause);
    } finally {
      setBusy("");
    }
  }
  async function start(task: OwnerTask) {
    if (busy) return;
    setBusy(task.id);
    setError("");
    setNote("");
    setCardError(null);
    try {
      const scope = task.requestId || task.id;
      let baseline: PullRequestWorkflow | undefined, preflightError: unknown;
      if (task.start?.startsWith("loop:"))
        try {
          baseline = await taskWorkflowBaseline(scope, task.start.split(":")[1]);
        } catch (cause) {
          preflightError = new ApiError(
            "The current workflow could not be checked. No execution decision was sent. Open the thread and check its status.",
            cause instanceof ApiError && cause.status === 403 ? 403 : 409,
            undefined,
            cause instanceof ApiError ? cause.correlationId : undefined,
          );
        }
      const outcome = await trackedAction(
        scope,
        task.start?.split(":")[1],
        task.start?.startsWith("refresh:") ? "refresh" : task.start === "allow" ? "allow" : "loop",
        async (options) => {
          if (preflightError) throw preflightError;
          if (task.start?.startsWith("loop:")) {
            const [, workflow, snapshot] = task.start.split(":");
            return api("/github/workflows/" + workflow + "/loop", "POST", { snapshot }, options);
          }
          if (task.start?.startsWith("refresh:"))
            return api("/github/workflows/" + task.start.slice(8) + "/refresh", "POST", {}, options);
          if (task.start === "allow" && task.requestId)
            return api("/requests/" + task.requestId + "/allow-agent", "POST", {}, options);
          throw new ApiError("This action is no longer available. Check the task status.", 409);
        },
        (value) => {
          if (task.start?.startsWith("loop:")) {
            const target = /^https:\/\/github\.com\/([^/]+\/[^/]+)\/pull\/(\d+)/.exec(task.url || "");
            return loopOutcome(value, {
              id: task.start.split(":")[1],
              requestId: scope,
              repository: target?.[1] || "",
              number: Number(target?.[2]),
            });
          }
          if (task.start?.startsWith("refresh:")) return strategyOutcome(value, scope);
          return consentOutcome(value, scope);
        },
        baseline,
      );
      if (!outcome.ok) {
        if (outcome.cause instanceof ApiError && outcome.cause.status === 403) onForbidden();
        return;
      }
      // The decision receipt survives a failed read; a refresh failure is not a failed start.
      try {
        setTasks(await api<OwnerTask[]>("/owner-tasks"));
      } catch {
        setError("Your decision was received, but the view could not refresh. Check status before another action.");
      }
    } finally {
      setBusy("");
    }
  }
  // Home is the running conversation, not a task.
  const visible = tasks.filter((task) => task.status !== "home" && (closed || task.localState === "open"));
  const listed = all ? visible : visible.slice(0, shown);
  return (
    <div className="fe-up-next ot" aria-label="All your tasks">
      <div className="ot-head">
        <h3>
          Your tasks <span className="fe-count">{visible.length}</span>
        </h3>
        <span
          className="ot-info"
          title="Local requests and recorded workflow evidence. Resolving or dismissing here only changes this view."
        >
          <Info size={13} aria-hidden="true" />
        </span>
        <button
          className={"ot-toggle" + (closed ? " on" : "")}
          aria-pressed={closed}
          onClick={() => setClosed((value) => !value)}
        >
          {closed ? "Hide closed" : "Show closed"}
        </button>
      </div>
      {visible.length === 0 ? (
        <p className="ot-empty">{closed ? "No tasks recorded yet." : "Nothing open. Closed tasks are hidden."}</p>
      ) : (
        <ol className="ot-list">
          {listed.map((task) => {
            const open = task.localState === "open",
              needsYou =
                /needs?[-\s]?owner|needs?[-\s]?you|approval/i.test(task.approval) &&
                !/allowed|approved|none/i.test(task.approval);
            const source = (task.provenance ?? [task.source])[0] ?? task.source;
            return (
              <li key={task.id} className={"ot-task p-" + task.priority + (open ? "" : " closed")}>
                {task.kind && (
                  <div className="ot-kind" title={task.detail ?? undefined}>
                    {task.kind}
                  </div>
                )}
                <div className="ot-top" title={task.detail ?? undefined}>
                  {task.requestId ? (
                    <button className="ot-title" onClick={() => onOpen(task.requestId!)} title={task.title}>
                      {shortRepo(task.title)}
                    </button>
                  ) : (
                    <span className="ot-title" title={task.title}>
                      {shortRepo(task.title)}
                    </span>
                  )}
                  <time
                    className="ot-age"
                    title={task.updatedAt ? new Date(task.updatedAt).toLocaleString() : "Freshness unknown"}
                  >
                    {age(task.updatedAt)}
                  </time>
                </div>
                <div className="ot-chips">
                  <span className="ot-chip">{label(source)}</span>
                  <span className="ot-chip">{label(task.status)}</span>
                  {!open && <span className="ot-chip muted">{label(task.localState)}</span>}
                  {needsYou && open && <span className="ot-chip attn">Needs you</span>}
                </div>
                {task.blockers
                  .filter((blocker) => !(needsYou && /owner approval|needs (the )?owner/i.test(blocker)))
                  .map((blocker) => (
                    <p key={blocker} className="ot-blocker">
                      {blocker}
                    </p>
                  ))}
                {task.nextAction && open && <p className="ot-next">Next: {sentence(task.nextAction)}</p>}
                <ActionFeedbackNotice
                  requestId={task.requestId || task.id}
                  onCheck={() =>
                    task.requestId
                      ? onOpen(task.requestId)
                      : void api<OwnerTask[]>("/owner-tasks").then(setTasks).catch(failed)
                  }
                />
                {open && (task.start || (needsYou && task.requestId)) && (
                  <div className="ot-primary">
                    {task.start ? (
                      <button
                        className="ot-start"
                        disabled={
                          busy === task.id ||
                          (task.start?.startsWith("loop:") &&
                            !mayStart(task.requestId || task.id, task.start.split(":")[1]))
                        }
                        onClick={() => void start(task)}
                        title={
                          task.start.startsWith("refresh:")
                            ? "This strategy predates the current conversation or consent. Re-read the PR and propose a current one; nothing runs until you approve it."
                            : task.start.startsWith("loop:")
                              ? "Approve this strategy and keep fixing until Codex is clean and CI is green. Each stage is posted in chat."
                              : "Let the agent work on this request. Its reply is posted in chat."
                        }
                      >
                        <Play size={13} />
                        {busy === task.id
                          ? task.start.startsWith("refresh:")
                            ? "Refreshing…"
                            : "Starting…"
                          : task.start.startsWith("refresh:")
                            ? "Refresh strategy"
                            : task.start.startsWith("loop:")
                              ? "Start fixing"
                              : "Let the agent work"}
                      </button>
                    ) : (
                      <button
                        className="ot-review"
                        onClick={() => onOpen(task.requestId!)}
                        title="Open the thread to see what needs your decision."
                      >
                        Review
                      </button>
                    )}
                    {task.start && task.requestId && (
                      <button className="ot-review" onClick={() => onOpen(task.requestId!)}>
                        Details
                      </button>
                    )}
                  </div>
                )}
                <div className="ot-actions">
                  <select
                    className="ot-priority"
                    aria-label={"Priority for " + task.title}
                    value={task.priority}
                    disabled={busy === task.id}
                    onChange={(event) => void change(task, task.localState, event.target.value)}
                  >
                    <option value="high">High</option>
                    <option value="normal">Normal</option>
                    <option value="low">Low</option>
                  </select>
                  <span className="ot-spacer" />
                  {task.url && (
                    <a
                      className="ot-icon"
                      href={task.url}
                      target="_blank"
                      rel="noopener noreferrer"
                      title="Open source evidence"
                      aria-label={"Open source evidence for " + task.title}
                    >
                      <ExternalLink size={14} />
                    </a>
                  )}
                  {open ? (
                    <>
                      <button
                        className="ot-icon"
                        disabled={busy === task.id}
                        onClick={() => void change(task, "resolved")}
                        title="Mark done — hides it from this list; nothing is approved or started"
                        aria-label={"Mark " + task.title + " done"}
                      >
                        <Archive size={14} />
                      </button>
                      <button
                        className="ot-icon"
                        disabled={busy === task.id}
                        onClick={() => void change(task, "dismissed")}
                        title="Dismiss — hides it from this list; nothing is approved or started"
                        aria-label={"Dismiss " + task.title}
                      >
                        <X size={15} />
                      </button>
                    </>
                  ) : (
                    <button
                      className="ot-icon"
                      disabled={busy === task.id}
                      onClick={() => void change(task, "open")}
                      title="Restore"
                      aria-label={"Restore " + task.title}
                    >
                      <RotateCcw size={14} />
                    </button>
                  )}
                </div>
              </li>
            );
          })}
        </ol>
      )}
      {visible.length > shown && (
        <button className="ot-more" onClick={() => setAll((value) => !value)}>
          {all ? "Show fewer" : "Show all " + visible.length}
        </button>
      )}
      {note && (
        <p className="ot-note" role="status">
          {note}
        </p>
      )}
      {error && (
        <p className="fe-alert" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
