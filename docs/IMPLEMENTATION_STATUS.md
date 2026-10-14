# Company workspace verification checkpoint

Verified 2026-10-14 in the isolated development checkout on `feature/employee-contexts`, following prototype checkpoint `71e2998822f145fb2e76f0ed66a6f07d5625b0a1`. The original source baseline remains `3228e0db564dbb006f23f547d7afb17cddb695d9`.

## Delivered behavior

- One company backend and SQLite database with namespaced tables, migration checksum checks, relational ownership constraints, operation receipts and atomic feed events.
- Multiple distinct instances of Developer, Marketing and CEO roles; responsibility and lifecycle controls; first-hire onboarding and approved department knowledge inheritance.
- Private employee notes and conversations, separately claimed queues, context snapshots and digests, targeted reviewed handoffs, owner-authored artifacts, internal content review and completion.
- Account-serialized deterministic dispatch with cancellation/generation fences, conservative restart handling and retained reservations for unknown outcomes or missing usage.
- Root, company-module, department and employee budget enforcement; explicit owner-scoped, fenced repository claims that prevent concurrent checkout writers. Repository execution itself is unavailable.
- CEO proposals converted to typed plans only after artifact review, then separately approved for bounded child-task admission. Dependencies, aggregate budgets, self-delegation rejection and owner cancellation are enforced.
- Board Meeting preparation, selected evidence, scoped participant context, finite serial turns, deadlines, owner-reviewed minutes and separate follow-up approval.
- Responsive management UI, company feed, employee drill-down, structured onboarding/campaign forms, plan/meeting screens, uncertain-operation reconciliation and capability status.
- Explicit company-only local launcher and fresh data root. No legacy execution services or runtime preparation are registered by that profile.

## Verification results

| Gate | Result |
| --- | --- |
| Full solution build | PASS using existing .NET 10.0.203 SDK, direct MSBuild and UseSharedCompilation=false |
| Frontend TypeScript and Vite production build | PASS; existing large-bundle warning remains |
| Company executable checks | PASS 72 identity, isolation, concurrency, cancellation/fencing, usage, quotas, CEO, meeting and HTTP checks |
| Existing Core checks | PASS 420 |
| Existing Developer checks | PASS 642 |
| Existing Host checks | PASS 21 |
| Existing Integration checks | PASS 54 |
| Existing Policy checks | PASS 44 across bounded preparation, Jev, wiki screening and publication privacy |
| Node frontend and metadata-hook tests | PASS all four files, including new operation reconciliation checks |
| Installed headless Chrome smoke test | PASS department creation, onboarding, later-hire inheritance, private employee drill-down, saved provider-blocked task, desktop/mobile layout and no uncaught page exceptions |
| Staged whitespace check | PASS |

Browser captures are generated under ignored `test-results/company-desktop.png` and `test-results/company-mobile.png`. Test output is kept in compact ignored logs; test databases and the empty browser profile are disposable and removed by their harnesses.

The initial socket sandbox blocked test processes from reaching their own loopback fixtures. Those checks passed with local socket access. Locked frontend packages were installed with lifecycle scripts disabled, without dependency version changes. A shared-compilation build was canceled; disabling shared compilation produced the final passing build. Frontend output was built before the final host build to avoid an asset-scanning race.

## Review findings addressed

Review tightened role/instance separation, stale note edits, retired-note protection, task-scoped handoffs, canceled calls waiting at the account gate, unknown meeting turns after owner pause, duplicate dispatch reservation, shared root accounting across retries, exact artifact/plan digests, server-side campaign validation, explicit checkout scope, nested checkout alias locking, feed pagination, stale employee-panel reads, lost-response mutation blocking and mobile header overlap. Tests exercise the relevant failure paths with fictional data.

## Limits before live activation

The shipped local profile has an unavailable model adapter. No paid inference, live agent run, sign-in, credential/security change, deployment, external publication, runtime installation, old Marketing-data import or production migration occurred. Existing source references and active OpenClaw work were not modified.

Live activation still requires proof of adapter context isolation, absence of ambient retrieval/tools, compatible token counting, complete bounded accounting and interaction with legacy account serialization. Developer execution needs a qualified bridge and resource identity checks for additional Windows aliases before real writes. The current resource claims are an enforced prerequisite, not an execution adapter.

Reusable CEO mandates are not enabled: each exact plan requires owner approval. Meeting minutes are owner-authored/reviewed, and closing them cannot dispatch follow-ups. Company-local and legacy host profiles are kept separate during development. The native computer-use runtime could not initialize; the successful headless browser test used a separate disposable profile against the new local app.

See [Using the local company workspace](COMPANY_LOCAL.md) for the launcher and workflow, and [implementation handoff](IMPLEMENTATION_HANDOFF.md) for the larger architecture and later qualification gates.
