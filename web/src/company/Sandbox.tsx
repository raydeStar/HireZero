import { useCallback, useEffect, useRef, useState, type FormEvent } from "react";
import { api, ApiError } from "../lib/api";
import { command, type Artifact, type Snapshot, type Work } from "./api";
import "./sandbox.css";

type File = { name: string; text: string };
type Release = { confirmed: boolean; status: string; resourceId: string };
type Job = {
  id: string;
  taskId: string;
  name: string;
  digest: string;
  resultDigest: string | null;
  version: number;
  state: string;
  revoked: boolean;
  expiresAt: string;
  startedAt: string | null;
  settledAt: string | null;
  allocatedSeconds: number | null;
  runtimeIdentity: string;
  request: {
    tier?: string | null;
    script: string;
    inputs: File[];
    outputs: string[];
    limits: { seconds: number; memoryMiB: number; temporaryMiB: number };
  };
  documents: { documentId: string; version: number; digest: string; name: string }[];
  evidence?: { artifactId: string; version: number; digest: string; name: string }[] | null;
  plan?: { id: string; digest: string } | null;
  premium?: { planDigest: string; maximumSeconds: number } | null;
  release: Release | null;
  result: {
    stdout: string;
    stderr: string;
    outputs: File[];
    reason: string | null;
    wallMilliseconds: number | null;
  } | null;
};
type Document = {
  id: string;
  title: string;
  state: string;
  departmentId: string | null;
  employeeId: string | null;
  bytes: number;
};
type EvidenceInput = { id: string; taskId: string; employeeName: string; taskName: string; bytes: number };
type Runtime = {
  available: boolean;
  recoveryAvailable: boolean;
  scriptTiers?: {
    extendedRuntime: boolean;
    extendedMaximumSeconds: number | null;
    status: string;
    planDigest: string | null;
  };
};
export type ComputeHold = { startedAt: string | null; settledAt: string | null };
export function taskExecutionHold(task: Work, jobs: ComputeHold[]) {
  if (["completed", "canceled", "superseded", "unknown"].includes(task.state))
    return "This task is finished or unavailable. Discard any unused execution proposals.";
  if (task.state === "running")
    return "Finish or resolve this task's current model request before starting another execution.";
  if (jobs.some((job) => job.startedAt !== null && job.settledAt === null))
    return "Finish or recover this task's current script or browser resources before starting another execution.";
  return "";
}
export type ComputeUsage = {
  period: string;
  resetsAt: string;
  jobs: number;
  allocatedSeconds: number;
  heldSeconds: number;
  heldJobs: number;
  carriedJobs: number;
  carriedSeconds: number;
  policy: { version: number; limits: { jobs: number; seconds: number; paused: boolean } };
  subscriptionRequired?: boolean;
  subscriptionAllowance?: { computeJobs: number; computeSeconds: number } | null;
};
const states: Record<string, string> = {
  proposed: "Ready for review",
  running: "Running",
  completed: "Output ready",
  failed: "Script failed",
  "timed-out": "Time limit reached",
  "output-limit": "Output limit reached",
  unknown: "Outcome unknown",
  "not-sent": "Script did not run",
  withheld: "Output withheld",
  revoked: "Proposal discarded",
};
const nameValid = (name: string) => /^[a-zA-Z0-9][a-zA-Z0-9._-]{0,99}$/.test(name) && name !== "job.mjs";

