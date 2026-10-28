import type { Snapshot, Work } from "./api";

export function recurringInstructions(snapshot: Snapshot, task: Work): string | null {
  const feedback: string[] = [];
  const seen = new Set([task.id]);
  let current = task;
  while (current.revision) {
    feedback.unshift(current.revision.feedback);
    const source = snapshot.data.tasks.find((t) => t.id === current.revision!.sourceTaskId);
    if (
      !source ||
      seen.has(source.id) ||
      seen.size >= 16 ||
      source.employeeId !== task.employeeId ||
      source.threadId !== task.threadId
    )
      return null;
    seen.add(source.id);
    current = source;
  }
  if (!feedback.length) return task.input;
  // Repeating the work must not quietly dismiss the owner's earlier instructions from the table.
  return [
    task.input,
    "Owner revision feedback, oldest first. Keep the original requirements unless a later note explicitly changes them:",
    ...feedback.map((note, index) => `${index + 1}. ${note}`),
  ].join("\n\n");
}

export function canRepeatWork(snapshot: Snapshot, task: Work) {
  const employee = snapshot.data.employees.find((e) => e.id === task.employeeId);
  const role = snapshot.data.roles.find((r) => r.id === employee?.roleId);
  return (
    task.state === "completed" &&
    (task.kind ?? "work") === "work" &&
    !task.resource &&
    employee?.state === "active" &&
    !!role?.measuredSkills?.includes(task.skill) &&
    !role.executionSkills?.includes(task.skill) &&
    recurringInstructions(snapshot, task) !== null &&
    snapshot.data.artifacts.some((a) => a.taskId === task.id && a.state === "approved")
  );
}
