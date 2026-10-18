import { api, ApiError } from "../lib/api";
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
export type Thread = { id: string; employeeId: string; title: string; scope?: string };
export type Work = {
  id: string;
  employeeId: string;
  threadId: string;
  skill: string;
  input: string;
  state: string;
  reason: string | null;
  version: number;
  rootId: string;
  watermark: number;
  kind?: string;
  modelRoute?: string | null;
  runtime?: string | null;
  resource?: string | null;
  allowNetwork?: boolean;
};
export type Artifact = {
  id: string;
  taskId: string;
  employeeId: string;
  body: string;
  digest: string;
  state: string;
  provenance: string;
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
export type Event = { sequence: number; type: string; id: string; employeeId: string | null; text: string; at: string };
// Hiring copy from the employee module's manifest (docs/adr/0002-employee-module-contract.md).
export type RoleOnboarding = {
  department: string;
  name: string;
  responsibility: string;
  help: Record<string, string>;
  boundaries: string;
};
export type Role = {
  id: string;
  name: string;
  skills: string[];
  onboardingFields: string[];
  measuredSkills?: string[];
  executionSkills?: string[];
  publishingSkills?: string[];
  summary?: string | null;
  onboarding?: RoleOnboarding | null;
  tools?: { id: string; risk: "read" | "write" | "external" | "spend"; purpose: string }[] | null;
  kpis?: { id: string; name: string; unit: string; description: string }[] | null;
  uiSlots?: string[];
  actions?: {
    id: string;
    name: string;
    description: string;
    fields: { id: string; label: string; maxLength: number }[];
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
    modelExecution: boolean;
    measuredOneJob?: boolean;
    conversationalReplies?: boolean;
    providerStatus: string;
    integrations?: IntegrationStatus[];
  };
  data: {
    departments: Department[];
    employees: Employee[];
    knowledge: Knowledge[];
    threads: Thread[];
    tasks: Work[];
    artifacts: Artifact[];
    roles: Role[];
    plans: Plan[];
    meetings: Meeting[];
    measured?: MeasuredEntry[];
    handoffs?: Handoff[];
    cursor: number;
    budgets: { id: string; limit: number; used: number; reserved: number }[];
  };
};
export type ThreadDetail = {
  thread: Thread;
  messages: { id: string; author: string; body: string; sequence: number }[];
  notes: { body: string; version: number };
};
function pending() {
  try {
    return sessionStorage.getItem("hirezero-company-operation");
  } catch {
    return null;
  }
}
function remember(id: string | null) {
  try {
    if (id) sessionStorage.setItem("hirezero-company-operation", id);
    else sessionStorage.removeItem("hirezero-company-operation");
  } catch {
    /* A restricted browser still gets the current request's reconciliation. */
  }
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
) {
  const uncertain = pending();
  if (uncertain) {
    try {
      await api<unknown>("/company/operations/" + uncertain);
      remember(null);
    } catch {
      throw new Error(
        "A previous operation remains unconfirmed. Refresh and inspect the saved state before proceeding. Operation: " +
          uncertain,
      );
    }
    throw new Error("The previous operation is confirmed. Refresh the saved state, then choose the next action.");
  }
  const requestId = crypto.randomUUID();
  // Guided multi-step saves opt into a durable pre-dispatch receipt; ordinary in-flight work remains cancelable.
  if (onAttempt) {
    remember(requestId);
    onAttempt(requestId);
  }
  try {
    const result = await api<unknown>("/company/commands", "POST", { requestId, action, ...fields });
    if (pending() === requestId) remember(null);
    return result;
  } catch (error) {
    if (error instanceof ApiError && error.status === 0) {
      remember(requestId);
      try {
        const result = await api<unknown>("/company/operations/" + requestId);
        if (pending() === requestId) remember(null);
        return result;
      } catch {
        throw new UnconfirmedCompanyOperation(requestId);
      }
    }
    if (pending() === requestId) remember(null);
    throw error;
  }
}

// Runs one declared employee action; retrying with the same request ID returns the recorded task without rerunning it.
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
