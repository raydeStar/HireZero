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
export { ApiError, api, preference, savePreference, type ApiOptions } from "../lib/api";
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
