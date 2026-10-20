import type { Role } from "./api";

// Plain words for what the owner reads (owner walkthrough, 2026-10-19): names instead of ids, states as phrases.
// Ids and raw states stay in data attributes and the API, never in visible text.

export function humanize(id: string): string {
  const text = id.replace(/[-_]+/g, " ").trim();
  return text.charAt(0).toUpperCase() + text.slice(1);
}

// A skill's or action's name as the employee's module declares it.
export function workName(role: Role | undefined, skill: string): string {
  if (skill === "chat") return "Conversation";
  return role?.skillNames?.[skill] ?? role?.actions?.find((a) => a.id === skill)?.name ?? humanize(skill);
}

export function roleName(roles: Role[], roleId: string): string {
  return roles.find((r) => r.id === roleId)?.name ?? humanize(roleId);
}

const taskStates: Record<string, string> = {
  queued: "Queued",
  blocked: "Waiting",
  running: "Working",
  gathering: "Gathering evidence",
  awaiting_review: "Waiting for your review",
  completed: "Done",
  failed: "Failed",
  cancelled: "Cancelled",
  canceled: "Cancelled",
  unknown: "Outcome unknown",
};
export const taskState = (state: string) => taskStates[state] ?? humanize(state);

const artifactStates: Record<string, string> = {
  needs_review: "Needs your review",
  approved: "Approved",
  changes_requested: "Changes requested",
  rejected: "Rejected",
  superseded: "Replaced by a newer version",
};
export const artifactState = (state: string) => artifactStates[state] ?? humanize(state);

const employeeStates: Record<string, string> = {
  active: "Active",
  onboarding: "Onboarding",
  paused: "Paused",
  retired: "Retired",
};
export const employeeState = (state: string) => employeeStates[state] ?? humanize(state);

const publicationStates: Record<string, string> = {
  proposed: "proposed",
  approved: "approved",
  publishing: "being sent",
  published: "published",
  unknown: "outcome unknown",
  revoked: "revoked",
  declined: "declined",
  failed: "failed",
};
export const publicationState = (state: string) => publicationStates[state] ?? humanize(state).toLowerCase();

// Where an artifact came from: an action the employee ran, the employee's own draft, or the owner.
export function provenance(role: Role | undefined, value: string): string {
  if (value.startsWith("action:")) return "From “" + workName(role, value.slice("action:".length)) + "”";
  if (value === "owner") return "Written by you";
  if (value === "model-output") return "Draft by the employee";
  return humanize(value);
}
