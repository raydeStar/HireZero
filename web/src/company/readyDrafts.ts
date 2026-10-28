import type { Snapshot, Work } from "./api";

// A private text result can be useful before the owner promotes it into shared work.
// This is presentation only: the approval ledger keeps its keys, and its dignity.
export function readyDraft(snapshot: Snapshot, task: Work | undefined): boolean {
  if (
    !task ||
    task.state !== "awaiting_review" ||
    (task.kind ?? "work") !== "work" ||
    task.planId ||
    task.meetingId ||
    task.scheduleRunId ||
    task.contextScope ||
    task.resource ||
    task.dependencies?.length
  )
    return false;
  const d = snapshot.data;
  const thread = d.threads.find((thread) => thread.id === task.threadId);
  if (!thread || (thread.scope ?? "employee") !== "employee") return false;
  if (d.tasks.some((other) => other.dependencies?.includes(task.id))) return false;
  const artifact = d.artifacts.filter((artifact) => artifact.taskId === task.id).at(-1);
  return (
    artifact?.state === "needs_review" &&
    artifact.provenance === "model-output" &&
    !artifact.reviewFeedback &&
    !!d.measured?.some((attempt) => attempt.taskId === task.id && attempt.phase === "committed")
  );
}

export function optionalDraftReview(
  snapshot: Snapshot,
  need: { kind: string; taskId?: string; priority: string },
): boolean {
  return (
    need.kind === "review" &&
    need.priority === "normal" &&
    readyDraft(
      snapshot,
      snapshot.data.tasks.find((task) => task.id === need.taskId),
    )
  );
}
