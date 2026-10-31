import type { Snapshot } from "./api";

// Saved conversation order gives repeated requests a name without putting database IDs on the silverware.
export function workContext(snapshot: Snapshot, taskId?: string) {
  const task = snapshot.data.tasks.find((t) => t.id === taskId);
  if (!task) return "";
  const reply = task.kind === "conversation";
  const requests = snapshot.data.tasks.filter(
    (t) => t.threadId === task.threadId && (t.kind === "conversation") === reply,
  );
  const number = requests.findIndex((t) => t.id === task.id) + 1;
  const conversation = snapshot.data.threads.find((t) => t.id === task.threadId)?.title;
  // A number only tells requests apart when there is more than one (dogfood, 2026-10-30).
  return [conversation, requests.length > 1 && `${reply ? "Reply" : "Request"} ${number}`].filter(Boolean).join(" · ");
}

export function WorkContext({ snapshot, taskId }: { snapshot: Snapshot; taskId?: string }) {
  const label = workContext(snapshot, taskId);
  return label ? <small className="co-work-context">{label}</small> : null;
}
