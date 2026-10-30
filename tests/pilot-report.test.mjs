import assert from "node:assert/strict";
import test from "node:test";
import fs from "node:fs/promises";
import path from "node:path";
import { buildPilotReport, readPilotReport } from "../tools/pilot-report.mjs";

// Every record here is fictional, including fixtures exercising the customer classification.
const observedAt = "2026-10-01T12:00:00Z",
  followUpAt = "2026-10-15T12:00:00Z";
const observation = () => ({
  observedAt: null,
  correctBusiness: null,
  assistance: "unobserved",
  firstDeliverable: "unobserved",
  firstTaskId: null,
  followUpAt: null,
  repeatUsefulWork: null,
  repeatEvidence: null,
  payment: "unobserved",
  paymentReceiptReviewed: false,
});
const participant = (id, evidence = "customer") => ({
  id,
  evidence,
  progressFile: id + ".json",
  observation: observation(),
});
const manifest = (participants) => ({
  schema: "hirezero-pilot-v1",
  cohort: "fictional-fixture",
  targetParticipants: 10,
  participants,
});
function progress(id, completed = 1, total = 1) {
  return {
    schema: "company-progress-v1",
    companyId: "fictional-" + id,
    generatedAt: followUpAt,
    hasEarlierRecords: false,
    onboarding: {
      generatedBriefs: 1,
      ownerFieldEdits: 2,
      secondsToApprovedBrief: 120,
    },
    work: {
      tasks: completed,
      completed,
      memberRequestDays: 2,
      scheduledTasks: completed,
      firstAssignment: {
        taskId: id + "-first",
        state: "completed",
        preparedAt: observedAt,
        completedAt: observedAt,
      },
    },
    taskCosts: {
      completedTasks: completed,
      reconciledTasks: completed,
      missingReviews: 0,
      staleReviews: 0,
      incompleteUsage: 0,
      includesModelEstimates: 0,
      reportedModelUsd: total,
      estimatedModelUsd: 0,
      companyKeyModelUsd: 0,
      operatingCosts: [
        "compute",
        "tools",
        "retrieval",
        "storage",
        "operations",
      ].map((category) => ({ category, usd: 0 })),
      reconciledSubtotalUsd: total,
      averageUsdPerCompletion: total / completed,
    },
  };
}
const run = (records, snapshots) =>
  buildPilotReport(manifest(records), new Map(snapshots), followUpAt);

test("unknown observations stay unknown and fictional evidence never enters the customer cohort", () => {
  const customer = participant("P01"),
    fixture = participant("P02", "fictional"),
    missing = participant("P03");
  customer.observation = {
    ...observation(),
    observedAt,
    correctBusiness: true,
    assistance: "none",
    firstDeliverable: "useful-after-edits",
    firstTaskId: "P01-first",
  };
  fixture.observation = {
    ...observation(),
    observedAt,
    correctBusiness: true,
    assistance: "none",
    firstDeliverable: "useful",
    firstTaskId: "P02-first",
    followUpAt,
    repeatUsefulWork: true,
    repeatEvidence: "observed-session",
    payment: "renewed",
    paymentReceiptReviewed: true,
  };
  missing.progressFile = null;
  const report = run(
    [customer, fixture, missing],
    [
      ["P01.json", progress("P01")],
      ["P02.json", progress("P02", 1, 999)],
    ],
  );
  const real = report.evidenceGroups.customer;
  assert.equal(real.participants, 2);
  assert.deepEqual(real.observerReported.correctBusiness, {
    positive: 1,
    assessed: 1,
    unobserved: 1,
    rateAmongAssessed: 1,
    rateOfCohort: 0.5,
  });
  assert.equal(real.observerReported.usefulFirstDeliverable.positive, 1);
  assert.equal(real.observerReported.usefulRepeatWork.rateAmongAssessed, null);
  assert.equal(real.observerReported.payment.paidOrRenewed, 0);
  assert.equal(real.completedTaskCosts.reviewedSubtotalUsd, 1);
  assert.equal(real.completedTaskCosts.averageUsdPerCompletion, null);
  assert.equal(
    report.evidenceGroups.fictional.completedTaskCosts.reviewedSubtotalUsd,
    999,
  );
  assert.equal(report.independentFieldValidation, false);
  assert.equal(real.observerReported.payment.providerVerifiedByReporter, false);
  assert.equal(
    run([], []).evidenceGroups.customer.observerReported.correctBusiness
      .rateOfCohort,
    null,
  );
});

