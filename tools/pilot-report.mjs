import fs from "node:fs/promises";
import path from "node:path";
import { createHash } from "node:crypto";
import { pathToFileURL } from "node:url";

const fail = (message) => {
  throw new Error(message);
};
const object = (value) =>
  value !== null && typeof value === "object" && !Array.isArray(value);
const count = (value) =>
  Number.isSafeInteger(value) && value >= 0 && value <= 1_000_000_000;
const amount = (value) =>
  typeof value === "number" &&
  Number.isFinite(value) &&
  value >= 0 &&
  value <= 1_000_000_000_000;
const timestamp = (value) =>
  typeof value === "string" &&
  /^\d{4}-\d{2}-\d{2}T.+(?:Z|[+-]\d{2}:\d{2})$/.test(value) &&
  Number.isFinite(Date.parse(value));
const sum = (values) =>
  Number(values.reduce((total, value) => total + value, 0).toPrecision(12));
const keys = (value, allowed, label) => {
  if (
    !object(value) ||
    Object.keys(value).some((key) => !allowed.includes(key))
  )
    fail(`${label}: unexpected fields or invalid object.`);
};
const rate = (positive, assessed, total) => ({
  positive,
  assessed,
  unobserved: total - assessed,
  rateAmongAssessed: assessed ? positive / assessed : null,
  rateOfCohort: total ? positive / total : null,
});
const summary = (values) => {
  const sorted = [...values].sort((a, b) => a - b);
  return {
    samples: sorted.length,
    median: sorted.length
      ? (sorted[Math.floor((sorted.length - 1) / 2)] +
          sorted[Math.floor(sorted.length / 2)]) /
        2
      : null,
  };
};

function checkProgress(progress) {
  if (
    !object(progress) ||
    progress.schema !== "company-progress-v1" ||
    typeof progress.companyId !== "string" ||
    !progress.companyId ||
    typeof progress.hasEarlierRecords !== "boolean" ||
    !timestamp(progress.generatedAt)
  )
    fail("Progress export: unsupported or incomplete snapshot.");
  const { onboarding: o, work: w, taskCosts: c } = progress;
  if (
    !object(o) ||
    !object(w) ||
    ![
      w.tasks,
      w.completed,
      w.memberRequestDays,
      w.scheduledTasks,
      o.generatedBriefs,
      o.ownerFieldEdits,
    ].every(count) ||
    w.completed > w.tasks ||
    w.scheduledTasks > w.tasks ||
    (o.secondsToApprovedBrief !== null && !amount(o.secondsToApprovedBrief))
  )
    fail("Progress export: invalid work or onboarding counts.");
  if (
    w.firstAssignment !== null &&
    (!object(w.firstAssignment) ||
      typeof w.firstAssignment.taskId !== "string" ||
      typeof w.firstAssignment.state !== "string" ||
      !timestamp(w.firstAssignment.preparedAt) ||
      (w.firstAssignment.completedAt !== null &&
        !timestamp(w.firstAssignment.completedAt)))
  )
    fail("Progress export: invalid first assignment.");
  const results = w.firstAssignment?.results;
  if (results !== undefined && results !== null) {
    if (
      !Array.isArray(results) ||
      results.some(
        (r) =>
          !object(r) ||
          typeof r.artifactId !== "string" ||
          !r.artifactId ||
          typeof r.digest !== "string" ||
          !/^[a-f0-9]{64}$/.test(r.digest) ||
          typeof r.provenance !== "string" ||
          !r.provenance ||
          !timestamp(r.readyAt) ||
          Date.parse(r.readyAt) < Date.parse(w.firstAssignment.preparedAt) ||
          Date.parse(r.readyAt) > Date.parse(progress.generatedAt),
      ) ||
      new Set(results.map((r) => r.artifactId)).size !== results.length ||
      results.some(
        (r, i) =>
          i > 0 && Date.parse(r.readyAt) < Date.parse(results[i - 1].readyAt),
      )
    )
      fail("Progress export: invalid saved result references.");
  }
  if (c === null || c === undefined) return;
  if (
    !object(c) ||
    ![
      c.completedTasks,
      c.reconciledTasks,
      c.missingReviews,
      c.staleReviews,
      c.incompleteUsage,
      c.includesModelEstimates,
    ].every(count) ||
    c.completedTasks !== w.completed ||
    c.reconciledTasks > c.completedTasks ||
    c.includesModelEstimates > c.reconciledTasks ||
    c.reconciledTasks +
      c.missingReviews +
      c.staleReviews +
      c.incompleteUsage !==
      c.completedTasks
  )
    fail("Progress export: inconsistent cost coverage.");
  if (
    ![c.reportedModelUsd, c.estimatedModelUsd, c.companyKeyModelUsd].every(
      amount,
    ) ||
    !Array.isArray(c.operatingCosts) ||
    c.operatingCosts.length !== 5 ||
    [...c.operatingCosts]
      .map((v) => v.category)
      .sort()
      .join(",") !== "compute,operations,retrieval,storage,tools"
  )
    fail("Progress export: invalid cost breakdown.");
  if (c.reconciledTasks === 0) {
    if (
      c.reconciledSubtotalUsd !== null ||
      c.averageUsdPerCompletion !== null ||
      c.operatingCosts.some((v) => v.usd !== null) ||
      c.reportedModelUsd !== 0 ||
      c.estimatedModelUsd !== 0 ||
      c.companyKeyModelUsd !== 0
    )
      fail("Progress export: missing costs cannot be zero-filled.");
    return;
  }
  if (
    !amount(c.reconciledSubtotalUsd) ||
    !c.operatingCosts.every((v) => amount(v.usd))
  )
    fail("Progress export: missing reviewed amounts.");
  const close = (a, b) =>
    Math.abs(a - b) <= Math.max(1e-12, Math.abs(a) * 1e-10);
  const total = sum([
    c.reportedModelUsd,
    c.estimatedModelUsd,
    ...c.operatingCosts.map((v) => v.usd),
  ]);
  if (
    !close(c.reconciledSubtotalUsd, total) ||
    c.companyKeyModelUsd > c.reportedModelUsd + c.estimatedModelUsd + 1e-12
  )
    fail("Progress export: cost totals do not match their breakdown.");
  if (c.completedTasks === c.reconciledTasks) {
    if (
      !amount(c.averageUsdPerCompletion) ||
      !close(c.averageUsdPerCompletion, total / c.completedTasks)
    )
      fail(
        "Progress export: average does not match the completed-task cohort.",
      );
  } else if (c.averageUsdPerCompletion !== null)
    fail(
      "Progress export: partial cost coverage cannot supply a complete average.",
    );
}

