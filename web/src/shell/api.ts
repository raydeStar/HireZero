export type Session = {
  login: string;
  role: "owner" | "boss" | "trusted" | "visitor" | "manager" | "pending";
  accessMode: "local" | "tailscale" | "tailnet";
  agentModel: string;
  agentReasoning: string;
  roles?: string[];
  executionHeld?: boolean;
  chatOnly?: boolean;
  mutationsHeld?: boolean;
};
export type WorkRequest = {
  id: string;
  requester: string;
  project: string;
  title: string;
  description: string;
  status: string;
  semaForgeId: string | null;
  createdAt: string;
  shortTitle?: string | null;
  displayTitle?: string;
};
/** The readable title a list should show (a short one once the agent wrote it), falling back to what was typed. */
export const titleOf = (item: { title: string; displayTitle?: string }) => item.displayTitle || item.title;
export type Message = {
  id: string;
  requestId: string;
  author: string;
  body: string;
  createdAt: string;
  visibility?: "conversation" | "owner";
};
export type Detail = {
  request: WorkRequest;
  home?: boolean;
  messages: Message[];
  requesterRole?: string | null;
  agentAllowed?: boolean | null;
  agentUpgradeReviewRequired?: boolean | null;
  triage?: import("./TriageCard").Triage | null;
  access?: import("./Access").AccessRecord | null;
};
export type SharedWork = {
  requestId: string;
  summary: string;
  priority: string;
  status: string;
  outcome: string | null;
  updatedAt: string;
};
export type Person = { login: string; role: "boss" | "trusted" | "visitor" | "manager"; revoked: boolean };
export type Draft = { body: string; clientRequestId: string };
export type ReplyState = {
  status: "responding" | "queued" | "failed";
  error?: string;
  retryAt?: number;
  current?: string;
  ahead?: number;
  step?: string;
};
export type ReplyStatus = {
  running: boolean;
  state?: string;
  ahead?: number;
  current?: string | null;
  error?: string | null;
  step?: string | null;
};
export type ActivityEntry = { at: string; text: string; taskId: string | null; detail?: string | null };
/** A task's subject in plain words: GitHub links become "owner/repo #12". */
export const subject = (title: string) => {
  const pr = /^https?:\/\/github\.com\/([\w.-]+\/[\w.-]+)\/(?:pull|issues)\/(\d+)/i.exec(title.trim());
  if (pr) return pr[1] + " #" + pr[2];
  const repo = /^https?:\/\/github\.com\/([\w.-]+\/[\w.-]+)\/?$/i.exec(title.trim());
  return repo ? repo[1] : title;
};
/** What a task is, for people: "Reply about owner/repo #12", "Diagnose owner/repo #12". */
export const taskTitle = (task: { kind: string; title: string }) => {
  const what = subject(task.title);
  return task.kind === "reply" ? "Reply about " + what : task.kind === "diagnose-pr" ? "Look into " + what : what;
};
export type AgentTask = {
  id: string;
  kind: "reply" | "diagnose-pr";
  requestId: string | null;
  title: string;
  source: "ask" | "proactive";
  state: string;
  detail: string | null;
  createdAt: string;
  updatedAt: string;
};
export type ShiftSchedule = { enabled: boolean; days: number[]; start: string; end: string; timeZone: string };
export type ShiftState = {
  on: boolean;
  origin: string | null;
  startedAt: string | null;
  endsAt: string | null;
  proactiveDone: number;
  proactiveBudget: number;
  lastSenseAt: string | null;
  senseNote: string | null;
};
export type AgentState = {
  shift: ShiftState;
  schedule: ShiftSchedule;
  tasks: AgentTask[];
  recent: AgentTask[];
  activity: ActivityEntry[];
  runningIds: string[];
  progress: { state: string; summary: string; at: string | null; nextCheckAt: string | null; freshness: string };
};
export class ApiError extends Error {
  constructor(
    message: string,
    public status: number,
    public retryAfterSeconds?: number,
    public correlationId?: string,
    public code?: string,
  ) {
    super(message);
  }
}
export type ApiOptions = { timeoutMs?: number; correlationId?: string };
export async function api<T>(path: string, method = "GET", body?: unknown, options: ApiOptions = {}): Promise<T> {
  const correlation = options.correlationId || (method === "GET" ? undefined : crypto.randomUUID());
  const controller = new AbortController(),
    timer = options.timeoutMs ? setTimeout(() => controller.abort(), options.timeoutMs) : undefined;
  try {
    const response = await fetch("/api" + path, {
      method,
      cache: "no-store",
      signal: controller.signal,
      headers:
        method === "GET" && !correlation
          ? undefined
          : {
              "Content-Type": "application/json",
              "X-Dev-Request": "1",
              ...(correlation ? { "X-HireZero-Request-Id": correlation } : {}),
            },
      body: body === undefined ? undefined : JSON.stringify(body),
    });
    const received = response.headers.get("X-HireZero-Request-Id") || correlation;
    if (!response.ok) {
      const problem = await response.json().catch(() => ({}));
      const delay = Number(response.headers.get("Retry-After") || problem.retryAfterSeconds);
      const retryAfter =
        response.status === 429 && Number.isFinite(delay) && delay > 0 ? Math.min(Math.ceil(delay), 600) : undefined;
      throw new ApiError(
        problem.error ||
          (response.status === 429
            ? "Too many requests. Wait a moment and try again."
            : `Request failed (${response.status}).`),
        response.status,
        retryAfter,
        received,
        problem.code,
      );
    }
    try {
      return (await response.json()) as T;
    } catch (cause) {
      if (controller.signal.aborted) throw cause;
      throw new ApiError(
        "The server response did not confirm the result. Check status before trying again.",
        0,
        undefined,
        received,
        "outcome-unknown",
      );
    }
  } catch (cause) {
    if (cause instanceof ApiError) throw cause;
    throw new ApiError(
      controller.signal.aborted
        ? "The response timed out. Execution may still be in progress. Check status before trying again."
        : "The response was lost. Execution outcome is unknown. Check status before trying again.",
      0,
      undefined,
      correlation,
      "outcome-unknown",
    );
  } finally {
    if (timer !== undefined) clearTimeout(timer);
  }
}
export const when = (date: string) =>
  new Date(date).toLocaleString([], { month: "short", day: "numeric", hour: "numeric", minute: "2-digit" });
export const label = (status: string) =>
  ({ queued: "Needs review", ready: "In SemaForge", "promotion-pending": "Needs reconciliation" })[status] || status;
export const initials = (text: string) =>
  text
    .trim()
    .split(/\s+/)
    .map((part) => part[0])
    .join("")
    .slice(0, 2)
    .toUpperCase();
export function preference<T>(key: string, fallback: T): T {
  try {
    return JSON.parse(localStorage.getItem("dev-ui:" + key) || "null") ?? fallback;
  } catch {
    return fallback;
  }
}
export function savePreference(key: string, value: unknown) {
  try {
    localStorage.setItem("dev-ui:" + key, JSON.stringify(value));
  } catch {
    /* Cosmetic preferences can remain in memory. */
  }
}