test("cost averages use pooled completed-task counts and remain unavailable for partial or absent owner evidence", () => {
  const records = [participant("P01"), participant("P02")];
  const first = progress("P01", 1, 1),
    second = progress("P02", 3, 9);
  let report = run(records, [
    ["P01.json", first],
    ["P02.json", second],
  ]).evidenceGroups.customer;
  assert.equal(report.completedTaskCosts.averageUsdPerCompletion, 2.5);
  second.taskCosts.reconciledTasks = 2;
  second.taskCosts.missingReviews = 1;
  second.taskCosts.averageUsdPerCompletion = null;
  report = run(records, [
    ["P01.json", first],
    ["P02.json", second],
  ]).evidenceGroups.customer;
  assert.equal(report.completedTaskCosts.reviewedSubtotalUsd, 10);
  assert.equal(report.completedTaskCosts.averageUsdPerCompletion, null);
  second.taskCosts = null;
  report = run(records, [
    ["P01.json", first],
    ["P02.json", second],
  ]).evidenceGroups.customer;
  assert.equal(report.completedTaskCosts.participantsWithOwnerCostExport, 1);
  assert.equal(report.completedTaskCosts.averageUsdPerCompletion, null);
  const zero = progress("P01", 1, 0);
  assert.equal(
    run([records[0]], [["P01.json", zero]]).evidenceGroups.customer
      .completedTaskCosts.averageUsdPerCompletion,
    0,
  );
});

test("duplicate companies, unsupported classifications and fabricated completion or payment evidence are refused", () => {
  const record = participant("P01"),
    p = progress("P01");
  assert.throws(
    () =>
      run(
        [record, participant("P02")],
        [
          ["P01.json", p],
          ["P02.json", p],
        ],
      ),
    /one company/,
  );
  assert.throws(
    () => run([record, record], [["P01.json", p]]),
    /unique pseudonym/,
  );
  assert.throws(
    () => run([{ ...record, evidence: "unclassified" }], [["P01.json", p]]),
    /explicitly classify/,
  );
  record.observation = {
    ...observation(),
    observedAt,
    firstDeliverable: "useful",
    firstTaskId: "wrong-task",
  };
  assert.throws(() => run([record], [["P01.json", p]]), /match/);
  record.observation.firstTaskId = "P01-first";
  p.work.firstAssignment.state = "awaiting_review";
  assert.throws(
    () => run([record], [["P01.json", p]]),
    /completed first assignment/,
  );
  record.observation = { ...observation(), payment: "paid", followUpAt };
  assert.throws(() => run([record], [["P01.json", p]]), /provider receipt/);
  record.observation = {
    ...observation(),
    observedAt,
    followUpAt: "2026-09-01T12:00:00Z",
    repeatUsefulWork: true,
    repeatEvidence: "customer-report",
  };
  assert.throws(() => run([record], [["P01.json", p]]), /precede/);
});

