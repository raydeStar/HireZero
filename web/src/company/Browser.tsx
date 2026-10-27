import { browserRuntimeMessage } from "./browserStatus";
import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError } from "../lib/api";
import { command, type Artifact, type Snapshot, type Work } from "./api";
import { ComputeAllowance, computeRemaining, taskExecutionHold, type ComputeHold, type ComputeUsage } from "./Sandbox";
import { useNow } from "./time";
import "./browser.css";

type Step = { action: "navigate" | "click" | "wait-for" | "read"; value: string };
type Capture = {
  step: number;
  url: string;
  capturedAt: string;
  title: string;
  text: string;
  truncated: boolean;
  links: { text: string; url: string }[];
  screenshot: { mediaType: string; base64: string } | null;
};
type Job = {
  id: string;
  name: string;
  digest: string;
  resultDigest: string | null;
  version: number;
  state: string;
  revoked: boolean;
  expiresAt: string;
  startedAt: string | null;
  settledAt: string | null;
  reservedSeconds: number;
  allocatedSeconds: number | null;
  dispatchTaken: boolean;
  captureDeletion?: { deletedAt: string; reason: string; artifacts: { id: string; digest: string }[] } | null;
  request: {
    origins: string[];
    steps: Step[];
    limits: { seconds: number; requests: number; bytes: number };
    width: number;
    height: number;
    screenshots: boolean;
  };
  release: { confirmed: boolean; status: string; resourceId: string } | null;
  execution: {
    reason: string | null;
    network: { requests: number; receivedBytes: number; refused: number } | null;
    wallMilliseconds: number | null;
    browserExited: boolean;
    result: {
      state: string;
      completedSteps: number;
      partial: boolean;
      captures: Capture[];
      blocked: { url: string; reason: string }[];
      warnings: string[];
      contextClosed: boolean;
      error: string | null;
    } | null;
  } | null;
};
type Runtime = {
  browserAvailable: boolean;
  browserRecoveryAvailable: boolean;
  browserRuntime: { status: string };
  usage: ComputeUsage;
};
const states: Record<string, string> = {
  proposed: "Ready for your review",
  running: "Website review in progress",
  completed: "Captures ready",
  failed: "Website review failed",
  "timed-out": "Time limit reached",
  canceled: "Canceled",
  unknown: "Outcome unknown",
  "not-sent": "No visit was started",
  withheld: "Captures withheld",
  revoked: "Proposal discarded",
  "cleanup-incomplete": "Browser exit needs attention",
};
const actions: Record<Step["action"], string> = {
  navigate: "Open page",
  click: "Click element",
  "wait-for": "Wait for element",
  read: "Read page area",
};
function publicLink(value: string) {
  try {
    const u = new URL(value);
    return ["https:", "http:"].includes(u.protocol) && !u.username && !u.password ? u.href : undefined;
  } catch {
    return undefined;
  }
}
function downloadFile(name: string, content: string, type: string) {
  const url = URL.createObjectURL(new Blob([content], { type }));
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = name;
  anchor.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
function download(job: Job) {
  downloadFile(
    `website-review-${job.id}.json`,
    JSON.stringify(
      {
        name: job.name,
        request: job.request,
        execution: job.execution,
        release: job.release,
        resultDigest: job.resultDigest,
        captureDeletion: job.captureDeletion,
      },
      null,
      2,
    ),
    "application/json",
  );
}
type Act = (action: string, fields: Record<string, unknown>) => Promise<unknown>;

function PageCapture({
  capture,
  job,
  existing,
  canPrepare,
  busy,
  act,
  showArtifact,
}: {
  capture: Capture;
  job: Job;
  existing?: Artifact;
  canPrepare: boolean;
  busy: boolean;
  act: Act;
  showArtifact: (id: string) => void;
}) {
  const [quote, setQuote] = useState(capture.text);
  const exact = quote.trim().length > 0 && quote.length <= 8000 && capture.text.includes(quote);
  return (
    <section className="co-browser-capture" aria-label={`Capture ${capture.step}`}>
      <h5>{capture.title || "Untitled page"}</h5>
      <a href={publicLink(capture.url)} target="_blank" rel="noopener noreferrer">
        {capture.url}
      </a>
      <p>
        Captured {new Date(capture.capturedAt).toLocaleString()}
        {capture.truncated ? " · Text shortened to fit the capture limit" : ""}
      </p>
      <pre>{capture.text || "No visible text was captured in this area."}</pre>
      <button
        onClick={() =>
          downloadFile(
            `website-capture-${capture.step}.txt`,
            `${capture.title}\n${capture.url}\nCaptured: ${capture.capturedAt}\n${job.execution?.result?.partial ? "Partial evidence: review the capture limitations.\n" : ""}\n${capture.text}`,
            "text/plain;charset=utf-8",
          )
        }
      >
        Download page text
      </button>
      {capture.screenshot && (
        <details>
          <summary>View saved screenshot</summary>
          <img
            src={`data:image/jpeg;base64,${capture.screenshot.base64}`}
            alt={`Saved viewport of ${capture.title || capture.url}`}
            loading="lazy"
          />
          <a
            href={`data:image/jpeg;base64,${capture.screenshot.base64}`}
            download={`website-capture-${capture.step}.jpg`}
          >
            Download screenshot
          </a>
        </details>
      )}
      {capture.links.length > 0 && (
        <details>
          <summary>Links found on this page ({capture.links.length})</summary>
          <ul>
            {capture.links.map((link, i) => (
              <li key={i}>
                {publicLink(link.url) ? (
                  <a href={publicLink(link.url)} target="_blank" rel="noopener noreferrer">
                    {link.text || link.url}
                  </a>
                ) : (
                  <span>{link.text || "Unsupported link"}</span>
                )}
              </li>
            ))}
          </ul>
        </details>
      )}
      {existing ? (
        <p>
          This capture has a saved task result.{" "}
          <button onClick={() => showArtifact(existing.id)}>Review page result</button>
        </p>
      ) : (
        canPrepare && (
          <details>
            <summary>Use this text as a task result</summary>
            <p>Select an exact excerpt. It will enter the task’s review flow with its page address and capture date.</p>
            <label>
              Selected page text
              <textarea rows={5} maxLength={8000} value={quote} onChange={(e) => setQuote(e.target.value)} />
            </label>
            <button
              disabled={busy || !exact}
              onClick={() =>
                void (async () => {
                  const result = (await act("browser.output.prepare", {
                    id: job.id,
                    expectedVersion: job.version,
                    digest: job.resultDigest,
                    decision: "prepare-review",
                    body: JSON.stringify({ step: capture.step, quote }),
                  })) as Artifact | undefined;
                  if (result?.id) showArtifact(result.id);
                })()
              }
            >
              Prepare page result
            </button>
            {!exact && <p>Select unchanged text from this saved capture.</p>}
          </details>
        )
      )}
    </section>
  );
}

function BrowserReview({
  job,
  task,
  artifacts,
  runtime,
  owner,
  executionHold,
  busy,
  act,
  recover,
  showArtifact,
}: {
  job: Job;
  task: Work;
  artifacts: Artifact[];
  runtime: Runtime | null;
  owner: boolean;
  executionHold: string;
  busy: boolean;
  act: Act;
  recover: (id: string) => Promise<void>;
  showArtifact: (id: string) => void;
}) {
  const [consent, setConsent] = useState(false),
    [deleteConsent, setDeleteConsent] = useState(false),
    now = useNow();
  useEffect(() => setDeleteConsent(false), [job.version]);
  const usage = runtime?.usage,
    expired = Date.parse(job.expiresAt) <= now;
  const enough = !!usage && computeRemaining(usage).jobs > 0 && computeRemaining(usage).seconds >= job.reservedSeconds;
  const result = job.execution?.result;
  const finished = ["completed", "canceled", "superseded", "unknown"].includes(task.state);
  const canPrepare =
    owner &&
    job.state === "completed" &&
    !job.revoked &&
    job.execution?.browserExited === true &&
    result?.contextClosed === true &&
    ["queued", "blocked", "failed", "awaiting_review"].includes(task.state);
  return (
    <article className="co-browser-job" data-browser-id={job.id} tabIndex={-1}>
      <header>
        <h4>{job.name}</h4>
        <strong>{job.captureDeletion ? "Saved captures deleted" : states[job.state] || job.state}</strong>
      </header>
      <p>
        {job.request.limits.seconds}s browsing · {job.request.limits.requests} requests ·{" "}
        {(job.request.limits.bytes / 1048576).toFixed(1)} MiB maximum
      </p>
      <p>
        {job.settledAt
          ? `${job.allocatedSeconds ?? 0} seconds allocated`
          : job.startedAt
            ? `${job.reservedSeconds} seconds held`
            : `${job.reservedSeconds} seconds will be reserved if approved`}
        , including startup and cleanup. Screenshots{" "}
        {job.request.screenshots ? (job.captureDeletion ? "were approved" : "included") : "off"}.
      </p>
      <details open={job.state === "proposed"}>
        <summary>
          {job.startedAt
            ? "Approved websites and steps"
            : job.revoked
              ? "Saved websites and steps"
              : "Websites and steps to approve"}
        </summary>
        <ul>
          {job.request.origins.map((origin) => (
            <li key={origin}>{origin}</li>
          ))}
        </ul>
        <ol>
          {job.request.steps.map((step, i) => (
            <li key={i}>
              <strong>{actions[step.action]}:</strong> <code>{step.value}</code>
            </li>
          ))}
        </ol>
        <p>
          Viewport: {job.request.width} × {job.request.height}. Public pages only; no sign-in, forms, downloads or
          private files.
        </p>
      </details>
      {job.state === "proposed" && owner && (
        <div className="co-sandbox-actions">
          <p>Nothing has run. Approval is for this itinerary once.</p>
          {executionHold && !finished && <p role="status">{executionHold}</p>}
          {finished ? (
            <p>This task is finished or unavailable. Discard this unused proposal.</p>
          ) : expired ? (
            <p>This proposal expired. Discard it and prepare a fresh review.</p>
          ) : !runtime?.browserAvailable ? (
            <p>New website execution is unavailable.</p>
          ) : usage?.policy.limits.paused ? (
            <p>Compute is paused. Update the shared allowance in Usage before approving.</p>
          ) : (
            !enough && <p>There is not enough shared compute allowance for this itinerary.</p>
          )}
          <label className="co-check">
            <input type="checkbox" checked={consent} onChange={(e) => setConsent(e.target.checked)} disabled={busy} />I
            reviewed these websites, steps, screenshots and limits.
          </label>
          <button
            className="co-primary"
            disabled={
              busy ||
              !consent ||
              expired ||
              finished ||
              !!executionHold ||
              !runtime?.browserAvailable ||
              usage?.policy.limits.paused ||
              !enough
            }
            onClick={() =>
              void act("browser.run", {
                id: job.id,
                expectedVersion: job.version,
                digest: job.digest,
                decision: "approve-once",
              })
            }
          >
            Approve website review once
          </button>
          <button
            disabled={busy}
            onClick={() => void act("browser.revoke", { id: job.id, expectedVersion: job.version })}
          >
            Discard browser proposal
          </button>
        </div>
      )}
      {job.state === "running" && (
        <p role="status">
          {job.dispatchTaken
            ? "Reading the approved pages. You can stop this visit below."
            : "Waiting for the browser worker. Your allowance is held."}
        </p>
      )}
      {job.revoked && (
        <p role="status">Authority revoked. These captures cannot become new task results or feed other work.</p>
      )}
      {job.state === "withheld" && <p>Permission or task context changed. Late captures were withheld.</p>}
      {job.state === "unknown" && (
        <p>The outcome is uncertain. Recovery checks the original resource; it never repeats the visit.</p>
      )}
      {job.state === "cleanup-incomplete" && (
        <p>Browser exit was not confirmed. Any retained captures cannot enter task review.</p>
      )}
      {job.captureDeletion && (
        <div role="status">
          <p>
            Saved page text, links, screenshots and capture diagnostics were deleted{" "}
            {new Date(job.captureDeletion.deletedAt).toLocaleString()}
            {job.captureDeletion.reason === "retention" ? " under your retention policy" : " by an owner"}.
          </p>
          <p>Selected task results, itinerary, usage and audit history remain. No website was revisited.</p>
          <div className="co-sandbox-actions">
            {artifacts
              .filter((a) => a.browserOrigin?.jobId === job.id)
              .map((a) => (
                <button key={a.id} onClick={() => showArtifact(a.id)}>
                  Review saved task result
                </button>
              ))}
            <button onClick={() => download(job)}>Download remaining website receipt</button>
          </div>
        </div>
      )}
      {result && (
        <>
          <p>
            {result.completedSteps} of {job.request.steps.length} steps completed.
          </p>
          {result.partial && (
            <p className="co-browser-partial">
              Partial evidence: some content was blocked, unavailable or shortened. Review the limitations before using
              it.
            </p>
          )}
          {result.error && <p>{result.error}</p>}
          {result.captures.map((capture) => (
            <PageCapture
              key={capture.step}
              capture={capture}
              job={job}
              existing={artifacts.find(
                (a) => a.browserOrigin?.jobId === job.id && a.browserOrigin.step === capture.step,
              )}
              canPrepare={canPrepare}
              busy={busy}
              act={act}
              showArtifact={showArtifact}
            />
          ))}
          {(result.blocked.length > 0 || result.warnings.length > 0) && (
            <details>
              <summary>Capture limitations</summary>
              <ul>
                {result.blocked.map((b, i) => (
                  <li key={i}>
                    {b.url}: {b.reason}
                  </li>
                ))}
                {result.warnings.map((w, i) => (
                  <li key={`w${i}`}>{w}</li>
                ))}
              </ul>
            </details>
          )}
          <button onClick={() => download(job)}>Download website receipt</button>
          {owner && (
            <details className="co-browser-delete">
              <summary>Delete saved captures</summary>
              <p>
                Permanently remove saved page text, links, screenshots and capture diagnostics from current application
                records. Selected task results, their source details, the approved itinerary, usage and audit history
                remain. Existing downloads, backups and database recovery files are not erased here.
              </p>
              {!job.settledAt || !job.release?.confirmed ? (
                <p>Confirm resource cleanup before deleting captures.</p>
              ) : (
                <>
                  <label className="co-check">
                    <input
                      type="checkbox"
                      checked={deleteConsent}
                      disabled={busy}
                      onChange={(e) => setDeleteConsent(e.target.checked)}
                    />
                    I understand which records remain and want to delete these captures.
                  </label>
                  <button
                    disabled={busy || !deleteConsent}
                    onClick={() =>
                      void act("browser.captures.delete", {
                        id: job.id,
                        expectedVersion: job.version,
                        digest: job.resultDigest,
                        decision: "delete-captures",
                      })
                    }
                  >
                    Delete these captures
                  </button>
                </>
              )}
            </details>
          )}
        </>
      )}
      {job.execution && (
        <p className="co-muted">
          {job.execution.network
            ? `Host observed ${job.execution.network.requests} requests and ${job.execution.network.receivedBytes.toLocaleString()} response-body bytes.`
            : "Host network usage is unknown for this receipt."}
        </p>
      )}
      {job.release?.confirmed ? (
        <p className="co-sandbox-released">
          {job.release.status === "not-created"
            ? "No temporary resource was created."
            : "Temporary browser resources released."}
        </p>
      ) : (
        job.release && <p role="status">Resource release is unconfirmed. {job.reservedSeconds} seconds remain held.</p>
      )}
      {owner && job.startedAt && (
        <div className="co-sandbox-actions">
          {!job.revoked && (
            <button
              disabled={busy}
              onClick={() => void act("browser.revoke", { id: job.id, expectedVersion: job.version })}
            >
              {job.state === "running" ? "Stop website review" : "Revoke browser result"}
            </button>
          )}
          {!job.settledAt && job.state !== "running" && (
            <button disabled={busy || !runtime?.browserRecoveryAvailable} onClick={() => void recover(job.id)}>
              Check browser resources and recover
            </button>
          )}
          {!job.settledAt && !runtime?.browserRecoveryAvailable && (
            <p>Recovery needs the original browser runtime configuration.</p>
          )}
        </div>
      )}
    </article>
  );
}

export function BrowserTools({
  task,
  snapshot,
  busy,
  refresh,
}: {
  task: Work;
  snapshot: Snapshot;
  busy: boolean;
  refresh: () => Promise<void>;
}) {
  const [jobs, setJobs] = useState<Job[]>([]),
    [runtime, setRuntime] = useState<Runtime | null>(null);
  const [holds, setHolds] = useState<ComputeHold[]>([]);
  const [working, setWorking] = useState(false),
    [error, setError] = useState(""),
    [editing, setEditing] = useState(false);
  const [focusJob, setFocusJob] = useState(""),
    [focusArtifact, setFocusArtifact] = useState("");
  const [name, setName] = useState("Review public website"),
    [extraOrigins, setExtraOrigins] = useState("");
  const [steps, setSteps] = useState<Step[]>([
    {
      action: "navigate",
      value: snapshot.data.business?.state === "confirmed" ? snapshot.data.business.website || "" : "",
    },
    { action: "read", value: "body" },
  ]);
  const [seconds, setSeconds] = useState(60),
    [requests, setRequests] = useState(40),
    [megabytes, setMegabytes] = useState(8);
  const [screenshots, setScreenshots] = useState(true),
    [viewport, setViewport] = useState("desktop");
  const root = useRef<HTMLDetailsElement>(null),
    loadSequence = useRef(0),
    locked = busy || working,
    owner = snapshot.capabilities.browserOwner === true;
  const load = useCallback(async () => {
    const sequence = ++loadSequence.current;
    const [detail, compute] = await Promise.all([
      api<{ browsers: Job[]; sandboxes: ComputeHold[] }>("/company/tasks/" + task.id),
      api<Runtime>("/company/compute"),
    ]);
    if (sequence !== loadSequence.current) return;
    setJobs(detail.browsers || []);
    setHolds([...(detail.browsers || []), ...(detail.sandboxes || [])]);
    setRuntime(compute);
  }, [task.id]);
  useEffect(() => {
    void load().catch((e) => setError(e.message));
  }, [load, snapshot.data.cursor]);
  const running = jobs.some((j) => j.state === "running" || (j.startedAt && !j.release && !j.settledAt));
  useEffect(() => {
    if (!running) return;
    const timer = window.setInterval(() => void load().catch((e) => setError(e.message)), 1000);
    return () => window.clearInterval(timer);
  }, [running, load]);
  useEffect(() => {
    if (!focusJob) return;
    const el = root.current?.querySelector<HTMLElement>(`[data-browser-id="${CSS.escape(focusJob)}"]`);
    if (el) {
      el.focus();
      el.scrollIntoView({ block: "start" });
      setFocusJob("");
    }
  }, [focusJob, jobs]);
  useEffect(() => {
    if (!focusArtifact) return;
    const el = document.querySelector<HTMLElement>(`[data-artifact-id="${CSS.escape(focusArtifact)}"]`);
    if (el) {
      el.focus();
      el.scrollIntoView({ block: "center" });
      setFocusArtifact("");
    }
  }, [focusArtifact, snapshot.data.artifacts]);
  async function act(action: string, fields: Record<string, unknown>) {
    if (locked) return;
    setWorking(true);
    setError("");
    try {
      const result = await command(
        action,
        fields,
        undefined,
        action === "browser.prepare"
          ? "/company/browsers/prepare"
          : action === "browser.output.prepare"
            ? "/company/browsers/outputs/prepare"
            : "/company/commands",
      );
      await load();
      await refresh();
      return result;
    } catch (e) {
      setError((e as Error).message);
      await load().catch(() => {});
      if (e instanceof ApiError && e.status === 409) {
        await refresh();
        setError(
          `${e.message} The task status has been refreshed. Your itinerary is kept; review it before submitting again.`,
        );
      }
    } finally {
      setWorking(false);
    }
  }
  async function recover(id: string) {
    if (locked) return;
    setWorking(true);
    setError("");
    try {
      await api("/company/browsers/" + id + "/recover", "POST", {});
      await load();
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setWorking(false);
    }
  }
  function updateStep(index: number, value: Partial<Step>) {
    setSteps((current) => current.map((step, i) => (i === index ? { ...step, ...value } : step)));
  }
  async function prepare(event: FormEvent) {
    event.preventDefault();
    setError("");
    let origins: string[];
    try {
      origins = [
        ...new Set([
          ...steps
            .filter((s) => s.action === "navigate")
            .map((s) => {
              const u = new URL(s.value);
              if (u.protocol !== "https:" || u.username || u.password || u.port) throw new Error();
              return u.origin;
            }),
          ...extraOrigins
            .split(/\r?\n/)
            .map((s) => s.trim())
            .filter(Boolean),
        ]),
      ];
      if (origins.length < 1 || origins.length > 4) throw new Error();
    } catch {
      setError("Use public HTTPS addresses and at most four allowed websites.");
      return;
    }
    const result = (await act("browser.prepare", {
      id: task.id,
      expectedVersion: task.version,
      body: JSON.stringify({
        name,
        origins,
        steps,
        limits: { seconds, requests, bytes: megabytes * 1048576 },
        width: viewport === "mobile" ? 390 : 1280,
        height: viewport === "mobile" ? 844 : 800,
        screenshots,
      }),
    })) as Job | undefined;
    if (result?.id) {
      setEditing(false);
      setFocusJob(result.id);
    }
  }
  return (
    <details ref={root} className="co-sandbox co-browser" aria-label="Website review tools">
      <summary>Review public websites{jobs.length > 0 ? ` · ${jobs.length} saved` : ""}</summary>
      <p>
        Read public pages in a fresh browser and keep dated evidence for this task. Preparing a review does not visit a
        website.
      </p>
      {task.planId && (
        <p>
          {task.state === "completed"
            ? "This plan assignment is complete. Its reviewed website evidence is saved below."
            : "Part of an approved plan. Complete and approve predecessor results first. Each website review needs its own exact consent and uses shared compute; the plan's token ceiling stays unchanged."}
        </p>
      )}
      {error && (
        <p role="alert" className="co-error">
          {error}
        </p>
      )}
      {runtime?.browserRuntime.status === "fixture" && (
        <p>Fictional test runtime: these receipts do not visit real websites.</p>
      )}
      {runtime && !runtime.browserAvailable && (
        <p role="status">{browserRuntimeMessage(runtime.browserRuntime.status)}</p>
      )}
      {!owner && <p>Only an owner can approve browser visits or prepare captured text for review.</p>}
      {runtime && <ComputeAllowance usage={runtime.usage} />}
      <button
        disabled={locked}
        onClick={() =>
          void load()
            .then(() => setError(""))
            .catch((e) => setError(e.message))
        }
      >
        Refresh website reviews
      </button>
      {jobs.map((job) => (
        <BrowserReview
          key={job.id}
          job={job}
          task={task}
          artifacts={snapshot.data.artifacts}
          runtime={runtime}
          owner={owner}
          executionHold={taskExecutionHold(task, holds)}
          busy={locked}
          act={act}
          recover={recover}
          showArtifact={setFocusArtifact}
        />
      ))}
      {owner &&
        runtime?.browserAvailable &&
        task.kind === "work" &&
        !["completed", "canceled", "superseded", "unknown"].includes(task.state) &&
        !task.meetingId &&
        !task.scheduleRunId && (
          <details open={editing} onToggle={(e) => setEditing(e.currentTarget.open)}>
            <summary>Prepare a website review</summary>
            <form onSubmit={(e) => void prepare(e)}>
              <fieldset disabled={locked}>
                <label>
                  Review name
                  <input required maxLength={120} value={name} onChange={(e) => setName(e.target.value)} />
                </label>
                <label>
                  Website address
                  <input
                    type="url"
                    required
                    maxLength={2000}
                    placeholder="https://example.com/services"
                    value={steps[0].value}
                    onChange={(e) => updateStep(0, { value: e.target.value })}
                  />
                </label>
                <label className="co-check">
                  <input type="checkbox" checked={screenshots} onChange={(e) => setScreenshots(e.target.checked)} />
                  Save screenshots with captured text
                </label>
                <label>
                  Page size
                  <select value={viewport} onChange={(e) => setViewport(e.target.value)}>
                    <option value="desktop">Desktop</option>
                    <option value="mobile">Mobile</option>
                  </select>
                </label>
                <details>
                  <summary>Advanced steps and limits</summary>
                  <p>
                    CSS selectors identify page elements. Clicks cannot submit forms or sign in. Content outside the
                    allowed websites may be blocked.
                  </p>
                  {steps.map((step, i) => (
                    <div className="co-browser-step" key={i}>
                      <label>
                        Step {i + 1}
                        <select
                          aria-label={`Step ${i + 1} action`}
                          disabled={i === 0}
                          value={step.action}
                          onChange={(e) => updateStep(i, { action: e.target.value as Step["action"] })}
                        >
                          {Object.entries(actions).map(([key, label]) => (
                            <option key={key} value={key}>
                              {label}
                            </option>
                          ))}
                        </select>
                      </label>
                      <label>
                        {step.action === "navigate" ? "Page address" : "Element selector"}
                        <input
                          required
                          maxLength={step.action === "navigate" ? 2000 : 300}
                          value={step.value}
                          onChange={(e) => updateStep(i, { value: e.target.value })}
                        />
                      </label>
                      {i > 0 && (
                        <button type="button" onClick={() => setSteps(steps.filter((_, index) => index !== i))}>
                          Remove step {i + 1}
                        </button>
                      )}
                    </div>
                  ))}
                  <button
                    type="button"
                    disabled={steps.length >= 16}
                    onClick={() => setSteps([...steps, { action: "read", value: "body" }])}
                  >
                    Add step
                  </button>
                  <label>
                    Additional websites allowed
                    <textarea
                      rows={2}
                      placeholder="https://cdn.example.com"
                      value={extraOrigins}
                      maxLength={8000}
                      onChange={(e) => setExtraOrigins(e.target.value)}
                    />
                  </label>
                  <label>
                    Maximum browsing seconds
                    <input
                      type="number"
                      required
                      min={1}
                      max={120}
                      value={seconds}
                      onChange={(e) => setSeconds(Number(e.target.value))}
                    />
                  </label>
                  <label>
                    Maximum requests
                    <input
                      type="number"
                      required
                      min={1}
                      max={100}
                      value={requests}
                      onChange={(e) => setRequests(Number(e.target.value))}
                    />
                  </label>
                  <label>
                    Maximum response MiB
                    <input
                      type="number"
                      required
                      min={1}
                      max={16}
                      value={megabytes}
                      onChange={(e) => setMegabytes(Number(e.target.value))}
                    />
                  </label>
                </details>
                <p>
                  This review reserves {seconds + 12} seconds from shared compute. You will review the exact websites
                  and steps before execution.
                </p>
                <button type="submit" className="co-primary">
                  Prepare website review
                </button>
              </fieldset>
            </form>
          </details>
        )}
    </details>
  );
}