function downloadOutput(file: File) {
  const url = URL.createObjectURL(new Blob([file.text], { type: "text/plain;charset=utf-8" }));
  const link = document.createElement("a");
  link.href = url;
  link.download = file.name;
  link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

function OutputReview({
  job,
  file,
  existing,
  acceptsResult,
  busy,
  act,
  showArtifact,
}: {
  job: Job;
  file: File;
  existing?: Artifact;
  acceptsResult: boolean;
  busy: boolean;
  act: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  showArtifact: (id: string) => void;
}) {
  const [quote, setQuote] = useState(file.text.length <= 12000 ? file.text : "");
  if (existing)
    return (
      <p>
        This output has a saved task result.{" "}
        <button onClick={() => showArtifact(existing.id)}>Review task result</button>
      </p>
    );
  if (!acceptsResult)
    return <p>This task cannot accept another result in its current state. The full file remains available above.</p>;
  const exact = quote.trim().length > 0 && quote.length <= 12000 && file.text.includes(quote);
  return (
    <details className="co-sandbox-output-review">
      <summary>Use as a task result</summary>
      <p>
        Prepare this output for the task’s existing review flow. It stays unapproved until you review it; sharing with
        another employee is a separate action.
      </p>
      {file.text.length > 12000 && (
        <p>
          This file is longer than a task result. Paste up to 12,000 exact characters below; the complete file stays
          saved and downloadable.
        </p>
      )}
      <label>
        Result text for review
        <textarea value={quote} maxLength={12000} onChange={(e) => setQuote(e.target.value)} />
      </label>
      <p>
        {quote.length.toLocaleString()} / 12,000 characters. Use the saved text or an exact excerpt. Credential-like
        values may be redacted in the review.
      </p>
      {quote.length > 0 && !exact && <p role="status">Choose text copied exactly from this output.</p>}
      <button
        disabled={busy || !exact}
        onClick={async () => {
          const result = (await act("sandbox.output.prepare", {
            id: job.id,
            expectedVersion: job.version,
            digest: job.resultDigest,
            decision: "prepare-review",
            body: JSON.stringify({ outputName: file.name, quote }),
          })) as Artifact | undefined;
          if (result?.id) showArtifact(result.id);
        }}
      >
        Prepare task result for review
      </button>
    </details>
  );
}

export function computeRemaining(usage: ComputeUsage) {
  const jobs = Math.min(
    usage.policy.limits.jobs,
    usage.subscriptionRequired ? (usage.subscriptionAllowance?.computeJobs ?? 0) : Infinity,
  );
  const seconds = Math.min(
    usage.policy.limits.seconds,
    usage.subscriptionRequired ? (usage.subscriptionAllowance?.computeSeconds ?? 0) : Infinity,
  );
  return {
    jobs: Math.max(0, jobs - usage.jobs - usage.carriedJobs),
    seconds: Math.max(0, seconds - usage.allocatedSeconds - usage.heldSeconds - usage.carriedSeconds),
  };
}
export function ComputeAllowance({ usage }: { usage: ComputeUsage }) {
  const { limits } = usage.policy;
  const remaining = computeRemaining(usage);
  return (
    <div className="co-sandbox-allowance">
      <strong>{limits.paused ? "Compute execution is paused" : "Shared monthly compute allowance"}</strong>
      <p>
        {remaining.jobs} {remaining.jobs === 1 ? "job" : "jobs"} and {remaining.seconds} allocated compute{" "}
        {remaining.seconds === 1 ? "second" : "seconds"} remain.
      </p>
      {usage.subscriptionRequired && (
        <p>
          {usage.subscriptionAllowance
            ? "Your purchased compute allowance and safety limits both apply."
            : "New compute work needs a verified subscription allowance. Review payment status in Billing."}
        </p>
      )}
      <p>
        Unresolved work holds {usage.heldSeconds + usage.carriedSeconds} seconds. These safety allocations are separate
        from AI tokens and are not a monetary charge.
      </p>
      {usage.carriedJobs > 0 && <p>{usage.carriedJobs} earlier jobs still hold allowance across the monthly reset.</p>}
    </div>
  );
}

function ScriptReview({
  job,
  artifacts,
  acceptsResult,
  showArtifact,
  owner,
  available,
  recoveryAvailable,
  paused,
  allowance,
  executionHold,
  busy,
  act,
  recover,
}: {
  job: Job;
  artifacts: Artifact[];
  acceptsResult: boolean;
  showArtifact: (id: string) => void;
  owner: boolean;
  available: boolean;
  recoveryAvailable: boolean;
  paused: boolean;
  allowance: boolean;
  executionHold: string;
  busy: boolean;
  act: (action: string, fields: Record<string, unknown>) => Promise<unknown>;
  recover: (id: string) => Promise<void>;
}) {
  const [consent, setConsent] = useState(false);
  const pending = job.startedAt !== null && job.settledAt === null;
  const expired = new Date(job.expiresAt).getTime() <= Date.now();
  return (
    <article className="co-sandbox-job" tabIndex={-1} aria-label={job.name + " script"} data-sandbox-id={job.id}>
      <header>
        <strong>{job.name}</strong>
        <span>{states[job.state] || job.state}</span>
      </header>
      <p>
        {job.request.tier ? "Extended script · " : "Standard script · "}
        {job.request.limits.seconds} seconds maximum · {job.request.limits.memoryMiB} MiB memory ·{" "}
        {job.request.limits.temporaryMiB} MiB working files · No network access
      </p>
      <details open={job.state === "proposed"}>
        <summary>Exact script and inputs</summary>
        <pre className="co-sandbox-code">{job.request.script}</pre>
        <h5>Input files</h5>
        {job.request.inputs.length === 0 ? (
          <p>No company files are supplied.</p>
        ) : (
          job.request.inputs.map((file) => (
            <details key={file.name}>
              <summary>
                {file.name} ·{" "}
                {job.evidence?.some((e) => e.name === file.name) ? "approved task result" : "approved document"}{" "}
                revision{" "}
                {job.evidence?.find((e) => e.name === file.name)?.version ??
                  job.documents.find((d) => d.name === file.name)?.version}
              </summary>
              <pre>{file.text}</pre>
            </details>
          ))
        )}
        <p>Selected output files: {job.request.outputs.join(", ") || "None; only console output is collected"}.</p>
        <small>
          Review reference {job.digest.slice(0, 16)} · {job.runtimeIdentity}
        </small>
      </details>
      {job.state === "proposed" && owner && (
        <div className="co-sandbox-actions">
          <label className="co-check">
            <input
              type="checkbox"
              checked={consent}
              onChange={(e) => setConsent(e.target.checked)}
              disabled={busy || expired || !available}
            />
            I reviewed this script, its files and limits.
          </label>
          {expired ? (
            <p>This review expired. Discard it and prepare a fresh proposal.</p>
          ) : (
            <p>Approval permits this exact script to run once in an isolated environment.</p>
          )}
          {job.request.tier && (
            <p>
              Reserves {job.request.limits.seconds} seconds from your monthly compute allowance. No extra usage is
              purchased. This run requires its reviewed paid plan and stops if that entitlement is lost.
            </p>
          )}
          <button
            className="co-primary"
            disabled={busy || !consent || expired || !available || paused || !allowance || !!executionHold}
            onClick={() =>
              void act("sandbox.run", {
                id: job.id,
                expectedVersion: job.version,
                digest: job.digest,
                decision: "approve-once",
              })
            }
          >
            Approve and run once
          </button>
          {executionHold && <p role="status">{executionHold}</p>}
          {!allowance && <p>This script exceeds the remaining monthly allowance. Review script limits in Usage.</p>}
          <button
            disabled={busy}
            onClick={() => void act("sandbox.revoke", { id: job.id, expectedVersion: job.version })}
          >
            Discard proposal
          </button>
        </div>
      )}
      {job.state === "running" && (
        <p role="status">
          {job.revoked
            ? "Stopping. The allowance stays held until resource release is confirmed."
            : "The approved script is queued or running. Its result will appear here."}
        </p>
      )}
      {job.result && (
        <div className="co-sandbox-result">
          {job.result.outputs.map((file) => (
            <details key={file.name} open>
              <summary>Output: {file.name}</summary>
              <pre>{file.text || "Empty file"}</pre>
              <button onClick={() => downloadOutput(file)}>Download {file.name}</button>
              {owner && job.state === "completed" && !job.revoked && (
                <OutputReview
                  key={job.resultDigest + file.name}
                  job={job}
                  file={file}
                  busy={busy}
                  acceptsResult={acceptsResult}
                  existing={artifacts.find(
                    (a) => a.sandboxOrigin?.jobId === job.id && a.sandboxOrigin.outputName === file.name,
                  )}
                  act={act}
                  showArtifact={showArtifact}
                />
              )}
            </details>
          ))}
          {job.result.stdout && (
            <details open>
              <summary>Console output</summary>
              <pre>{job.result.stdout}</pre>
            </details>
          )}
          {job.result.stderr && (
            <details open>
              <summary>Script diagnostics</summary>
              <pre>{job.result.stderr}</pre>
            </details>
          )}
          {job.state === "completed" && job.result.outputs.length === 0 && !job.result.stdout && (
            <p>The script finished without selected files or console output.</p>
          )}
          {job.state === "withheld" && <p>Permission or approved context changed. Late output was withheld.</p>}
          {job.result.reason && (
            <details>
              <summary>Execution details</summary>
              <p>Runtime status: {job.result.reason.replaceAll("-", " ")}</p>
            </details>
          )}
        </div>
      )}
      {job.state === "unknown" && (
        <p role="alert">
          The script's outcome could not be confirmed. This job will not run again.{" "}
          {job.release?.confirmed
            ? "Its temporary resources have been released. There is no resource left to check."
            : !owner
              ? "A workspace owner needs to check the saved resource and confirm cleanup."
              : recoveryAvailable
                ? "Use Check resource and recover to look for a saved result and confirm cleanup."
                : "Recovery is currently unavailable. This job needs a resource check once recovery is restored."}
        </p>
      )}
      {pending && job.state !== "running" && (
        <p role="status">
          Resource release is unconfirmed. {job.request.limits.seconds} seconds remain held
          {job.result?.outputs.length ? "; the output above is already saved" : ""}.
        </p>
      )}
      {job.release?.confirmed && (
        <p className="co-sandbox-released">
          Temporary resources released · {job.allocatedSeconds ?? 0} script seconds allocated.
        </p>
      )}
      {pending && owner && (
        <div className="co-sandbox-actions">
          {!job.revoked && (
            <button
              disabled={busy}
              onClick={() => void act("sandbox.revoke", { id: job.id, expectedVersion: job.version })}
            >
              Stop script authority
            </button>
          )}
          {job.state !== "running" && (
            <button disabled={busy || !recoveryAvailable} onClick={() => void recover(job.id)}>
              Check resource and recover
            </button>
          )}
        </div>
      )}
      {job.release && !job.release.confirmed && (
        <details>
          <summary>Resource receipt</summary>
          <p>{job.release.status.replaceAll("-", " ")}</p>
          <code>{job.release.resourceId}</code>
        </details>
      )}
    </article>
  );
}

export function SandboxTools({
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
    [documents, setDocuments] = useState<Document[]>([]),
    [evidenceInputs, setEvidenceInputs] = useState<EvidenceInput[]>([]),
    [usage, setUsage] = useState<ComputeUsage | null>(null);
  const [runtime, setRuntime] = useState<Runtime | null>(null);
  const [tier, setTier] = useState("standard");
  const [holds, setHolds] = useState<ComputeHold[]>([]);
  const [error, setError] = useState(""),
    [working, setWorking] = useState(false),
    [loaded, setLoaded] = useState(false);
  const [name, setName] = useState(""),
    [script, setScript] = useState(""),
    [selected, setSelected] = useState<string[]>([]),
    [selectedEvidence, setSelectedEvidence] = useState<string[]>([]),
    [outputs, setOutputs] = useState("report.txt"),
    [seconds, setSeconds] = useState(30);
  const root = useRef<HTMLDetailsElement>(null);
  const [editing, setEditing] = useState(false),
    [focusJob, setFocusJob] = useState("");
  const [focusArtifact, setFocusArtifact] = useState("");
  useEffect(() => {
    if (!focusArtifact) return;
    const element = document.querySelector<HTMLElement>(`[data-artifact-id="${focusArtifact}"]`);
    if (element) {
      element.focus();
      element.scrollIntoView({ block: "center" });
      setFocusArtifact("");
    }
  }, [focusArtifact, snapshot.data.artifacts]);
  const available = runtime?.available ?? snapshot.capabilities.sandboxExecution === true,
    recoveryAvailable = runtime?.recoveryAvailable ?? snapshot.capabilities.sandboxRecovery === true,
    owner = snapshot.capabilities.sandboxOwner === true;
  const extendedMaximum = runtime?.scriptTiers?.extendedRuntime ? runtime.scriptTiers.extendedMaximumSeconds : null;
  const employee = snapshot.data.employees.find((e) => e.id === task.employeeId);
  const companyOnly = snapshot.data.threads.find((t) => t.id === task.threadId)?.scope === "company";
  const eligible = documents.filter(
    (d) =>
      d.state === "approved" &&
      ((!d.departmentId && !d.employeeId) ||
        (!companyOnly &&
          (d.employeeId === task.employeeId || (d.departmentId === employee?.departmentId && !d.employeeId)))),
  );
  const fileName = (id: string) => `source-${documents.findIndex((d) => d.id === id) + 1}.txt`;
  const evidenceFileName = (id: string) => `result-${evidenceInputs.findIndex((e) => e.id === id) + 1}.txt`;
  const inputNames = [...selected.map(fileName), ...selectedEvidence.map(evidenceFileName)];
  const load = useCallback(async () => {
    const [detail, docs, compute, evidence] = await Promise.all([
      api<{ sandboxes: Job[]; browsers: ComputeHold[] }>("/company/tasks/" + task.id),
      api<Document[]>("/company/documents"),
      api<Runtime & { usage: ComputeUsage }>("/company/compute"),
      api<EvidenceInput[]>("/company/tasks/" + task.id + "/sandbox-inputs"),
    ]);
    setJobs(detail.sandboxes || []);
    setHolds([...(detail.sandboxes || []), ...(detail.browsers || [])]);
    setDocuments(docs);
    setEvidenceInputs(evidence);
    setUsage(compute.usage);
    setRuntime(compute);
    setLoaded(true);
  }, [task.id]);
  useEffect(() => {
    let current = true;
    void load().catch((e) => {
      if (current) setError(e.message);
    });
    return () => {
      current = false;
    };
  }, [load, snapshot.data.cursor]);
  const running = jobs.some((j) => j.state === "running");
  useEffect(() => {
    if (!focusJob) return;
    const review = root.current?.querySelector<HTMLElement>(`[data-sandbox-id="${CSS.escape(focusJob)}"]`);
    if (review) {
      review.scrollIntoView({ block: "start" });
      review.focus({ preventScroll: true });
      setFocusJob("");
    }
  }, [focusJob, jobs]);
  useEffect(() => {
    if (!running) return;
    const timer = window.setInterval(() => void load().catch((e) => setError(e.message)), 1000);
    return () => window.clearInterval(timer);
  }, [running, load]);
  async function act(action: string, fields: Record<string, unknown>) {
    if (busy || working) return;
    setWorking(true);
    setError("");
    try {
      const result = await command(
        action,
        fields,
        undefined,
        action === "sandbox.prepare"
          ? "/company/sandboxes/prepare"
          : action === "sandbox.output.prepare"
            ? "/company/sandboxes/outputs/prepare"
            : "/company/commands",
      );
      await load();
      await refresh();
      return result;
    } catch (e) {
      setError((e as Error).message);
      if (e instanceof ApiError && e.status === 409) {
        await refresh().catch(() => {});
        setError(
          (e as Error).message +
            " The task status has been refreshed. Your draft is kept; review it before submitting again.",
        );
      }
      await load().catch(() => {});
    } finally {
      setWorking(false);
    }
  }
  async function recover(id: string) {
    setWorking(true);
    setError("");
    try {
      await api("/company/sandboxes/" + id + "/recover", "POST", {});
      await load();
      await refresh();
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setWorking(false);
    }
  }
  function example() {
    setName("Count rows in selected files");
    setOutputs("report.txt");
    setScript(
      `import fs from 'node:fs';\nconst files = ${JSON.stringify(inputNames)};\nconst rows = files.map(name => ({ name, lines: fs.readFileSync(name, 'utf8').trim().split(/\\r?\\n/).length }));\nfs.writeFileSync('report.txt', JSON.stringify(rows, null, 2));\n`,
    );
  }
  async function prepare(event: FormEvent) {
    event.preventDefault();
    const names = outputs
      .split(/[\n,]/)
      .map((n) => n.trim())
      .filter(Boolean);
    if (
      !script.trim() ||
      new TextEncoder().encode(script).length > 32768 ||
      names.length > 8 ||
      names.some((n) => !nameValid(n)) ||
      new Set(names.map((n) => n.toLowerCase())).size !== names.length ||
      names.some((n) => inputNames.includes(n))
    ) {
      setError(
        "Use a script up to 32 KiB and at most eight distinct output filenames, with no paths or input-name conflicts.",
      );
      return;
    }
    const result = await act("sandbox.prepare", {
      id: task.id,
      expectedVersion: task.version,
      body: JSON.stringify({
        name,
        script,
        documents: selected.map((id) => ({ documentId: id, name: fileName(id) })),
        evidence: selectedEvidence.map((id) => ({ artifactId: id, name: evidenceFileName(id) })),
        outputs: names,
        limits: { seconds, memoryMiB: 256, temporaryMiB: 32 },
        ...(tier === "extended" ? { tier: "extended-script-v1" } : {}),
      }),
    });
    if (result && typeof result === "object" && "id" in result && typeof result.id === "string") {
      setEditing(false);
      setFocusJob(result.id);
    }
  }
  if (loaded && !available && jobs.length === 0) return null;
  const disabled = busy || working;
  const selectedBytes =
    documents.filter((d) => selected.includes(d.id)).reduce((total, d) => total + d.bytes, 0) +
    evidenceInputs.filter((e) => selectedEvidence.includes(e.id)).reduce((total, e) => total + e.bytes, 0);
  const selectedCount = selected.length + selectedEvidence.length;
  const unresolved = jobs.some((j) => j.startedAt !== null && j.settledAt === null);
  const standalone = !task.meetingId && !task.scheduleRunId;
  return (
    <details ref={root} className="co-sandbox" aria-label="Isolated script tools">
      <summary>Script tools{jobs.length ? ` · ${jobs.length} saved` : ""}</summary>
      <p>
        Run a reviewed JavaScript calculation or transformation using selected company text files. The environment has
        no network, account credentials or host-file access.
      </p>
      {task.planId && (
        <p>
          {task.state === "completed"
            ? "This plan assignment is complete. Its reviewed script history is saved below."
            : "Part of an approved plan. Finish and approve predecessor results first. Each script needs its own exact approval and uses the shared compute allowance; the plan's token ceiling stays unchanged."}
        </p>
      )}
      {error && (
        <p role="alert" className="co-error">
          {error}
        </p>
      )}
      {!loaded && !error && <p>Loading script history…</p>}
      <button
        disabled={disabled}
        onClick={() =>
          void load()
            .then(() => setError(""))
            .catch((e) => setError(e.message))
        }
      >
        Refresh script status
      </button>
      {!available && (
        <p>
          New script execution is unavailable. Saved results remain here.
          {recoveryAvailable
            ? " You can still check recorded resources and recover saved jobs."
            : " An operator must restore the original runtime configuration to recover unfinished jobs."}
        </p>
      )}
      {usage && <ComputeAllowance usage={usage} />}
      {!owner && <p>Only a company owner can prepare, approve or stop a script.</p>}
      {jobs.map((job) => (
        <ScriptReview
          key={job.id + ":" + job.digest}
          job={job}
          artifacts={snapshot.data.artifacts}
          acceptsResult={
            task.kind === "work" && ["queued", "blocked", "failed", "awaiting_review"].includes(task.state)
          }
          showArtifact={setFocusArtifact}
          owner={owner}
          available={available}
          recoveryAvailable={recoveryAvailable}
          paused={!!usage?.policy.limits.paused}
          allowance={
            !!usage && computeRemaining(usage).jobs > 0 && computeRemaining(usage).seconds >= job.request.limits.seconds
          }
          executionHold={
            taskExecutionHold(task, holds) ||
            (job.request.tier &&
            (!extendedMaximum ||
              job.premium?.planDigest !== runtime?.scriptTiers?.planDigest ||
              job.request.limits.seconds > extendedMaximum)
              ? "This extended review no longer matches an available runtime and paid plan. Check Billing, then prepare a fresh script review."
              : "")
          }
          busy={disabled}
          act={act}
          recover={recover}
        />
      ))}
      {loaded &&
        available &&
        owner &&
        standalone &&
        !["completed", "canceled", "superseded", "unknown"].includes(task.state) && (
          <details
            open={editing}
            onToggle={(event) => {
              if (event.target === event.currentTarget) setEditing(event.currentTarget.open);
            }}
          >
            <summary>Prepare a script</summary>
            {unresolved && <p>Resolve this task’s current script and resource hold before preparing more work.</p>}
            <form onSubmit={(event) => void prepare(event)}>
              <fieldset disabled={disabled || unresolved}>
                <label>
                  Script name
                  <input required maxLength={120} value={name} onChange={(e) => setName(e.target.value)} />
                </label>
                <fieldset>
                  <legend>Approved input documents</legend>
                  <p>Choose up to eight files, 128 KiB combined. {(selectedBytes / 1024).toFixed(1)} KiB selected.</p>
                  {eligible.length === 0 && (
                    <p>No approved documents for this task. A script can still run without input files.</p>
                  )}
                  {eligible.map((d) => (
                    <label className="co-check" key={d.id}>
                      <input
                        type="checkbox"
                        checked={selected.includes(d.id)}
                        disabled={
                          !selected.includes(d.id) && (selectedCount >= 8 || selectedBytes + d.bytes > 128 * 1024)
                        }
                        onChange={(e) =>
                          setSelected(e.target.checked ? [...selected, d.id] : selected.filter((id) => id !== d.id))
                        }
                      />
                      {d.title} <code>{fileName(d.id)}</code>
                    </label>
                  ))}
                </fieldset>
                {evidenceInputs.length > 0 && (
                  <fieldset>
                    <legend>Approved task results</legend>
                    <p>
                      Select only the results this script needs. Employee conversations and private notes are not
                      included.
                    </p>
                    {evidenceInputs.map((item) => (
                      <label className="co-check" key={item.id}>
                        <input
                          type="checkbox"
                          checked={selectedEvidence.includes(item.id)}
                          disabled={
                            !selectedEvidence.includes(item.id) &&
                            (selectedCount >= 8 || selectedBytes + item.bytes > 128 * 1024)
                          }
                          onChange={(e) =>
                            setSelectedEvidence(
                              e.target.checked
                                ? [...selectedEvidence, item.id]
                                : selectedEvidence.filter((id) => id !== item.id),
                            )
                          }
                        />
                        <span>
                          {item.employeeName} · {item.taskName.slice(0, 100)} <code>{evidenceFileName(item.id)}</code>
                        </span>
                      </label>
                    ))}
                  </fieldset>
                )}
                <button type="button" onClick={example}>
                  Use row-count example
                </button>
                <label>
                  JavaScript to review
                  <textarea
                    required
                    rows={9}
                    value={script}
                    onChange={(e) => setScript(e.target.value)}
                    spellCheck={false}
                  />
                </label>
                <label>
                  Output filenames
                  <textarea rows={2} value={outputs} onChange={(e) => setOutputs(e.target.value)} />
                  <small>One name per line. UTF-8 text only; at most 64 KiB combined. No file paths.</small>
                </label>
                <label>
                  Execution tier
                  <select
                    value={tier}
                    onChange={(e) => {
                      setTier(e.target.value);
                      setSeconds(Math.min(seconds, e.target.value === "extended" ? (extendedMaximum ?? 120) : 120));
                    }}
                  >
                    <option value="standard">Standard · up to 120 seconds</option>
                    <option value="extended" disabled={!extendedMaximum}>
                      Extended{extendedMaximum ? ` · up to ${extendedMaximum} seconds` : " · unavailable"}
                    </option>
                  </select>
                </label>
                {runtime?.scriptTiers && <p>{runtime.scriptTiers.status}</p>}
                {tier === "extended" && (
                  <p>
                    The full duration is reserved from your plan’s existing monthly compute allowance. This run cannot
                    exceed the approved duration, resume after a host restart, or purchase extra usage. Losing the paid
                    entitlement stops new work and cancels an active extended run.
                  </p>
                )}
                <label>
                  Maximum script seconds
                  <input
                    type="number"
                    min={1}
                    max={tier === "extended" ? (extendedMaximum ?? 120) : 120}
                    required
                    value={seconds}
                    onChange={(e) => setSeconds(Number(e.target.value))}
                  />
                </label>
                <button className="co-primary" type="submit" disabled={tier === "extended" && !extendedMaximum}>
                  Prepare exact review
                </button>
                <p>
                  Preparing a review does not execute the script. Enable a monthly script allowance in Usage before
                  approving execution.
                </p>
              </fieldset>
            </form>
          </details>
        )}
    </details>
  );
}
