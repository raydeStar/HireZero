import { api } from "../lib/api";
import {
  command,
  reconcileCompanyOperation,
  UnconfirmedCompanyOperation,
  type BusinessIdentity,
  type BusinessLookup,
  type Knowledge,
  type MeasuredEntry,
  type Work,
} from "./api";

export type GoalTerms = Pick<MeasuredEntry["job"], "route" | "runtimeIdentity" | "terms" | "policy">;
type Prepared = { task: Work; attempt: { id: string; version: number; measured: MeasuredEntry["job"] } };
export type GoalFlow = {
  stage: "context" | "lookup" | "sources" | "prepare" | "run" | "done";
  fields: Record<string, unknown>;
  input: Record<string, unknown>;
  terms: GoalTerms;
  pending?: string;
  context?: { knowledge: Knowledge; business: BusinessIdentity };
  lookupId?: string;
  prepared?: Prepared;
  stopped?: boolean;
};

// Follow the saved request's revisions; the butler should not bring back the rejected draft.
export function currentGoalWork(tasks: readonly Work[], taskId?: string): Work | undefined {
  const original = tasks.find((task) => task.id === taskId);
  if (!original) return undefined;
  let current = original;
  const seen = new Set([current.id]);
  while (current.state === "superseded") {
    const revisions = tasks.filter(
      (task) =>
        task.revision?.sourceTaskId === current.id &&
        task.employeeId === original.employeeId &&
        task.threadId === original.threadId,
    );
    if (revisions.length !== 1) break;
    const next = revisions[0];
    if (seen.has(next.id) || seen.size >= 16) return original;
    seen.add(next.id);
    current = next;
  }
  return current;
}

export function sameGoalTerms(job: GoalTerms, approved: GoalTerms) {
  return (
    job.route === approved.route &&
    job.runtimeIdentity === approved.runtimeIdentity &&
    JSON.stringify(job.terms ?? null) === JSON.stringify(approved.terms ?? null) &&
    JSON.stringify(job.policy) === JSON.stringify(approved.policy)
  );
}

// Persist the next step before any mutation. A lost reply is an invitation to read, never to run again.
export async function advanceGoal(initial: GoalFlow, save: (flow: GoalFlow) => void): Promise<Work | undefined> {
  let flow = initial;
  const update = (patch: Partial<GoalFlow>) => {
    flow = { ...flow, ...patch };
    save(flow);
  };
  const perform = async (action: string, fields: Record<string, unknown>) => {
    if (flow.pending) return reconcileCompanyOperation(flow.pending);
    try {
      return await command(action, fields, (pending) => update({ pending }));
    } catch (error) {
      if (!(error instanceof UnconfirmedCompanyOperation)) update({ pending: undefined, stopped: true });
      throw error;
    }
  };
  if (flow.stopped) throw new Error("This request stopped. Review the saved setup before starting new work.");
  while (flow.stage !== "done") {
    if (flow.stage === "context") {
      const context = (await perform("goal.context", {
        ...flow.fields,
        body: JSON.stringify(flow.input),
      })) as GoalFlow["context"];
      if (!context?.knowledge?.id || !context.business?.id)
        throw new Error("Saved business details could not be verified.");
      update({ context, pending: undefined, stage: flow.input.website ? "lookup" : "prepare" });
    } else if (flow.stage === "lookup") {
      const identity = flow.context!.business;
      const lookup = (await perform("goal.lookup.start", {
        expectedVersion: identity.version,
        digest: identity.digest,
        decision: "authorize-one-lookup",
        body: flow.input.website,
      })) as BusinessLookup;
      if (!lookup.id) throw new Error("The website lookup could not be identified.");
      update({ lookupId: lookup.id, pending: undefined, stage: "sources" });
    } else if (flow.stage === "sources") {
      const lookup = await api<BusinessLookup>("/company/business/lookups/" + encodeURIComponent(flow.lookupId!));
      if (lookup.state === "running") return undefined;
      if (!["completed", "partial", "unknown"].includes(lookup.state)) {
        update({ stopped: true });
        throw new Error("Website context changed. Review your saved business details before continuing.");
      }
      update({ stage: "prepare" });
    } else if (flow.stage === "prepare") {
      const knowledge = flow.context!.knowledge;
      const prepared = (await perform("goal.prepare", {
        ...flow.fields,
        id: knowledge.id,
        expectedVersion: knowledge.version,
        digest: knowledge.digest,
        body: JSON.stringify({ ...flow.input, websiteLookupId: flow.lookupId }),
      })) as Prepared;
      if (!prepared?.task?.id || !prepared?.attempt?.measured)
        throw new Error("The prepared draft could not be identified.");
      update({ prepared, pending: undefined, stage: "run" });
    } else {
      const prepared = flow.prepared!;
      if (
        !sameGoalTerms(prepared.attempt.measured, flow.terms) ||
        prepared.attempt.measured.creditAllowance !== Number(flow.fields.creditAllowance)
      ) {
        update({ stopped: true });
        throw new Error(
          "The model or run terms changed. Your request is saved; review the new terms in your work before running it.",
        );
      }
      await perform("measured.run", {
        id: prepared.attempt.id,
        expectedVersion: prepared.attempt.version,
        digest: prepared.attempt.measured.digest,
        decision: "accept-soft-limits",
      });
      update({ pending: undefined, stage: "done" });
    }
  }
  return flow.prepared!.task;
}