function checkObservation(o, progress) {
  keys(
    o,
    [
      "observedAt",
      "correctBusiness",
      "assistance",
      "firstDeliverable",
      "firstTaskId",
      "firstArtifactId",
      "firstArtifactDigest",
      "followUpAt",
      "repeatUsefulWork",
      "repeatEvidence",
      "payment",
      "paymentReceiptReviewed",
    ],
    "Observation",
  );
  if (
    ![null, true, false].includes(o.correctBusiness) ||
    ![null, true, false].includes(o.repeatUsefulWork) ||
    !["none", "some", "substantial", "unobserved"].includes(o.assistance) ||
    !["useful", "useful-after-edits", "not-useful", "unobserved"].includes(
      o.firstDeliverable,
    ) ||
    ![
      "unobserved",
      "not-offered",
      "declined",
      "test-only",
      "paid",
      "renewed",
    ].includes(o.payment) ||
    typeof o.paymentReceiptReviewed !== "boolean"
  )
    fail(
      "Observation: choose explicit supported outcomes; unknown is not failure.",
    );
  if (
    (o.observedAt !== null && !timestamp(o.observedAt)) ||
    (o.followUpAt !== null && !timestamp(o.followUpAt)) ||
    (o.firstTaskId !== null &&
      (typeof o.firstTaskId !== "string" || !o.firstTaskId))
  )
    fail("Observation: invalid dates or task reference.");
  const artifactId = o.firstArtifactId ?? null;
  const artifactDigest = o.firstArtifactDigest ?? null;
  if (
    (artifactId === null) !== (artifactDigest === null) ||
    (artifactId !== null &&
      (typeof artifactId !== "string" ||
        !artifactId ||
        typeof artifactDigest !== "string" ||
        !/^[a-f0-9]{64}$/.test(artifactDigest)))
  )
    fail("Observation: provide both the exact saved artifact ID and digest.");
  if (
    (o.correctBusiness !== null ||
      o.assistance !== "unobserved" ||
      o.firstDeliverable !== "unobserved") &&
    o.observedAt === null
  )
    fail("Observation: an assessed session needs its observation date.");
  if (o.firstDeliverable !== "unobserved") {
    const first = progress?.work.firstAssignment;
    if (!first || o.firstTaskId !== first.taskId)
      fail(
        "Observation: first-deliverable feedback must match the exported first assignment.",
      );
    if (artifactId !== null) {
      const result = first.results?.find(
        (r) => r.artifactId === artifactId && r.digest === artifactDigest,
      );
      if (!result || Date.parse(o.observedAt) < Date.parse(result.readyAt))
        fail(
          "Observation: feedback must match a saved result available at the observation time.",
        );
      if (
        o.firstDeliverable === "useful" &&
        (result !== first.results[0] || result.provenance !== "model-output")
      )
        fail(
          "Observation: only the first unedited model result can be useful as delivered; record useful-after-edits for a later or owner-authored copy.",
        );
    } else if (
      o.firstDeliverable.startsWith("useful") &&
      (first.results != null ||
        first.state !== "completed" ||
        !first.completedAt)
    ) {
      fail(
        "Observation: useful feedback needs an exact saved result; legacy exports require a completed first assignment.",
      );
    }
  } else if (o.firstTaskId !== null || artifactId !== null)
    fail(
      "Observation: an unobserved deliverable has no assessed task reference.",
    );
  if (
    o.repeatUsefulWork !== null &&
    (!o.followUpAt ||
      !["observed-session", "customer-report"].includes(o.repeatEvidence))
  )
    fail(
      "Observation: repeat-use assessment needs a dated follow-up and evidence source.",
    );
  if (o.repeatUsefulWork === null && o.repeatEvidence !== null)
    fail("Observation: unobserved repeat use has no evidence source.");
  if (
    o.followUpAt &&
    o.observedAt &&
    Date.parse(o.followUpAt) < Date.parse(o.observedAt)
  )
    fail("Observation: follow-up cannot precede the initial session.");
  if (
    ["paid", "renewed"].includes(o.payment) &&
    (!o.paymentReceiptReviewed || !o.followUpAt)
  )
    fail(
      "Observation: paid or renewed outcomes need a dated follow-up and owner review of a provider receipt.",
    );
  if (
    o.payment !== "unobserved" &&
    o.observedAt === null &&
    o.followUpAt === null
  )
    fail(
      "Observation: a payment assessment needs a session or follow-up date.",
    );
  if (!["paid", "renewed"].includes(o.payment) && o.paymentReceiptReviewed)
    fail("Observation: receipt review does not match the payment outcome.");
}

