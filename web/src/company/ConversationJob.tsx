import { useState, type FormEvent } from "react";
import { api } from "../lib/api";
import type { Snapshot, Work } from "./api";
import { MeasuredJob } from "./MeasuredJob";
import { ArtifactHandoff } from "./ArtifactHandoff";
import { ArtifactPublication, ExecutionSteps } from "./ExecutionSteps";
const reasonLabels: Record<string, string> = {
  "handoff-awaiting-approval": "Assigned from an approved handoff. Separate execution approval is required.",
  "execution-unavailable": "Reviewed execution needs a granted checkout and your approval of its exact proposal.",
  // Tasks saved before the reason was made role-neutral.
  "developer-execution-unavailable": "Repository execution is unavailable. This task is saved for review.",
  "provider-unavailable": "Live model execution is unavailable. Your task is saved.",
  dependencies: "Waiting for an owner-reviewed dependency.",
  "usage-unknown": "Provider usage is uncertain; the reservation remains held.",
};

function Submit({ children = "Save", busy = false }: { children?: string; busy?: boolean }) {
  return (
    <button className="co-primary" disabled={busy} type="submit">
      {children}
    </button>
  );
}
type CodeReviewData = {
  id: string;
  state: string;
  route: string;
  review: null | { baseRevision: string; digest: string; files: { path: string; change: string; patch: string }[] };
};
function CodeReview({ taskId }: { taskId: string }) {
  const [items, setItems] = useState<CodeReviewData[] | null>(null),
    [error, setError] = useState("");
  return (
    <details
      onToggle={(event) => {
        if (event.target === event.currentTarget && event.currentTarget.open)
          void api<{ executions: CodeReviewData[] }>("/company/tasks/" + taskId)
            .then((result) => {
              setItems(result.executions);
              setError("");
            })
            .catch((e) => setError(e.message));
      }}
    >
      <summary>Repository diff for review</summary>
      <p className="co-muted">
        File content captured against the proposed Git revision, including any pre-existing uncommitted changes. Content
        approval does not merge or publish code.
      </p>
      {error && <p role="alert">{error}</p>}
      {items === null && !error && <p>Loading saved review.</p>}
      {items?.length === 0 && <p>No repository execution or diff is recorded for this task.</p>}
      {items?.map((item) => (
        <article key={item.id}>
          <p>
            {item.state} · {item.route.replace("openai/", "")}
          </p>
          {item.review ? (
            <>
              <small>
                Base {item.review.baseRevision.slice(0, 12)} · review {item.review.digest.slice(0, 12)}
              </small>
              {item.review.files.length === 0 ? (
                <p>No checkout changes.</p>
              ) : (
                item.review.files.map((file) => (
                  <details key={file.path}>
                    <summary>
                      {file.change}: {file.path}
                    </summary>
                    <pre>{file.patch || "Empty-file change; see its added/deleted status."}</pre>
                  </details>
                ))
              )}
            </>
          ) : (
            <p>A captured diff is not available.</p>
          )}
        </article>
      ))}
    </details>
  );
}

