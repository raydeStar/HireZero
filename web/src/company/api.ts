import { memberStorage as sessionStorage } from "../lib/api";
import type { Spend } from "./Budgets";
import type { CompanyWorkControl } from "./WorkControl";
import { api, ApiError, company } from "../lib/api";
export type Employee = {
  id: string;
  departmentId: string;
  roleId: string;
  name: string;
  responsibility: string;
  state: string;
  version: number;
};
export type Department = { id: string; name: string; guide: string | null; knowledge: string | null; version: number };
export type Knowledge = {
  id: string;
  departmentId: string | null;
  body: string;
  digest: string;
  state: string;
  version: number;
};
export type BusinessIdentity = {
  id: string;
  name: string;
  website: string | null;
  goal: string;
  location: string;
  industry: string;
  state: string;
  version: number;
  digest: string;
  updatedAt: string;
};
export type BusinessSource = {
  id: string;
  businessVersion: number;
  url: string;
  title: string;
  digest: string;
  retrievedAt: string;
  clipped: boolean;
  text?: string;
};
export type ProfileField = {
  key: string;
  value: string;
  verification: "owner" | "sourced" | "hypothesis" | "unknown";
  citations: { sourceId: string; quote: string }[];
};
export type BusinessProfile = {
  id: string;
  businessVersion: number;
  fields: ProfileField[];
  digest: string;
  state: string;
  version: number;
  updatedAt: string;
  knowledgeId?: string | null;
};
export type BusinessProfileRevision = {
  id: string;
  sequence: number;
  profile: BusinessProfile;
  origin: "owner" | "model" | "earlier";
  actorName: string;
  recordedAt: string;
  changedFields: string[];
  draftId?: string | null;
};
export type BusinessProfileHistory = { items: BusinessProfileRevision[]; before: number | null };
export type BusinessDraftJob = {
  id: string;
  businessVersion: number;
  sourceIds: string[];
  job: MeasuredEntry["job"];
  state: string;
  version: number;
  updatedAt: string;
  profileId?: string | null;
  error?: string | null;
  usage?: { inputTokens: number; outputTokens: number; billedUsd?: number | null } | null;
  reportedUsage?: number | null;
  providerReceipt?: string | null;
  localRefusalReceipt?: string | null;
  rejectedResponse?: string | null;
};
export type BusinessLookup = {
  id: string;
  kind: "search" | "discovery";
  businessVersion: number;
  state: string;
  query: string;
  startedAt: string;
  dispatched: boolean;
  sourceIds?: string[];
  candidates?: { url: string; title: string; description: string }[];
  notices?: string[];
};
export type Thread = { id: string; employeeId: string; title: string; scope?: string };
export type Work = {
  id: string;
  employeeId: string;
  threadId: string;
  skill: string;
  input: string;
  evidence?: string[];
  dependencies?: string[];
  state: string;
  reason: string | null;
  version: number;
  rootId: string;
  watermark: number;
  kind?: string;
  modelRoute?: string | null;
  requestedModelRoute?: string | null;
  revision?: {
    sourceTaskId: string;
    artifactId: string;
    artifactVersion: number;
    artifactDigest: string;
    feedback: string;
  } | null;
  runtime?: string | null;
  resource?: string | null;
  allowNetwork?: boolean;
  // For a context draft: "company" for the company-wide facts.
  contextScope?: string | null;
  planId?: string | null;
  meetingId?: string | null;
  scheduleRunId?: string | null;
};
export type ConnectionRequest = {
  id: string;
  integrationId: string;
  name: string;
  // None when an assistant asked through HireZero's MCP endpoint (docs/adr/0048).
  threadId: string | null;
  employeeId: string | null;
  reason: string;
  state: "open" | "connected" | "dismissed";
  from?: string | null;
  keySource?: string | null;
  secretName?: string | null;
  version: number;
  taskId?: string | null;
  account?: string | null;
};
export type Artifact = {
  contextId?: string | null;
  reviewFeedback?: string | null;
  id: string;
  taskId: string;
  employeeId: string;
  body: string;
  digest: string;
  state: string;
  provenance: string;
  sandboxOrigin?: {
    jobId: string;
    jobDigest: string;
    resultDigest: string;
    outputName: string;
    outputDigest: string;
    start: number;
    length: number;
  };
  browserOrigin?: {
    jobId: string;
    jobDigest: string;
    resultDigest: string;
    step: number;
    url: string;
    capturedAt: string;
    partial: boolean;
    textDigest: string;
    start: number;
    length: number;
  };
  version: number;
};
export type TaskSpec = {
  employeeId: string;
  threadId: string;
  skill: string;
  input: string;
  evidence: string[];
  dependsOn: number[];
};
export type Plan = {
  id: string;
  ceoId: string;
  objective: string;
  tasks: TaskSpec[];
  digest: string;
  state: string;
  version: number;
  budget: number;
  rootId: string;
  taskIds: string[];
  deadline: string;
  executionHours?: number | null;
  source?: { artifactId: string; version: number; digest: string } | null;
};
export type Meeting = {
  id: string;
  title: string;
  agenda: string;
  participants: string[];
  chairId: string;
  state: string;
  version: number;
  turns: { employeeId: string; body: string; taskId: string }[];
  activeTask: string | null;
  outcome: string | null;
  outcomeDigest: string | null;
};
export type Event = {
  sequence: number;
  type: string;
  id: string;
  employeeId: string | null;
  text: string;
  at: string;
  // The member who acted, or "system".
  actor?: string;
};
// Hiring copy from the employee module's manifest (docs/adr/0002-employee-module-contract.md).
export type RoleOnboarding = {
  department: string;
  name: string;
  responsibility: string;
  help: Record<string, string>;
  // Fields that may be left blank when department context is saved.
  optional?: string[];
  boundaries: string;
};
// One week's scorecard numbers for an employee (docs/adr/0170-goal-ladder-and-weekly-numbers.md).
export type Reading = {
  id: string;
  employeeId: string;
  week: string;
  values: { name: string; value: number }[];
  recordedBy: string;
  recordedAt: string;
  version: number;
};
export type Role = {
  unavailableReason?: string | null;
  id: string;
  name: string;
  skills: string[];
  onboardingFields: string[];
  measuredSkills?: string[];
  executionSkills?: string[];
  publishingSkills?: string[];
  // Skills whose scheduled results are a shift's rounds (docs/adr/0171-an-owner-approved-shift.md).
  shiftSkills?: string[];
  // Skill id to its display name, as the module declares it.
  skillNames?: Record<string, string>;
  summary?: string | null;
  onboarding?: RoleOnboarding | null;
  tools?: { id: string; risk: "read" | "write" | "external" | "spend"; purpose: string }[] | null;
  kpis?: { id: string; name: string; unit: string; description: string }[] | null;
  uiSlots?: string[];
  actions?: {
    id: string;
    name: string;
    description: string;
    fields: ActionFieldInfo[];
  }[];
};
export type MeasuredEntry = {
  id: string;
  taskId: string;
  version: number;
  phase: string;
  error: string | null;
  reportedUsage: number | null;
  usage: { inputTokens: number; outputTokens: number; reasoningTokens: number | null } | null;
  job: {
    state: string;
    digest: string;
    contextDigest: string;
    instructionsDigest: string;
    runtimeIdentity: string;
    route: string;
    terms?: { provider: string; allowanceUnit: "credits" | "usd-cents" } | null;
    creditAllowance: number;
    expiresAt: string;
    applicationBytes: number;
    policy: {
      version: string;
      nativeRequests: number;
      applicationBytes: number;
      baselineInputAllowance: number;
      packetHeadroom: number;
      outputTarget: number;
      planningTokens: number;
      localDeadlineSeconds: number;
      visibleCharacterStop: number;
      effort?: string;
    };
  };
};
export type Handoff = {
  id: string;
  artifactId: string;
  targetEmployeeId: string;
  targetThreadId: string;
  targetTaskId: string | null;
  artifactVersion: number | null;
  state: string;
  version: number;
  digest: string;
  body: string;
};
export type IntegrationStatus = { id: string; name: string; active: boolean; reason: string };
export type Snapshot = {
  capabilities: {
    liveCoding?: boolean;
    sandboxExecution?: boolean;
    sandboxRecovery?: boolean;
    sandboxOwner?: boolean;
    browserExecution?: boolean;
    browserRecovery?: boolean;
    browserOwner?: boolean;
    costOwner?: boolean;
    modelExecution: boolean;
    measuredOneJob?: boolean;
    businessSearch?: boolean;
    conversationalReplies?: boolean;
    // "usd-cents" when an API model enforces the allowance as a spending cap; Codex credits are only intended.
    signupCompany?: boolean;
    signupInvites?: boolean;
    freeAllowance?: { remainingCents: number; companyRemainingCents: number; dailyRemainingCents: number } | null;
    allowanceUnit?: "credits" | "usd-cents";
    modelChoices?: import("./ModelChoice").ModelOption[];
    providerStatus: string;
    integrations?: IntegrationStatus[];
  };
  data: {
    workControl?: CompanyWorkControl;
    departments: Department[];
    employees: Employee[];
    knowledge: Knowledge[];
    threads: Thread[];
    tasks: Work[];
    sandboxTaskIds?: string[];
    browserTaskIds?: string[];
    browserRunningTaskIds?: string[];
    artifacts: Artifact[];
    roles: Role[];
    plans: Plan[];
    meetings: Meeting[];
    measured?: MeasuredEntry[];
    handoffs?: Handoff[];
    cursor: number;
    budgets: { id: string; limit: number; used: number; reserved: number; version?: number }[];
    budgetLabels?: Record<string, string>;
    // Model spend in US dollars from recorded usage (owner walkthrough, 2026-10-19).
    spend?: Spend;
    // Connect cards, open and closed (docs/adr/0046-connect-cards.md).
    connections?: ConnectionRequest[];
    readings?: Reading[];
    // When each task's first and latest events happened, by task id.
    taskTimes?: Record<string, { started: string; updated: string }>;
    // The company-wide fact fields every employee inherits, in order.
    companyFields?: { id: string; help: string }[];
    business?: BusinessIdentity | null;
    businessProfiles?: BusinessProfile[];
    businessSources?: BusinessSource[];
    businessDrafts?: BusinessDraftJob[];
    businessLookups?: BusinessLookup[];
  };
};
export type ThreadDetail = {
  thread: Thread;
  messages: {
    id: string;
    author: string;
    body: string;
    sequence: number;
    attachments?: import("./conversationAttachments").ConversationAttachment[];
  }[];
  notes: { body: string; version: number };
};
const operationKey = "hirezero-company-operation-v2:" + (company() || "local-company");
const legacyOperationKey = "hirezero-company-operation";
const remembered = new Map<string, string | null>();
const memoryOnly = new Set<string>();
function pending(key = operationKey) {
  if (memoryOnly.has(key)) return remembered.get(key) ?? null;
  try {
    const id = sessionStorage.getItem(key);
    remembered.set(key, id);
    return id;
  } catch {
    return remembered.get(key) ?? null;
  }
}
function remember(id: string | null, key = operationKey) {
  remembered.set(key, id);
  try {
    if (id) sessionStorage.setItem(key, id);
    else sessionStorage.removeItem(key);
    memoryOnly.delete(key);
  } catch {
    // A denied write must not let a later readable, stale value erase an uncertain attempt.
    memoryOnly.add(key);
  }
}
function forgetConfirmed(requestId: string) {
  for (const key of [operationKey, legacyOperationKey]) {
    if (pending(key) === requestId) remember(null, key);
  }
}
export async function reconcileCompanyOperation(requestId: string) {
  const result = await api<unknown>("/company/operations/" + encodeURIComponent(requestId));
  forgetConfirmed(requestId);
  return result;
}
export class UnconfirmedCompanyOperation extends Error {
  constructor(public requestId: string) {
    super(
      "The operation outcome is unconfirmed. Refresh the saved view before submitting again. Operation: " + requestId,
    );
  }
}
// Keep one attempt's operation ID until its outcome is known. A lost HTTP reply is reconciled first.
export async function command(
  action: string,
  fields: Record<string, unknown> = {},
  onAttempt?: (requestId: string) => void,
  endpoint = "/company/commands",
) {
  const scoped = pending(),
    legacy = pending(legacyOperationKey),
    uncertain = scoped || legacy;
  if (uncertain) {
    try {
      await reconcileCompanyOperation(uncertain);
    } catch {
      throw new Error(
        (scoped
          ? "A previous operation remains unconfirmed. Refresh and inspect the saved state before proceeding. Operation: "
          : "A previous operation remains unconfirmed. Switch to the company where you started it and check its saved state before submitting again. Operation: ") +
          uncertain,
      );
    }
    throw new Error("The previous operation is confirmed. Refresh the saved state, then choose the next action.");
  }
  const requestId = crypto.randomUUID();
  // Guided multi-step saves opt into a durable pre-dispatch receipt; ordinary in-flight work remains cancelable.
  if (onAttempt) {
    remember(requestId);
    try {
      onAttempt(requestId);
    } catch (error) {
      // A failed pre-dispatch receipt write is a known refusal, not a missing provider result.
      forgetConfirmed(requestId);
      throw error;
    }
  }
  try {
    const result = await api<unknown>(endpoint, "POST", { requestId, action, ...fields });
    forgetConfirmed(requestId);
    return result;
  } catch (error) {
    if (error instanceof ApiError && error.status === 0) {
      remember(requestId);
      try {
        return await reconcileCompanyOperation(requestId);
      } catch {
        throw new UnconfirmedCompanyOperation(requestId);
      }
    }
    forgetConfirmed(requestId);
    throw error;
  }
}