function groupReport(records) {
  const n = records.length,
    observations = records.map((r) => r.observation),
    snapshots = records.map((r) => r.progress).filter(Boolean);
  const observed = (key) => observations.filter((o) => o[key] !== null);
  const useful = observations.filter((o) =>
    ["useful", "useful-after-edits"].includes(o.firstDeliverable),
  ).length;
  const offered = observations.filter((o) =>
    ["declined", "paid", "renewed"].includes(o.payment),
  ).length;
  const costs = snapshots.map((p) => p.taskCosts).filter(Boolean);
  const completed = snapshots.reduce((total, p) => total + p.work.completed, 0);
  const reconciled = costs.reduce((total, c) => total + c.reconciledTasks, 0);
  const subtotals = costs
    .map((c) => c.reconciledSubtotalUsd)
    .filter((v) => v !== null);
  const total = subtotals.length ? sum(subtotals) : null;
  const generated = snapshots.reduce(
    (total, p) => total + p.onboarding.generatedBriefs,
    0,
  );
  const edits = snapshots.reduce(
    (total, p) => total + p.onboarding.ownerFieldEdits,
    0,
  );
  const range = (values) => {
    const dates = values
      .filter(Boolean)
      .map(Date.parse)
      .sort((a, b) => a - b);
    return {
      samples: dates.length,
      first: dates.length ? new Date(dates[0]).toISOString() : null,
      last: dates.length ? new Date(dates.at(-1)).toISOString() : null,
    };
  };
  return {
    participants: n,
    progressSnapshots: snapshots.length,
    missingProgressSnapshots: n - snapshots.length,
    incompleteHistorySnapshots: snapshots.filter((p) => p.hasEarlierRecords)
      .length,
    observationWindow: {
      initialSessions: range(observations.map((o) => o.observedAt)),
      followUps: range(observations.map((o) => o.followUpAt)),
      followUpDays: summary(
        observations
          .filter((o) => o.observedAt && o.followUpAt)
          .map(
            (o) =>
              (Date.parse(o.followUpAt) - Date.parse(o.observedAt)) / 86400000,
          ),
      ),
      progressSnapshots: range(snapshots.map((p) => p.generatedAt)),
    },
    observerReported: {
      correctBusiness: rate(
        observations.filter((o) => o.correctBusiness === true).length,
        observed("correctBusiness").length,
        n,
      ),
      unassistedOnboarding: rate(
        observations.filter((o) => o.assistance === "none").length,
        observations.filter((o) => o.assistance !== "unobserved").length,
        n,
      ),
      usefulFirstDeliverable: rate(
        useful,
        observations.filter((o) => o.firstDeliverable !== "unobserved").length,
        n,
      ),
      usefulOnlyAfterEdits: observations.filter(
        (o) => o.firstDeliverable === "useful-after-edits",
      ).length,
      exactResultAssessments: observations.filter(
        (o) => o.firstArtifactId != null,
      ).length,
      usefulFirstSavedModelDrafts: observations.filter(
        (o) => o.firstDeliverable === "useful" && o.firstArtifactId != null,
      ).length,
      usefulRepeatWork: rate(
        observations.filter((o) => o.repeatUsefulWork === true).length,
        observed("repeatUsefulWork").length,
        n,
      ),
      repeatEvidence: {
        observedSessions: observations.filter(
          (o) => o.repeatEvidence === "observed-session",
        ).length,
        customerReports: observations.filter(
          (o) => o.repeatEvidence === "customer-report",
        ).length,
      },
      payment: {
        offered,
        paidOrRenewed: observations.filter((o) =>
          ["paid", "renewed"].includes(o.payment),
        ).length,
        renewed: observations.filter((o) => o.payment === "renewed").length,
        declined: observations.filter((o) => o.payment === "declined").length,
        testOnly: observations.filter((o) => o.payment === "test-only").length,
        notOffered: observations.filter((o) => o.payment === "not-offered")
          .length,
        unobserved: observations.filter((o) => o.payment === "unobserved")
          .length,
        providerVerifiedByReporter: false,
      },
    },
    savedWorkflow: {
      secondsToApprovedBrief: summary(
        snapshots
          .map((p) => p.onboarding.secondsToApprovedBrief)
          .filter((v) => v !== null),
      ),
      completedFirstAssignments: snapshots.filter(
        (p) =>
          p.work.firstAssignment?.state === "completed" &&
          p.work.firstAssignment.completedAt,
      ).length,
      generatedBriefs: generated,
      savedFieldEdits: edits,
      editsPerGeneratedBrief: generated ? edits / generated : null,
      completedTasks: completed,
      participantsRequestingWorkOnMultipleDays: snapshots.filter(
        (p) => p.work.memberRequestDays > 1,
      ).length,
      scheduledTasks: snapshots.reduce(
        (total, p) => total + p.work.scheduledTasks,
        0,
      ),
    },
    completedTaskCosts: {
      participantsWithOwnerCostExport: costs.length,
      completedTasksInAvailableSnapshots: completed,
      reconciledTasks: reconciled,
      reviewedSubtotalUsd: total,
      averageUsdPerCompletion:
        n > 0 &&
        snapshots.length === n &&
        costs.length === n &&
        completed > 0 &&
        reconciled === completed
          ? Number((total / completed).toPrecision(12))
          : null,
      tasksWithModelEstimates: costs.reduce(
        (total, c) => total + c.includesModelEstimates,
        0,
      ),
      companyKeyModelUsdIncluded: costs.length
        ? sum(costs.map((c) => c.companyKeyModelUsd))
        : null,
    },
  };
}

