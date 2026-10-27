import type { Snapshot, TaskSpec } from "./api";

export function planProposal(body: string): { objective: string; budget: number; tasks: TaskSpec[] } | null {
  try {
    const p = JSON.parse(body);
    if (
      !p ||
      typeof p.objective !== "string" ||
      !Number.isSafeInteger(p.budget) ||
      p.budget < 5000 ||
      p.budget > 100000 ||
      !Array.isArray(p.tasks) ||
      p.tasks.length < 1 ||
      p.tasks.length > 4 ||
      !p.tasks.every(
        (t: TaskSpec, index: number) =>
          t &&
          [t.employeeId, t.threadId, t.skill, t.input].every((v) => typeof v === "string" && v.length > 0) &&
          Array.isArray(t.evidence) &&
          t.evidence.every((id) => typeof id === "string") &&
          Array.isArray(t.dependsOn) &&
          t.dependsOn.every((n) => Number.isInteger(n) && n >= 0 && n < index),
      )
    )
      return null;
    return p;
  } catch {
    return null;
  }
}

export function PlanProposal({ body, snapshot }: { body: string; snapshot: Snapshot }) {
  const p = planProposal(body);
  return (
    <div className="co-plan-proposal">
      {p ? (
        <>
          <h3>{p.objective}</h3>
          <p>
            {p.tasks.length} proposed tasks · shared ceiling {p.budget.toLocaleString()} tokens · seven-day execution
            window.
          </p>
          <ol>
            {p.tasks.map((t, i) => (
              <li key={i}>
                <b>{snapshot.data.employees.find((e) => e.id === t.employeeId)?.name ?? "Unknown employee"}</b>
                <p>{t.input}</p>
                <small>
                  {t.dependsOn.length
                    ? "After approved completion of " + t.dependsOn.map((n) => `task ${n + 1}`).join(", ")
                    : "Can start independently"}{" "}
                  · {t.evidence.length} selected evidence items
                </small>
              </li>
            ))}
          </ol>
          <p className="co-muted">
            Review the assignments and sharing of approved dependency results. Approving this draft does not start the
            plan or its model jobs.
          </p>
        </>
      ) : (
        <p role="status">
          This proposal is not a complete structured plan. Ask for a corrected draft before preparing delegation.
        </p>
      )}
      <details>
        <summary>Exact proposal text</summary>
        <pre>{body}</pre>
      </details>
    </div>
  );
}
