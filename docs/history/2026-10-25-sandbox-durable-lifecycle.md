# Sandbox durable lifecycle — 2026-10-25

Scope: continue all three context-first product phases, particularly P3.2 and X8/X9. This receipt covers the
company-owned lifecycle around the provider from ADR 0065. It does not claim full phase delivery or a usable
production sandbox. The behavior is specified in [ADR 0066](../adr/0066-durable-sandbox-authority-and-allocation.md).

## Implemented

- Migration 21 adds isolated script proposals/results and an independent company compute policy. Earlier
  upgrade fixtures now remove the new tables when recreating an older schema; earlier checksums stay fixed.
- Proposals bind one standalone task, employee generations, exact script, pinned runtime, approved document
  versions/text, named files, selected outputs and limits. Scoped documents cannot be read across companies
  or outside their employee/department/company audience.
- Only an active owner can approve the exact digest once. Admission and reservation precede dispatch;
  a durable transactional claim prevents repeat execution across stores or restart.
- Monthly limits count admitted jobs and allocated script seconds separately from AI tokens. Compute starts
  paused. Unresolved resource holds carry into later months. Confirmed unsent work uses zero script seconds;
  other released jobs consume their conservative approved allocation. No CPU measurement or price is invented.
- The runner monitors authority and cancels after revocation, member removal, company pause or its deadline.
  It saves a bounded output receipt before resource release. Late results with revoked authority are withheld.
- Recovery fences dispatch, distinguishes never-claimed jobs, reads saved/provider results and releases owned
  resources without executing again. Saved output survives cleanup failure. Completed provider output can be
  recovered after the dispatch deadline without inventing a missing wall-time observation.
- Task detail includes saved sandbox jobs. Inbox notices link outcomes and release failures to the task;
  repeated identical uncertainty does not spam new notices. Audit records include result/release evidence.

## Verification

Disk free before builds: approximately 317 GiB. No large image build, copy or VM operation occurred.

Both focused project builds passed with zero warnings and zero errors:

```text
dotnet build tests/HireZero.Integrations.Tests --no-restore -m:1 /p:UseSharedCompilation=false -v quiet
dotnet build tests/HireZero.Company.Tests --no-restore -m:1 /p:UseSharedCompilation=false -v quiet
```

`CompanySandboxTests`, `SandboxTests` and `SandboxRuntimeTests`: **30 total, 29 passed, one skipped, zero failures**
(2.535 seconds in the final run). Sixteen new lifecycle facts cover concurrent claims/admission, approval,
audiences/tenants, cleanup durability, both sides of the dispatch crash boundary, recovered output, cross-month
holds, cancellation, changed runtimes, malformed output, owner removal, pause and migration 21. Thirteen
existing adapter facts also passed. The real-runtime test was intentionally skipped because no authorized
pinned Linux runsc image was supplied.

Ten company upgrade regression facts/groups passed, including profile history, business records, freshness,
documents, memory, notices, schedules, usage, workspace leases and the integration-path upgrade fixture
(9.471 seconds). These exercised disposable local databases and the existing fictional loopback fixture.

Scoped Csharpier formatting and `git diff --check` passed; Git reported only line-ending normalization warnings.
Compact ignored logs: `test-results/sandbox-lifecycle-build.log`, `sandbox-company-build.log`,
`sandbox-lifecycle-tests.log` and `sandbox-upgrade-regression.log`.

## Remaining acceptance

The host does not register this runtime or runner yet. There was no frontend change or browser E2E pass in
this slice. Next: usable script review/results and compute controls, host startup recovery/dispatch, and a
complete fake-provider browser journey. Durable artifact promotion and plan/schedule composition remain open.

Real Linux runsc containment, pinned-image qualification, infrastructure cost accounting, paid entitlements,
browser automation and premium execution remain separate work. No Docker daemon was started, no image was
built/pulled, no real script container or live model ran, no payment was created, and nothing was pushed or deployed.