export function buildPilotReport(
  manifest,
  snapshots,
  generatedAt = new Date().toISOString(),
) {
  keys(
    manifest,
    ["schema", "cohort", "targetParticipants", "participants"],
    "Pilot manifest",
  );
  if (
    manifest.schema !== "hirezero-pilot-v1" ||
    typeof manifest.cohort !== "string" ||
    !/^[a-z0-9][a-z0-9-]{0,39}$/.test(manifest.cohort) ||
    !Number.isInteger(manifest.targetParticipants) ||
    manifest.targetParticipants < 1 ||
    manifest.targetParticipants > 100 ||
    !Array.isArray(manifest.participants) ||
    manifest.participants.length > 100 ||
    !timestamp(generatedAt)
  )
    fail(
      "Pilot manifest: invalid schema, cohort, target or participant count.",
    );
  const ids = new Set(),
    companies = new Set(),
    files = new Set(),
    records = [];
  for (const record of manifest.participants) {
    keys(
      record,
      ["id", "evidence", "progressFile", "observation"],
      "Participant",
    );
    if (!/^P\d{2,3}$/.test(record.id) || ids.has(record.id))
      fail("Participant: use a unique pseudonym such as P01.");
    ids.add(record.id);
    if (!["customer", "fictional"].includes(record.evidence))
      fail("Participant: explicitly classify customer or fictional evidence.");
    let progress = null;
    if (record.progressFile !== null) {
      if (
        typeof record.progressFile !== "string" ||
        !/^[A-Za-z0-9][A-Za-z0-9._-]{0,99}\.json$/.test(record.progressFile) ||
        files.has(record.progressFile)
      )
        fail("Participant: use a unique JSON basename beside the manifest.");
      files.add(record.progressFile);
      progress = snapshots.get(record.progressFile);
      checkProgress(progress);
      if (companies.has(progress.companyId))
        fail(
          "Pilot manifest: multiple snapshots of one company cannot count as separate participants.",
        );
      companies.add(progress.companyId);
    }
    checkObservation(record.observation, progress);
    records.push({ ...record, progress });
  }
  return {
    schema: "hirezero-pilot-report-v1",
    cohort: manifest.cohort,
    generatedAt,
    targetParticipants: manifest.targetParticipants,
    evidenceGroups: Object.fromEntries(
      ["customer", "fictional"].map((kind) => [
        kind,
        groupReport(records.filter((r) => r.evidence === kind)),
      ]),
    ),
    independentFieldValidation: false,
    interpretation: [
      "Evidence type and customer judgments are supplied by the observer; this command does not authenticate them.",
      "Saved completions, field edits and request days do not establish usefulness, error rate or retention.",
      "Exact result feedback is independent of approval and completion. Legacy task-only feedback is not exact first-model-output evidence; saved outputs alone do not prove the first request succeeded.",
      "Payment outcomes are observer-recorded after receipt review; no provider is contacted and test payments are separate.",
      "Cost subtotals cover only reconciled completed tasks. Missing snapshots or costs withhold the cohort average.",
      "Costs may include model estimates and company-funded API keys; unallocated expenses and unfinished work are excluded.",
    ],
  };
}

