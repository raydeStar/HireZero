import { customerView } from "./audience";
import { useEffect, useRef, useState } from "react";
import { api } from "../lib/api";
import { dollars } from "./money";
import { taskState } from "./labels";
import "./progress.css";

type ModelCosts = {
  jobs: number;
  settledJobs: number;
  reportedUsd: number;
  estimatedUsd: number;
  unpricedJobs: number;
  heldJobs: number;
};
type ProgressData = {
  schema: string;
  companyId: string;
  generatedAt: string;
  observationStartedAt: string | null;
  hasEarlierRecords: boolean;
  onboarding: {
    identifiedAt: string | null;
    confirmedAt: string | null;
    approvedAt: string | null;
    secondsToApprovedBrief: number | null;
    generatedBriefs: number;
    approvedBriefs: number;
    editedGeneratedBriefs: number;
    ownerFieldEdits: number;
  };
  work: {
    tasks: number;
    completed: number;
    awaitingReview: number;
    memberRequestDays: number;
    scheduledTasks: number;
    firstCompletedAt: string | null;
    firstAssignment: null | { taskId: string; state: string; preparedAt: string; completedAt: string | null };
  };
  modelCosts: ModelCosts;
  completedWorkModelCosts: ModelCosts;
  completedTasksWithoutModelReceipt: number;
  averageRecordedModelUsdPerCompletion: number | null;
  taskCosts?: {
    completedTasks: number;
    reconciledTasks: number;
    missingReviews: number;
    staleReviews: number;
    incompleteUsage: number;
    includesModelEstimates: number;
    reportedModelUsd: number;
    estimatedModelUsd: number;
    companyKeyModelUsd: number;
    operatingCosts: { category: string; usd: number | null }[];
    reconciledSubtotalUsd: number | null;
    averageUsdPerCompletion: number | null;
    nextReview: { taskId: string; reason: string } | null;
  } | null;
};
const costCategory: Record<string, string> = {
  compute: "Compute",
  tools: "Tools and external services",
  retrieval: "Retrieval",
  storage: "Storage",
  operations: "Operating overhead",
};
function elapsed(seconds: number | null) {
  if (seconds === null) return "Not recorded";
  if (seconds < 60) return "Under a minute";
  if (seconds < 3600) return `${Math.round(seconds / 60)} min`;
  if (seconds < 86400) return `${(seconds / 3600).toFixed(1)} hours`;
  return `${(seconds / 86400).toFixed(1)} days`;
}
const date = (value: string | null) => (value ? new Date(value).toLocaleString() : "Not recorded");

