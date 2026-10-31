import { canRepeatWork } from "./recurringWork";
import { useMemberAccess } from "./MemberAccess";
import { lazy, Suspense, useRef, useState, type FormEvent, type ReactNode } from "react";
import { DraftMarkdown } from "./DraftMarkdown";
import { api, company, memberStorage } from "../lib/api";
import type { Snapshot, Work } from "./api";
import { MeasuredJob } from "./MeasuredJob";
import { providerName } from "./ModelChoice";
import { ArtifactHandoff } from "./ArtifactHandoff";
import { ArtifactRevision } from "./ArtifactRevision";
import { ArtifactSources } from "./ArtifactSources";
import { draftChecks } from "../roles/company";
import { DraftChecks, draftRepairFeedback } from "./DraftChecks";
import { ArtifactPublication, ExecutionSteps } from "./ExecutionSteps";
import { artifactState, provenance, taskState } from "./labels";
import { humanize } from "./labels";
import { SandboxTools } from "./Sandbox";
import { PlanProposal, planProposal } from "./PlanProposal";
import { workState, workTitle } from "./Cockpit";
import { splitDraftReview } from "./reportSections";
const BrowserTools = lazy(() => import("./Browser").then((module) => ({ default: module.BrowserTools })));
const TaskCosts = lazy(() => import("./TaskCosts").then((module) => ({ default: module.TaskCosts })));
const reasonLabels: Record<string, string> = {
  "handoff-awaiting-approval": "Assigned from an approved handoff. Separate execution approval is required.",
  "execution-unavailable": "Reviewed execution needs a granted checkout and your approval of its exact proposal.",
  // Tasks saved before the reason was made role-neutral.
  "developer-execution-unavailable": "Repository execution is unavailable. This task is saved for review.",
  "provider-unavailable": "Live model execution is unavailable. Your task is saved.",
  dependencies: "Waiting for an owner-reviewed dependency.",
  "usage-unknown": "Provider usage is uncertain; the reservation remains held.",
  "revision-awaiting-approval":
    "Your feedback is saved. Review the revision's model, context and allowance before authorizing a new run.",
  "revision-requested":
    "Replaced by a separate revision request. The original result and receipt remain in your history.",
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
  onOpenResult,
  onOpenPlans,
  onRememberResult,
  onRepeatWork,
  focused = false,
}: {
  task: Work;
  snapshot: Snapshot;
  busy: boolean;
  run: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  refresh: () => Promise<void>;
  open: (employeeId: string, threadId: string) => void;
  onOpenResult?: (task: Work) => void;
  onOpenPlans?: () => void;
  onRememberResult?: (artifactId: string) => void;
  onRepeatWork?: (taskId: string) => void;
  focused?: boolean;
}) {
  const { canWork } = useMemberAccess();
  const d = snapshot.data,
    employee = d.employees.find((e) => e.id === t.employeeId)!,
    employeeRole = d.roles.find((r) => r.id === employee.roleId);
  const draftKey = `hirezero-artifact-edit-v1:${company() || "local-company"}:${t.id}`;
  const [outcome, setOutcome] = useState(() => {
    try {
      return memberStorage.getItem(draftKey) ?? "";
    } catch {
      return "";
    }
  });
  const [editorNotice, setEditorNotice] = useState("");
  const [editing, setEditing] = useState(false);
  const [showCosts, setShowCosts] = useState(false);
  const editor = useRef<HTMLTextAreaElement>(null);
  const savedArtifacts = d.artifacts.filter((a) => a.taskId === t.id);
  const artifacts = [...savedArtifacts].reverse();
  if (t.state === "completed")
    artifacts.sort((a, b) => Number(b.state === "approved") - Number(a.state === "approved"));
  const hasApprovedResult = artifacts.some((a) => a.state === "approved");
  const measured = d.measured?.filter((m) => m.taskId === t.id).slice(-1)[0];
  const scheduledOccurrence = !!t.scheduleRunId;
  const canAuthor =
    canWork &&
    ["blocked", "awaiting_review", "queued"].includes(t.state) &&
    t.kind === "work" &&
    (!scheduledOccurrence || (t.state === "awaiting_review" && measured?.job.state === "completed"));
  function changeOutcome(value: string) {
    setOutcome(value);
    setEditorNotice("");
    try {
      if (value) memberStorage.setItem(draftKey, value);
      else memberStorage.removeItem(draftKey);
    } catch {
      setEditorNotice("Tab recovery is unavailable. Keep a copy of your edits before leaving this page.");
    }
  }
  function editCopy(body: string) {
    setEditing(true);
    // Keep the owner's unfinished prose; the butler does not replace a letter mid-sentence.
    if (outcome.trim() && outcome !== body)
      setEditorNotice("You already have an unsaved draft. Save it or clear its text before editing another copy.");
    else changeOutcome(body);
    requestAnimationFrame(() => editor.current?.focus());
  }
  const scheduledIsolatedWork = t.kind === "work" && scheduledOccurrence && !t.modelRoute;
  const savedResult = measured?.job.state === "completed" && artifacts.length > 0;
  const measuredJob =
    !scheduledIsolatedWork &&
    (employeeRole?.measuredSkills?.includes(t.skill) || (t.planId && t.skill === "chat")) &&
    !["canceled", "superseded"].includes(t.state) &&
    !(!measured && ["completed", "awaiting_review"].includes(t.state)) ? (
      <MeasuredJob
        key={`measured:${measured?.job.digest ?? t.id}`}
        task={t}
        entry={measured}
        available={!!snapshot.capabilities.measuredOneJob}
        cents={snapshot.capabilities.allowanceUnit === "usd-cents"}
        modelChoices={snapshot.capabilities.modelChoices}
        busy={busy}
        prepare={run}
        refresh={refresh}
      />
    ) : null;
  function form(fn: () => void | Promise<void>) {
    return (e: FormEvent) => {
      e.preventDefault();
      void fn();
    };
  }
  return (
    <article
      className="co-card co-job-card"
      data-task-id={t.id}
      data-task-state={t.state}
      aria-label={workTitle(snapshot, t) + " job"}
    >
      {!focused && (
        <b>
          {t.revision ? "Revision · " : ""}
          {workTitle(snapshot, t)} · {taskState(workState(snapshot, t), canWork)}
        </b>
      )}
      {t.revision && (
        <details aria-label="Revision request" open={!artifacts.length}>
          <summary>Requested changes</summary>
          <p style={{ whiteSpace: "pre-wrap" }}>{t.revision.feedback}</p>
          <p className="co-muted">
            {artifacts.length
              ? "Revised using your feedback. The previous draft remains available for comparison."
              : "The previous draft is reference material. Review and authorize this revision before it runs."}
          </p>
          {d.artifacts.find((artifact) => artifact.id === t.revision?.artifactId) && (
            <details>
              <summary>Previous unapproved draft</summary>
              <Report body={d.artifacts.find((artifact) => artifact.id === t.revision?.artifactId)!.body} />
            </details>
          )}
        </details>
      )}
      {artifacts.length > 0 && onOpenResult && !focused && (
        <button className="co-primary" onClick={() => onOpenResult(t)}>
          Read result
        </button>
      )}
      {!artifacts.length && <TaskInput input={t.input} />}
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
            {canWork && h.state === "shared" && (
              <button
                disabled={busy}
                onClick={() => void run("handoff.revoke", { id: h.id, expectedVersion: h.version })}
              >
                Revoke this handoff
              </button>
            )}
          </div>
        ))}
      {scheduledOccurrence && (
        <p>
          This result belongs to an approved recurring schedule. Review its saved output below.{" "}
          <a
            href={`/?view=departments&section=schedules${company() ? `&company=${encodeURIComponent(company())}` : ""}`}
          >
            Manage recurring work
          </a>
        </p>
      )}
      {!savedResult && measuredJob}
      {artifacts.map((a, index) => (
        <details className="co-artifact" key={a.id} data-artifact-id={a.id} tabIndex={-1} open={index === 0}>
          <summary>
            <small>
              {savedArtifacts.length > 1 && `Draft ${savedArtifacts.findIndex((saved) => saved.id === a.id) + 1} · `}
              {a.state === "needs_review" ? "Draft ready" : artifactState(a.state, canWork)}
            </small>
          </summary>

          {canWork &&
            t.skill === "company-plan" &&
            employeeRole?.skills.includes("company-plan") &&
            a.state === "approved" && (
              <button
                disabled={busy || !planProposal(a.body)}
                onClick={() =>
                  void (async () => {
                    const result = (await run("plan.from-artifact", {
                      id: a.id,
                      expectedVersion: a.version,
                      digest: a.digest,
                    })) as { id: string } | undefined;
                    if (result?.id) onOpenPlans?.();
                  })()
                }
              >
                Prepare delegation plan from proposal
              </button>
            )}
          {t.skill === "company-plan" ? (
            <>
              <PlanProposal body={a.body} snapshot={snapshot} />
              {canWork && !["canceled", "superseded"].includes(t.state) && a.state === "needs_review" && (
                <ArtifactRevision
                  artifact={a}
                  busy={busy}
                  run={run}
                  onCreated={onOpenResult}
                  canRevise={
                    t.kind === "work" &&
                    t.state === "awaiting_review" &&
                    !t.planId &&
                    !t.meetingId &&
                    !t.scheduleRunId &&
                    !t.resource &&
                    !t.dependencies?.length &&
                    !!employeeRole?.measuredSkills?.includes(t.skill)
                  }
                />
              )}
            </>
          ) : (
            <Report
              body={a.body}
              onEdit={canAuthor ? () => editCopy(a.body) : undefined}
              busy={busy}
              refine={
                canWork && !["canceled", "superseded"].includes(t.state) && a.state === "needs_review" ? (
                  <ArtifactRevision
                    key={`${a.id}:${a.version}`}
                    label="Refine draft"
                    artifact={a}
                    busy={busy}
                    run={run}
                    onCreated={onOpenResult}
                    suggestedFeedback={draftRepairFeedback(draftChecks(employeeRole, a.body, t, d.tasks))}
                    canRevise={
                      t.kind === "work" &&
                      t.state === "awaiting_review" &&
                      !t.planId &&
                      !t.meetingId &&
                      !t.scheduleRunId &&
                      !t.resource &&
                      !t.dependencies?.length &&
                      !!employeeRole?.measuredSkills?.includes(t.skill)
                    }
                  />
                ) : undefined
              }
            />
          )}
          {/* What to do next, in plain words (dogfood, 2026-10-30): approve the draft, then mark the request done.
              The same commands and checks as the controls further down. */}
          {canWork &&
            index === 0 &&
            t.kind !== "conversation" &&
            (a.state === "needs_review"
              ? ["awaiting_review", "blocked"].includes(t.state)
              : a.state === "approved" && t.state === "awaiting_review") && (
              <div className="co-next-step" role="group" aria-label="Next step">
                {a.state === "needs_review" ? (
                  <>
                    <p>
                      <b>Happy with this draft?</b> Approve it to keep it as your final version. Nothing is posted or
                      sent. For changes, use Refine draft above.
                    </p>
                    <button
                      className="co-primary"
                      disabled={busy}
                      onClick={() =>
                        void run("artifact.review", {
                          id: a.id,
                          expectedVersion: a.version,
                          digest: a.digest,
                          decision: "approve",
                        })
                      }
                    >
                      Approve draft
                    </button>
                  </>
                ) : (
                  <>
                    <p>
                      <b>Approved.</b> Mark this request as done once you have used it.
                    </p>
                    <button
                      className="co-primary"
                      disabled={busy}
                      onClick={() => void run("task.complete", { id: t.id, expectedVersion: t.version })}
                    >
                      Mark as done
                    </button>
                  </>
                )}
              </div>
            )}
          <ArtifactSources key={`${company()}:${a.id}:${a.contextId}`} artifact={a} entry={measured}>
            <p>
              {provenance(employeeRole, a.provenance)} · {artifactState(a.state, canWork)}
            </p>
            {a.sandboxOrigin && (
              <p className="co-muted">
                From {a.sandboxOrigin.outputName} · {a.sandboxOrigin.length.toLocaleString()} selected characters.
                Source documents and script authority remain revocable. Approval does not publish or share this result.
              </p>
            )}
            {a.browserOrigin && (
              <p className="co-muted">
                From {a.browserOrigin.url} · {new Date(a.browserOrigin.capturedAt).toLocaleString()}
                {a.browserOrigin.partial ? " · Partial page evidence" : ""}. Source authority remains revocable.
                Approval does not publish or share this result.
              </p>
            )}
          </ArtifactSources>
          <DraftChecks checks={draftChecks(employeeRole, a.body, t, d.tasks)} />
          {a.reviewFeedback && (
            <section aria-label="Saved review feedback">
              <strong>Requested changes</strong>
              <p style={{ whiteSpace: "pre-wrap" }}>{a.reviewFeedback}</p>
            </section>
          )}
          {d.tasks.find((task) => task.revision?.artifactId === a.id) && (
            <button
              onClick={() => {
                const revision = d.tasks.find((task) => task.revision?.artifactId === a.id)!;
                if (onOpenResult) onOpenResult(revision);
                else open(revision.employeeId, revision.threadId);
              }}
            >
              Review revision request
            </button>
          )}
          <details className="co-share-result">
            <summary>Share or reuse this result</summary>
            <p className="co-muted">
              Reading, copying and downloading need no approval. Review and approve the contents before adding shared
              context, handing it to another employee or setting up recurring work.
            </p>
            {a.state === "approved" && !["canceled", "superseded"].includes(t.state) && onRememberResult && (
              <button disabled={busy} onClick={() => onRememberResult(a.id)}>
                Remember feedback for future work
              </button>
            )}
            {canWork && !["canceled", "superseded"].includes(t.state) && a.state === "needs_review" && (
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
                  Approve result
                </button>
              </div>
            )}
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
            {onRepeatWork && canRepeatWork(snapshot, t) && (
              <button disabled={busy} onClick={() => onRepeatWork(t.id)}>
                Repeat this work
              </button>
            )}
          </details>
        </details>
      ))}
      {artifacts.length > 0 && (
        <details className="co-job-request">
          <summary>Original request</summary>
          <TaskInput input={t.input} />
        </details>
      )}
      {savedResult && (
        <details className="co-job-receipt">
          <summary>Execution receipt</summary>
          {measuredJob}
        </details>
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
            runtimeAvailable={snapshot.capabilities.liveCoding === true}
            busy={busy}
            run={run}
            refresh={refresh}
          />
          <CodeReview taskId={t.id} />
        </>
      )}
      {(snapshot.capabilities.sandboxExecution || d.sandboxTaskIds?.includes(t.id)) && (
        <SandboxTools key={`sandbox:${t.id}`} task={t} snapshot={snapshot} busy={busy} refresh={refresh} />
      )}
      {((!scheduledOccurrence && snapshot.capabilities.browserExecution) || d.browserTaskIds?.includes(t.id)) && (
        <Suspense fallback={<p role="status">Loading website reviews…</p>}>
          <BrowserTools key={`browser:${t.id}`} task={t} snapshot={snapshot} busy={busy} refresh={refresh} />
        </Suspense>
      )}
      {snapshot.capabilities.costOwner && t.kind === "work" && (
        <details
          className="co-job-costs"
          onToggle={(event) => {
            if (event.target === event.currentTarget) setShowCosts(event.currentTarget.open);
          }}
        >
          <summary>Task costs and reconciliation</summary>
          {showCosts && (
            <Suspense fallback={<p role="status">Loading task costs…</p>}>
              <TaskCosts key={`costs:${t.id}`} task={t} />
            </Suspense>
          )}
        </details>
      )}
      {t.modelRoute && t.kind !== "action" && artifacts.length === 0 && (
        <p className="co-muted">
          {measured?.job.terms ? providerName(measured.job.terms.provider) : t.runtime}: {t.modelRoute}
        </p>
      )}
      {t.reason && (
        <p className="co-muted">
          {t.reason === "provider-unavailable" &&
          snapshot.capabilities.measuredOneJob &&
          employeeRole?.measuredSkills?.includes(t.skill) &&
          !d.measured?.some((m) => m.taskId === t.id)
            ? canWork
              ? "Waiting for your one-job authorization."
              : "Waiting for one-job authorization."
            : reasonLabels[t.reason] || t.reason}
        </p>
      )}
      {canWork && (
        <details className="co-task-management">
          <summary>Manage task</summary>
          <div className="co-actions">
            {["queued", "blocked", "running", "awaiting_review", "unknown", "failed"].includes(t.state) &&
              // An approved copy can finish the work; the earlier drafts keep their own history.
              !(["awaiting_review", "blocked"].includes(t.state) && hasApprovedResult) && (
                <button onClick={() => void run("task.cancel", { id: t.id, expectedVersion: t.version })}>
                  Cancel
                </button>
              )}
            {["blocked", "failed"].includes(t.state) &&
              !t.revision &&
              t.kind !== "action" &&
              !scheduledOccurrence &&
              !d.handoffs?.some((h) => h.targetTaskId === t.id) && (
                <button onClick={() => void run("task.retry", { id: t.id, expectedVersion: t.version })}>
                  Request fresh attempt
                </button>
              )}
            {(t.state === "awaiting_review" || (t.state === "blocked" && hasApprovedResult)) &&
              t.kind !== "conversation" &&
              // Completing needs an approved result (the store refuses otherwise), so the button waits for one instead of
              // offering a click that only errors (owner feedback, 2026-10-20).
              (() => {
                const approved = d.artifacts.some((a) => a.taskId === t.id && a.state === "approved");
                return (
                  <button
                    disabled={!approved}
                    title={approved ? undefined : "Approve the result first"}
                    onClick={() => void run("task.complete", { id: t.id, expectedVersion: t.version })}
                  >
                    Complete after review
                  </button>
                );
              })()}
          </div>
        </details>
      )}
      {(canAuthor || (canWork && outcome.trim())) && (editing || !!outcome.trim() || !artifacts.length) && (
        <form
          className="co-artifact-editor"
          onSubmit={form(async () => {
            if (!canAuthor || busy || !outcome.trim() || outcome.length > 12000) return;
            const body = outcome;
            const saved = await run("artifact.create", { id: t.id, expectedVersion: t.version, body });
            if (saved) changeOutcome("");
          })}
        >
          <label>
            {/* Before any result, this is the optional write-it-yourself route, not a step (dogfood, 2026-10-30). */}
            {artifacts.length || editing ? "Your draft" : "Or write the draft yourself (optional)"}
            <textarea
              ref={editor}
              required
              rows={outcome ? 12 : 4}
              maxLength={12000}
              disabled={busy}
              readOnly={!canAuthor}
              value={outcome}
              onChange={(e) => changeOutcome(e.target.value)}
            />
          </label>
          <p className="co-muted">
            {canAuthor
              ? "Save a separate draft for review. The original stays in history. No model request is made. Unsaved edits stay in this tab."
              : "This task no longer accepts drafts. Your unsaved text is preserved in this editor for copying."}
          </p>
          {scheduledOccurrence && (
            <p className="co-muted">
              Your edits affect this result only. Later scheduled runs keep their approved instructions and references.
            </p>
          )}
          <p className="co-muted">
            {outcome.trim() ? outcome.trim().split(/\s+/).length : 0} words · {outcome.length.toLocaleString()} / 12,000
            characters
          </p>
          {editorNotice && <p role="status">{editorNotice}</p>}
          {canAuthor && <Submit busy={busy || !outcome.trim() || outcome.length > 12000}>Save for review</Submit>}
        </form>
      )}
    </article>
  );
}