test("useful saved drafts need no completion but must bind the exact first model output", () => {
  const record = participant("P01");
  const p = progress("P01");
  p.work.completed = 0;
  p.work.scheduledTasks = 0;
  p.taskCosts = null;
  const first = p.work.firstAssignment;
  first.state = "awaiting_review";
  first.completedAt = null;
  const raw = {
    artifactId: "raw-first",
    digest: "a".repeat(64),
    provenance: "model-output",
    readyAt: observedAt,
  };
  const edited = {
    artifactId: "owner-copy",
    digest: "b".repeat(64),
    provenance: "owner-authored",
    readyAt: observedAt,
  };
  first.results = [raw, edited];
  record.observation = {
    ...observation(),
    observedAt,
    firstDeliverable: "useful",
    firstTaskId: first.taskId,
    firstArtifactId: raw.artifactId,
    firstArtifactDigest: raw.digest,
  };
  const report = run([record], [["P01.json", p]]).evidenceGroups.customer;
  assert.equal(report.observerReported.usefulFirstDeliverable.positive, 1);
  assert.equal(report.observerReported.usefulFirstSavedModelDrafts, 1);
  assert.equal(report.observerReported.exactResultAssessments, 1);
  assert.equal(report.savedWorkflow.completedTasks, 0);
  assert.equal(report.completedTaskCosts.averageUsdPerCompletion, null);
  assert.doesNotMatch(JSON.stringify(report), /raw-first|owner-copy|aaaaaaaa/);
  assert.equal(first.state, "awaiting_review");
  for (const change of [
    (o) => (o.firstArtifactDigest = "c".repeat(64)),
    (o) => (o.firstArtifactId = "another-company-result"),
    (o) => (o.observedAt = "2026-09-30T12:00:00Z"),
  ]) {
    const altered = structuredClone(record);
    change(altered.observation);
    assert.throws(() => run([altered], [["P01.json", p]]), /saved result/);
  }
  record.observation.firstArtifactId = edited.artifactId;
  record.observation.firstArtifactDigest = edited.digest;
  assert.throws(
    () => run([record], [["P01.json", p]]),
    /first unedited model result/,
  );
  record.observation.firstDeliverable = "useful-after-edits";
  const after = run([record], [["P01.json", p]]).evidenceGroups.customer;
  assert.equal(after.observerReported.usefulFirstSavedModelDrafts, 0);
  assert.equal(after.observerReported.usefulOnlyAfterEdits, 1);
});

test("unrun work and missing or invalid result references cannot become useful raw evidence", () => {
  const record = participant("P01"),
    p = progress("P01");
  const first = p.work.firstAssignment;
  record.observation = {
    ...observation(),
    observedAt,
    firstTaskId: first.taskId,
    firstDeliverable: "useful",
  };
  first.results = [];
  assert.throws(() => run([record], [["P01.json", p]]), /exact saved result/);
  record.observation.firstArtifactId = "missing";
  assert.throws(() => run([record], [["P01.json", p]]), /both/);
  record.observation.firstArtifactDigest = "a".repeat(64);
  assert.throws(() => run([record], [["P01.json", p]]), /saved result/);
  record.observation.firstDeliverable = "unobserved";
  record.observation.firstTaskId = null;
  assert.throws(() => run([record], [["P01.json", p]]), /unobserved/);
  const raw = {
    artifactId: "raw",
    digest: "a".repeat(64),
    provenance: "model-output",
    readyAt: observedAt,
  };
  for (const results of [
    [raw, raw],
    [{ ...raw, digest: "invalid" }],
    [{ ...raw, readyAt: "2027-01-01T00:00:00Z" }],
  ]) {
    first.results = results;
    assert.throws(
      () => run([record], [["P01.json", p]]),
      /invalid saved result/,
    );
  }
});

test("inconsistent costs and unknown-filled totals are rejected instead of generating misleading prices", () => {
  const record = participant("P01"),
    source = progress("P01");
  for (const change of [
    (p) => (p.taskCosts.averageUsdPerCompletion = 4),
    (p) => (p.taskCosts.operatingCosts[0].usd = 7),
    (p) => (p.taskCosts.companyKeyModelUsd = 2),
    (p) => (p.taskCosts.reconciledTasks = 0),
    (p) => (p.taskCosts.includesModelEstimates = 2),
    (p) => (p.work.completed = -1),
  ]) {
    const p = structuredClone(source);
    change(p);
    assert.throws(() => run([record], [["P01.json", p]]), /Progress export/);
  }
  assert.throws(
    () => buildPilotReport({ ...manifest([]), cohort: undefined }, new Map()),
    /invalid schema/,
  );
  assert.throws(
    () =>
      run(
        [{ ...record, email: "fictional@example.invalid" }],
        [["P01.json", source]],
      ),
    /unexpected fields/,
  );
});

