import type { Snapshot, Work } from "./api";

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
    snapshot.data.artifacts.some((a) => a.taskId === task.id && a.state === "approved")
  );
}