// A task's input as the owner wrote it: structured input as labelled fields, never raw JSON.
function TaskInput({ input }: { input: string }) {
  let fields: Record<string, unknown> | null = null;
  try {
    const value = JSON.parse(input);
    if (value && typeof value === "object" && !Array.isArray(value)) fields = value;
  } catch {
    /* Plain text is shown as written. */
  }
  if (!fields && input.length <= 400) return <p>{input}</p>;
  // A long request opens with its first lines; the exact text sent stays one click away (dogfood, 2026-10-30).
  if (!fields)
    return (
      <div className="co-task-input">
        <p>{input.split(/\r?\n/).slice(0, 2).join(" ").slice(0, 300)}…</p>
        <details>
          <summary>Show the full request</summary>
          <p style={{ whiteSpace: "pre-wrap" }}>{input}</p>
        </details>
      </div>
    );
  return (
    <dl className="co-packet-fields">
      {Object.entries(fields).map(([key, value]) => (
        <div key={key}>
          <dt>{humanize(key.replace(/([A-Z])/g, " $1"))}</dt>
          <dd>{typeof value === "string" ? value : JSON.stringify(value)}</dd>
        </div>
      ))}
    </dl>
  );
}

// Review starts with the whole deliverable; the useful bit should not hide behind the butler's curtain.
function Report({
  body,
  onEdit,
  busy = false,
  refine,
}: {
  body: string;
  onEdit?: () => void;
  busy?: boolean;
  refine?: ReactNode;
}) {
  // A publishing reading ends with the exact call it allows ("Call: {json}", docs/adr/0017). It is shown apart from
  // the reading and formatted; the approved text itself is unchanged.
  const lines = body.split(/\r?\n/);
  let callAt = -1;
  for (let i = lines.length - 1; i >= 0; i--)
    if (lines[i].startsWith("Call: ")) {
      callAt = i;
      break;
    }
  let call: string | null = null;
  if (callAt >= 0) {
    const raw = lines[callAt].slice(6);
    try {
      call = JSON.stringify(JSON.parse(raw), null, 2);
    } catch {
      call = raw;
    }
  }
  const shown = callAt >= 0 ? lines.filter((_, i) => i !== callAt).join("\n") : body;
  const sections = callAt >= 0 ? { draft: shown, notes: undefined } : splitDraftReview(shown);
  const usable = sections.notes ? sections.draft : body;
  const long = sections.draft.split(/\r?\n/).length > 14 || sections.draft.length > 1400;
  const [full, setFull] = useState(true);
  const [copyStatus, setCopyStatus] = useState("");
  async function copy() {
    try {
      await navigator.clipboard.writeText(usable);
      setCopyStatus("Draft copied.");
    } catch {
      setCopyStatus("Clipboard unavailable. Use Download draft or select the text below.");
    }
  }
  function download(text = usable, filename = "hirezero-draft.md") {
    const url = URL.createObjectURL(new Blob([text], { type: "text/markdown;charset=utf-8" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = filename;
    link.click();
    // Let the browser finish accepting the local download before releasing its URL.
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  return (
    <div className={"co-report" + (long && !full ? " clamped" : "")}>
      <div className="co-actions co-report-tools" aria-label="Use this draft">
        <button type="button" className="co-ghost" onClick={() => void copy()}>
          Copy draft
        </button>
        <button type="button" className="co-ghost" onClick={() => download()}>
          Download draft
        </button>
        {refine}
        {onEdit && (
          <button type="button" className="co-ghost" disabled={busy} onClick={onEdit}>
            Edit a copy
          </button>
        )}
        <span role="status">{copyStatus}</span>
      </div>
      <div className="co-report-content">
        <DraftMarkdown copySections>{sections.draft}</DraftMarkdown>
      </div>
      {long && (
        <button type="button" className="co-ghost co-report-toggle" onClick={() => setFull(!full)}>
          {full ? "Show less" : "Show full report"}
        </button>
      )}
      {sections.notes && (
        <details className="co-internal-review-notes">
          <summary>Internal review notes</summary>
          <p className="co-muted">
            Copy and Download use the draft above. The full saved response includes these notes.
          </p>
          <DraftMarkdown>{sections.notes}</DraftMarkdown>
          <button type="button" className="co-ghost" onClick={() => download(body, "hirezero-full-result.md")}>
            Download full saved result
          </button>
        </details>
      )}
      {call && (
        <details className="co-exact-call">
          <summary>Exact call</summary>
          <pre>{call}</pre>
        </details>
      )}
    </div>
  );
}
