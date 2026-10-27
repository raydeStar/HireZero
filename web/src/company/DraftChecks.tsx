import type { DraftCheck } from "../roles/company";

export function DraftChecks({ checks }: { checks: DraftCheck[] }) {
  if (!checks.length) return null;
  const failures = checks.filter((check) => check.state !== "pass").length;
  return (
    <details className="co-draft-checks" open={checks.some((check) => check.state === "fail")}>
      <summary>Draft checks · {failures ? `${failures} to review` : "format checks passed"}</summary>
      <p className="co-muted">
        Local counts and label checks. Read the draft for accuracy, audience fit and usefulness; passing these checks
        does not approve it.
      </p>
      <ul>
        {checks.map((check) => (
          <li key={check.label}>
            <strong>
              <span aria-hidden="true">{check.state === "pass" ? "✓" : check.state === "fail" ? "✗" : "?"} </span>
              {check.label}
            </strong>
            : {check.detail}
            <span className="co-muted">
              {" "}
              · {check.state === "pass" ? "Pass" : check.state === "fail" ? "Outside limit" : "Review"}
            </span>
          </li>
        ))}
      </ul>
    </details>
  );
}

export function draftRepairFeedback(checks: DraftCheck[]): string | undefined {
  const failed = checks.filter((check) => check.state === "fail");
  return failed.length
    ? `Please correct these measured draft issues: ${failed.map((check) => `${check.label}: ${check.detail}`).join("; ")}. Preserve the original deliverables, earlier feedback, useful examples and factual boundaries. Return the complete replacement draft.`
    : undefined;
}
