import type { Employee, Snapshot, Work } from "./api";
import { approvedBusinessProfile, teamRecommendations } from "../roles/company";

export function TeamRecommendations({
  snapshot,
  disabled,
  onHire,
  onEmployee,
  onWork,
}: {
  snapshot: Snapshot;
  disabled: boolean;
  onHire: (role?: string) => void;
  onEmployee: (employee: Employee) => void;
  onWork: (task: Work) => void;
}) {
  if (!approvedBusinessProfile(snapshot)) return null;
  const suggestions = teamRecommendations(snapshot);
  return (
    <section className="co-business-next" aria-label="Suggested employees">
      <h3>Your brief is ready. Choose your next step.</h3>
      <p>Start with one useful assignment. Review the reasons below, or browse all roles to choose your own.</p>
      <div className="co-team-suggestions">
        {suggestions.map(({ role, reason }) => {
          const existing = snapshot.data.employees.filter((e) => e.roleId === role.id && e.state !== "retired");
          return (
            <article key={role.id} data-role-suggestion={role.id}>
              <h4>{role.name}</h4>
              <p>{reason}</p>
              {existing.length ? (
                <>
                  <p className="co-muted">Already on your team. Continue with an existing employee.</p>
                  {existing.map((e) => {
                    const latest = [...snapshot.data.tasks]
                      .reverse()
                      .filter(
                        (task) =>
                          task.employeeId === e.id &&
                          ["awaiting_review", "completed"].includes(task.state) &&
                          snapshot.data.artifacts.some((a) => a.taskId === task.id),
                      )
                      .sort(
                        (a, b) =>
                          (Date.parse(snapshot.data.taskTimes?.[b.id]?.updated ?? "") || 0) -
                          (Date.parse(snapshot.data.taskTimes?.[a.id]?.updated ?? "") || 0),
                      )[0];
                    return (
                      <div key={e.id} className="co-actions">
                        <button disabled={disabled} onClick={() => onEmployee(e)}>
                          {e.state === "onboarding" ? "Continue onboarding " : "Open "}
                          {e.name}
                        </button>
                        {latest && e.state !== "onboarding" && (
                          <button
                            disabled={disabled}
                            aria-label={`Open latest result from ${e.name}`}
                            onClick={() => onWork(latest)}
                          >
                            Open latest result
                          </button>
                        )}
                      </div>
                    );
                  })}
                </>
              ) : (
                <button className="co-primary" disabled={disabled} onClick={() => onHire(role.id)}>
                  Set up a {role.name} employee
                </button>
              )}
            </article>
          );
        })}
      </div>
      <button disabled={disabled} onClick={() => onHire()}>
        Browse all employee roles
      </button>
    </section>
  );
}