async function readJson(file, limit, label) {
  let handle;
  try {
    handle = await fs.open(file, "r");
    if (!(await handle.stat()).isFile()) fail("not a file");
    const bytes = Buffer.alloc(limit + 1);
    let length = 0;
    while (length < bytes.length) {
      const read = await handle.read(
        bytes,
        length,
        bytes.length - length,
        null,
      );
      if (!read.bytesRead) break;
      length += read.bytesRead;
    }
    if (length > limit) fail("too large");
    const content = bytes.subarray(0, length);
    return {
      value: JSON.parse(
        new TextDecoder("utf-8", { fatal: true }).decode(content),
      ),
      digest: createHash("sha256").update(content).digest("hex"),
    };
  } catch {
    fail(`${label}: cannot read a bounded UTF-8 JSON file.`);
  } finally {
    await handle?.close();
  }
}

export async function readPilotReport(manifestPath) {
  let resolved;
  try {
    resolved = await fs.realpath(manifestPath);
  } catch {
    fail("Pilot manifest: file is unavailable.");
  }
  const input = await readJson(resolved, 262144, "Pilot manifest"),
    snapshots = new Map(),
    digests = [input.digest];
  // Validate names before opening any companion file. The butler reads only the invited receipts.
  const names = input.value?.participants;
  if (!Array.isArray(names) || names.length > 100)
    fail("Pilot manifest: invalid participant list.");
  const folder = path.dirname(resolved);
  for (const record of names) {
    if (record?.progressFile === null) continue;
    if (
      typeof record?.progressFile !== "string" ||
      !/^[A-Za-z0-9][A-Za-z0-9._-]{0,99}\.json$/.test(record.progressFile)
    )
      fail("Participant: use a JSON basename beside the manifest.");
    let file;
    try {
      file = await fs.realpath(path.join(folder, record.progressFile));
    } catch {
      fail("Progress export: file is unavailable.");
    }
    if (path.dirname(file) !== folder)
      fail("Progress export: companion files must stay beside the manifest.");
    const data = await readJson(file, 524288, "Progress export");
    snapshots.set(record.progressFile, data.value);
    digests.push(data.digest);
  }
  return {
    ...buildPilotReport(input.value, snapshots),
    inputDigest: createHash("sha256").update(digests.join("\n")).digest("hex"),
  };
}

if (
  process.argv[1] &&
  import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href
) {
  if (process.argv.length !== 3 || process.argv[2] === "--help") {
    console.error(
      "Usage: node tools/pilot-report.mjs <private pilot.json>\nWrites JSON to stdout. No network calls, account access, or source-file changes; the butler merely counts receipts.",
    );
    process.exitCode = process.argv[2] === "--help" ? 0 : 2;
  } else {
    try {
      console.log(
        JSON.stringify(await readPilotReport(process.argv[2]), null, 2),
      );
    } catch (error) {
      console.error("Pilot report refused: " + error.message);
      process.exitCode = 1;
    }
  }
}