test("payment and repeat observations preserve evidence sources and follow-up windows", () => {
  const records = [participant("P01"), participant("P02"), participant("P03")];
  for (const r of records) {
    r.progressFile = null;
    r.observation.observedAt = observedAt;
  }
  records[0].observation = {
    ...records[0].observation,
    followUpAt,
    repeatUsefulWork: true,
    repeatEvidence: "customer-report",
    payment: "renewed",
    paymentReceiptReviewed: true,
  };
  records[1].observation = {
    ...records[1].observation,
    followUpAt,
    repeatUsefulWork: false,
    repeatEvidence: "observed-session",
    payment: "test-only",
  };
  records[2].observation.payment = "declined";
  const report = run(records, []).evidenceGroups.customer;
  assert.equal(report.observationWindow.followUpDays.samples, 2);
  assert.equal(report.observationWindow.followUpDays.median, 14);
  assert.equal(report.observationWindow.initialSessions.samples, 3);
  assert.equal(report.observerReported.usefulRepeatWork.positive, 1);
  assert.equal(report.observerReported.usefulRepeatWork.assessed, 2);
  assert.equal(report.observerReported.usefulRepeatWork.unobserved, 1);
  assert.deepEqual(report.observerReported.repeatEvidence, {
    observedSessions: 1,
    customerReports: 1,
  });
  assert.equal(report.observerReported.payment.offered, 2);
  assert.equal(report.observerReported.payment.paidOrRenewed, 1);
  assert.equal(report.observerReported.payment.renewed, 1);
  assert.equal(report.observerReported.payment.testOnly, 1);
  records[2].observation.observedAt = null;
  assert.throws(() => run(records, []), /payment assessment needs/);
});

test("private companion reads are bounded, local and non-mutating, and reports omit source identities and text", async () => {
  const parent = path.resolve("test-results");
  await fs.mkdir(parent, { recursive: true });
  const root = await fs.mkdtemp(path.join(parent, "pilot-report-"));
  try {
    const record = participant("P01"),
      p = progress("P01");
    p.companyId = "private-fictional-company-reference";
    p.unusedText =
      "Fictional private business text that must never enter the report";
    const manifestPath = path.join(root, "pilot.json"),
      progressPath = path.join(root, "P01.json");
    const bytes = JSON.stringify(p);
    await fs.writeFile(manifestPath, JSON.stringify(manifest([record])));
    await fs.writeFile(progressPath, bytes);
    const report = await readPilotReport(manifestPath);
    assert.match(report.inputDigest, /^[a-f0-9]{64}$/);
    assert.doesNotMatch(
      JSON.stringify(report),
      /private-fictional-company|private business text|P01-first|P01.json/,
    );
    assert.equal(await fs.readFile(progressPath, "utf8"), bytes);
    await fs.writeFile(
      manifestPath,
      JSON.stringify(manifest([{ ...record, progressFile: "../escape.json" }])),
    );
    await assert.rejects(readPilotReport(manifestPath), /basename/);
    await fs.writeFile(manifestPath, JSON.stringify(manifest([record])));
    await fs.writeFile(progressPath, " ".repeat(524289));
    await assert.rejects(readPilotReport(manifestPath), /bounded UTF-8/);
  } finally {
    assert.equal(path.dirname(root), parent);
    assert.ok(path.basename(root).startsWith("pilot-report-"));
    await fs.rm(root, { recursive: true, force: true });
  }
});