export function Progress({
  onOpenWork,
  onBusiness,
  onUsage,
}: {
  onOpenWork: (id: string) => Promise<void>;
  onBusiness: () => void;
  onUsage: () => void;
}) {
  const [data, setData] = useState<ProgressData | null>(null),
    [busy, setBusy] = useState(false),
    [error, setError] = useState(""),
    [notice, setNotice] = useState("");
  const generation = useRef(0);
  async function load() {
    const request = ++generation.current;
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const next = await api<ProgressData>("/company/progress");
      if (request === generation.current) setData(next);
    } catch (e) {
      if (request === generation.current) setError((e as Error).message);
    } finally {
      if (request === generation.current) setBusy(false);
    }
  }
  useEffect(() => {
    void load();
    return () => {
      generation.current++;
    };
  }, []);
  function download() {
    if (!data) return;
    // A customer's file leaves out what the install pays for AI (docs/adr/0187).
    const customer = customerView();
    const shown = customer
      ? { ...data, taskCosts: undefined, modelCosts: undefined, averageRecordedModelUsdPerCompletion: undefined }
      : data;
    const url = URL.createObjectURL(new Blob([JSON.stringify(shown, null, 2)], { type: "application/json" }));
    const link = document.createElement("a");
    link.href = url;
    link.download = "hirezero-progress.json";
    link.click();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
    setNotice(
      customer
        ? "Metrics downloaded. The file contains counts, dates and record IDs, without business or result text."
        : "Metrics downloaded. The file contains counts, dates, costs and record IDs, without business or result text.",
    );
  }
  async function openResult(id: string) {
    setError("");
    try {
      await onOpenWork(id);
    } catch (e) {
      setError((e as Error).message);
    }
  }
  const first = data?.work.firstAssignment;
  const costs = data?.taskCosts;
  return (
    <section className="co-progress" aria-label="Company progress" aria-busy={busy}>
      <header>
        <h2>Progress</h2>
        <p>From your business brief to completed work. These measures come from this company's saved history.</p>
      </header>
      <div className="co-progress-actions">
        <button disabled={busy} onClick={() => void load()}>
          {busy ? "Checking…" : "Refresh progress"}
        </button>
        <button disabled={!data || busy} onClick={download}>
          Download metrics
        </button>
      </div>
      {error && (
        <p role="alert">
          {error}
          {data ? " Showing the last checked snapshot." : " Use Refresh progress to try again."}
        </p>
      )}
      {notice && <p role="status">{notice}</p>}
      {data && (
        <>
          <p className="co-muted">
            Checked {date(data.generatedAt)} · Earliest saved event: {date(data.observationStartedAt)}
          </p>
          {data.hasEarlierRecords && (
            <p>Some work predates the available measurement history. Missing dates or costs remain unknown.</p>
          )}
          <div className="co-progress-cards">
            <article>
              <span>Time to approved brief</span>
              <strong>{elapsed(data.onboarding.secondsToApprovedBrief)}</strong>
              <small>From first business identification, including time away.</small>
              <button onClick={onBusiness}>Open business brief</button>
            </article>
            <article>
              <span>Completed work</span>
              <strong>
                {data.work.completed}{" "}
                <small>
                  of {data.work.tasks} {data.work.tasks === 1 ? "task" : "tasks"}
                </small>
              </strong>
              <small>{data.work.awaitingReview} awaiting review. Chat replies are excluded.</small>
            </article>
            <article>
              <span>Days with requested work</span>
              <strong>{data.work.memberRequestDays}</strong>
              <small>
                UTC days when a member requested a task. {data.work.scheduledTasks} scheduled tasks are counted
                separately.
              </small>
            </article>
          </div>
          <article className="co-progress-section">
            <h3>First assignment</h3>
            {first ? (
              <>
                <p>
                  <b>{taskState(first.state)}</b> · Prepared {date(first.preparedAt)}
                </p>
                <p>
                  {first.completedAt
                    ? `Completed ${date(first.completedAt)}`
                    : "No completion recorded yet. Review the result and mark the task complete when it is ready."}
                </p>
                <button className="co-primary" onClick={() => void openResult(first.taskId)}>
                  Open first assignment
                </button>
              </>
            ) : (
              <p>No starter assignment has been prepared yet. Your earlier work is included in the totals above.</p>
            )}
          </article>
          <article className="co-progress-section">
            <h3>Brief edits</h3>
            <p>
              {data.onboarding.ownerFieldEdits} saved field {data.onboarding.ownerFieldEdits === 1 ? "edit" : "edits"}{" "}
              across {data.onboarding.editedGeneratedBriefs} of {data.onboarding.generatedBriefs} generated{" "}
              {data.onboarding.generatedBriefs === 1 ? "brief" : "briefs"}, before approval.
            </p>
            <p className="co-muted">
              Edits can be corrections or preferences; they are not a verified error rate. Manually written briefs are
              excluded. {data.onboarding.approvedBriefs}{" "}
              {data.onboarding.approvedBriefs === 1 ? "brief has" : "briefs have"} an approval recorded.
            </p>
          </article>
          {/* What the install pays for AI is not a customer's measure (docs/adr/0187). */}
          {costs && !customerView() && (
            <article className="co-progress-section co-progress-task-costs">
              <h3>Completed task costs</h3>
              <p>
                Current cost totals:{" "}
                <b>
                  {costs.reconciledTasks} of {costs.completedTasks}
                </b>{" "}
                completed {costs.completedTasks === 1 ? "task" : "tasks"}.
              </p>
              <dl>
                <div>
                  <dt>Reviewed subtotal</dt>
                  <dd>
                    {costs.reconciledSubtotalUsd === null ? "Not available" : dollars(costs.reconciledSubtotalUsd)}
                  </dd>
                </div>
                <div>
                  <dt>Average cost per completed task</dt>
                  <dd>
                    {costs.averageUsdPerCompletion === null ? "Not available" : dollars(costs.averageUsdPerCompletion)}
                  </dd>
                </div>
              </dl>
              <p className="co-muted">
                The subtotal covers only tasks with current owner-recorded expenses and settled usage. The average
                appears when every completed task has a total. It includes all recorded attempts on those tasks.
              </p>
              {costs.completedTasks === 0 && <p>Complete a task and review its costs to start measuring.</p>}
              {costs.missingReviews > 0 && (
                <p>
                  {costs.missingReviews} completed{" "}
                  {costs.missingReviews === 1 ? "task still needs" : "tasks still need"} operating costs recorded.
                </p>
              )}
              {costs.staleReviews > 0 && (
                <p>
                  {costs.staleReviews} cost {costs.staleReviews === 1 ? "review needs" : "reviews need"} updating
                  because task usage changed.
                </p>
              )}
              {costs.incompleteUsage > 0 && (
                <p>
                  {costs.incompleteUsage} {costs.incompleteUsage === 1 ? "task has" : "tasks have"} recorded expenses
                  but incomplete usage or cleanup evidence.
                </p>
              )}
              {costs.nextReview && (
                <button onClick={() => void openResult(costs.nextReview!.taskId)}>
                  Open next task needing cost review
                </button>
              )}
              {costs.reconciledTasks > 0 && (
                <details>
                  <summary>What is included in the reviewed subtotal</summary>
                  <dl>
                    <div>
                      <dt>Provider-reported model costs</dt>
                      <dd>{dollars(costs.reportedModelUsd)}</dd>
                    </div>
                    <div>
                      <dt>Estimated model costs</dt>
                      <dd>{dollars(costs.estimatedModelUsd)}</dd>
                    </div>
                    {costs.operatingCosts.map((cost) => (
                      <div key={cost.category}>
                        <dt>{costCategory[cost.category] || cost.category}</dt>
                        <dd>{cost.usd === null ? "Unknown" : dollars(cost.usd)}</dd>
                      </div>
                    ))}
                  </dl>
                  <p>
                    {costs.includesModelEstimates} of these tasks include model-price estimates. Owner-recorded expenses
                    depend on the saved receipts and allocation basis.
                  </p>
                  <p>
                    {dollars(costs.companyKeyModelUsd)} of model costs were funded through company API keys and are
                    included above. These totals measure recorded task costs, not HireZero's profit or a customer
                    charge.
                  </p>
                  <p>Brief preparation, chat, unfinished tasks, and unallocated company expenses are excluded.</p>
                </details>
              )}
            </article>
          )}
          {!customerView() && (
            <article className="co-progress-section">
              <h3>Recorded model costs</h3>
              <dl>
                <div>
                  <dt>Provider-reported</dt>
                  <dd>{dollars(data.modelCosts.reportedUsd)}</dd>
                </div>
                <div>
                  <dt>Estimated from saved prices</dt>
                  <dd>{dollars(data.modelCosts.estimatedUsd)}</dd>
                </div>
                <div>
                  <dt>Jobs without a price</dt>
                  <dd>{data.modelCosts.unpricedJobs}</dd>
                </div>
                <div>
                  <dt>Jobs awaiting settlement</dt>
                  <dd>{data.modelCosts.heldJobs}</dd>
                </div>
              </dl>
              <p>
                {data.modelCosts.settledJobs} of {data.modelCosts.jobs} model jobs settled, including brief preparation
                and chat.
              </p>
              <p>
                Average recorded model cost per completed task:{" "}
                <b>
                  {data.averageRecordedModelUsdPerCompletion === null
                    ? "Not available"
                    : dollars(data.averageRecordedModelUsdPerCompletion)}
                </b>
                .
              </p>
              {data.completedTasksWithoutModelReceipt > 0 && (
                <p>{data.completedTasksWithoutModelReceipt} completed tasks have no model receipt.</p>
              )}
              <p className="co-muted">
                The average includes only jobs attached to completed tasks and appears when all of those costs are
                known. Estimates may be included. Compute, tools, storage and subscription fees are not included.
              </p>
              <button onClick={onUsage}>Open usage receipts</button>
            </article>
          )}
          <details className="co-progress-section">
            <summary>What these measures can tell you</summary>
            <p>
              Saved activity shows progress through the workflow. Business identification accuracy, usefulness, customer
              retention and paid conversion need real observations; they cannot be inferred from these counts.
            </p>
            <p>
              No analytics are sent to an external service. Download metrics exports the checked snapshot for your own
              comparison. It does not label data as a real customer trial.
            </p>
          </details>
        </>
      )}
    </section>
  );
}