// Runs one declared employee action; retrying with the same request ID returns the recorded task without rerunning it.
export type ActionFieldInfo = {
  id: string;
  label: string;
  maxLength: number;
  choices?: string[] | null;
  // The module lists this field's current values when the form opens (docs/adr/0037-loaded-action-choices.md).
  loadsChoices?: boolean;
};
export type ActionChoice = { value: string; label: string };

export function actionChoices(employeeId: string, action: string, field: string) {
  const query = new URLSearchParams({ employee: employeeId, action, field });
  return api<ActionChoice[]>("/company/actions/choices?" + query);
}

export function runAction(
  requestId: string,
  employeeId: string,
  threadId: string,
  action: string,
  input: Record<string, string>,
) {
  return api<unknown>("/company/actions", "POST", {
    requestId,
    action: "action.start",
    employeeId,
    threadId,
    skill: action,
    body: JSON.stringify(input),
  });
}

// One step of reviewed execution (docs/adr/0012-company-publication.md). Steps are single use in the store, so a
// retried request is refused instead of repeated; refresh the task to see where it stands.
export type ExecutionStep = "prepare" | "run" | "propose" | "publish" | "reconcile";
export function executionStep(step: ExecutionStep, id: string) {
  return api<unknown>("/company/executions/" + step + "/" + encodeURIComponent(id), "POST", {});
}