export function ConversationJob({
  task: t,
  snapshot,
  busy,
  run,
  refresh,
  open,
}: {
  task: Work;
  snapshot: Snapshot;
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  refresh: () => Promise<void>;
  open: (employeeId: string, threadId: string) => void;
}) {
  const d = snapshot.data,
    employee = d.employees.find((e) => e.id === t.employeeId)!,
    employeeRole = d.roles.find((r) => r.id === employee.roleId);
  const [outcome, setOutcome] = useState("");
  function form(fn: () => void | Promise<void>) {
    return (e: FormEvent) => {
      e.preventDefault();
      void fn();
    };
  }
  return (
    <article className="co-card co-job-card" data-task-id={t.id} aria-label={t.skill + " job"}>
      <b>
        {t.skill} · {t.state}
      </b>
      <p>{t.input}</p>
      {d.handoffs
        ?.filter((h) => h.targetTaskId === t.id)
        .map((h) => (
          <div className="co-handoff" key={h.id}>
            <b>{h.state === "shared" ? "Owner-approved handoff" : "Handoff revoked"}</b>
            <p>
              One approved artifact is attached to this task only. Repository execution still requires separate
              qualification and approval.
            </p>
            <pre>{h.body}</pre>
            {h.state === "shared" && (
              <button
                disabled={busy}
                onClick={() => void run("handoff.revoke", { id: h.id, expectedVersion: h.version })}
              >
                Revoke this handoff
              </button>
            )}
          </div>
        ))}
      {employeeRole?.measuredSkills?.includes(t.skill) && (
        <MeasuredJob
          key={d.measured?.find((m) => m.taskId === t.id)?.job.digest ?? t.id}
          task={t}
          entry={d.measured?.find((m) => m.taskId === t.id)}
          available={!!snapshot.capabilities.measuredOneJob}
          cents={snapshot.capabilities.allowanceUnit === "usd-cents"}
          busy={busy}
          prepare={run}
          refresh={refresh}
        />
      )}
      {employeeRole?.publishingSkills?.includes(t.skill) && !employeeRole.executionSkills?.includes(t.skill) && (
        <ArtifactPublication
          task={t}
          artifacts={d.artifacts.filter((a) => a.taskId === t.id)}
          busy={busy}
          run={run}
          refresh={refresh}
        />
      )}
      {employeeRole?.executionSkills?.includes(t.skill) && (
        <>
          <ExecutionSteps
            task={t}
            publishes={!!employeeRole.publishingSkills?.includes(t.skill)}
            busy={busy}
            run={run}
            refresh={refresh}
          />
          <CodeReview taskId={t.id} />
        </>
      )}
      {t.modelRoute && t.kind !== "action" && (
        <p className="co-muted">
          {t.runtime}: {t.modelRoute.replace("openai/", "")}
        </p>
      )}
      {t.reason && (
        <p className="co-muted">
          {t.reason === "provider-unavailable" &&
          snapshot.capabilities.measuredOneJob &&
          employeeRole?.measuredSkills?.includes(t.skill) &&
          !d.measured?.some((m) => m.taskId === t.id)
            ? "Waiting for your one-job authorization."
            : reasonLabels[t.reason] || t.reason}
        </p>
      )}
      <div className="co-actions">
        {["queued", "blocked", "running", "awaiting_review", "unknown", "failed"].includes(t.state) && (
          <button onClick={() => void run("task.cancel", { id: t.id, expectedVersion: t.version })}>Cancel</button>
        )}
        {["blocked", "failed"].includes(t.state) &&
          t.kind !== "action" &&
          !d.handoffs?.some((h) => h.targetTaskId === t.id) && (
            <button onClick={() => void run("task.retry", { id: t.id, expectedVersion: t.version })}>
              Request fresh attempt
            </button>
          )}
        {t.state === "awaiting_review" && (
          <button onClick={() => void run("task.complete", { id: t.id, expectedVersion: t.version })}>
            Complete after review
          </button>
        )}
      </div>
      {["blocked", "awaiting_review", "queued"].includes(t.state) && t.kind !== "action" && (
        <form
          onSubmit={form(async () => {
            const body = outcome;
            const saved = await run("artifact.create", { id: t.id, expectedVersion: t.version, body });
            if (saved) setOutcome("");
          })}
        >
          <label>
            Owner-authored artifact
            <textarea required value={outcome || ""} onChange={(e) => setOutcome(e.target.value)} />
          </label>
          <Submit busy={busy}>Save for review</Submit>
        </form>
      )}
      {d.artifacts
        .filter((a) => a.taskId === t.id)
        .map((a) => (
          <div className="co-artifact" key={a.id}>
            <small>
              {a.provenance} · {a.state}
            </small>
            {employeeRole?.skills.includes("company-plan") && a.state === "approved" && (
              <button
                onClick={() =>
                  void run("plan.from-artifact", { id: a.id, expectedVersion: a.version, digest: a.digest })
                }
              >
                Prepare delegation plan from proposal
              </button>
            )}
            <pre>{a.body}</pre>
            <div className="co-actions">
              <button
                onClick={() =>
                  void run("artifact.review", {
                    id: a.id,
                    expectedVersion: a.version,
                    digest: a.digest,
                    decision: "approve",
                  })
                }
              >
                Approve content
              </button>
              <button
                onClick={() =>
                  void run("artifact.review", {
                    id: a.id,
                    expectedVersion: a.version,
                    digest: a.digest,
                    decision: "changes",
                  })
                }
              >
                Request changes
              </button>
            </div>
            {(a.state === "approved" || d.handoffs?.some((h) => h.artifactId === a.id && h.targetTaskId)) && (
              <ArtifactHandoff
                artifact={a}
                employees={d.employees}
                departments={d.departments}
                threads={d.threads}
                roles={d.roles}
                receipts={d.handoffs ?? []}
                busy={busy}
                run={run}
                open={open}
              />
            )}
          </div>
        ))}
    </article>
  );
}
