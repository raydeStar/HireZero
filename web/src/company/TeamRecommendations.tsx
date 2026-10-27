import type { Employee, Snapshot } from "./api";
import { approvedBusinessProfile, teamRecommendations } from "../roles/company";

export function TeamRecommendations({
  snapshot,
  disabled,
  onHire,
  onEmployee,
}: {
  snapshot: Snapshot;
  disabled: boolean;
  onHire: (role?: string) => void;
  onEmployee: (employee: Employee) => void;
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
                  {existing.map((e) => (
                    <button key={e.id} disabled={disabled} onClick={() => onEmployee(e)}>
                      {e.state === "onboarding" ? "Continue onboarding " : "Open "}
                      {e.name}
                    </button>
                  ))}
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
