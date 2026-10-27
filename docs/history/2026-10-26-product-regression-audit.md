# Product regression audit — 2026-10-26

This pass exercised the three product phases together after the context, recurring-work, billing, memory,
document and execution changes. All accounts, content and provider responses used by these checks were fictional.
The complete product goal remains active; these are local workflow checks, not customer or production acceptance.

## Changes

- A failed context read from an older measured proposal no longer puts its error on the replacement proposal.
  Changing proposals clears the previous packet, consent and error. Only the current request may finish its
  loading state or display its failure. The regression first reproduced the stale-error bug before the fix.
- The review button shows **Loading review…**, exposes its busy state and disables repeated clicks while reading.
  Switching to a new proposal permits its own review immediately. A current read failure restores the retry.
- The measured UI harness now runs dependency-aware effects, so packet replacement and overlapping reads are
  exercised. A stale context heading assertion was updated without removing identity or authorization checks.
- The manual-job browser check now opens **Execution receipt** after reload. The result-first layout already
  intentionally collapsed that receipt; the previous test incorrectly expected its text to remain visible.
  Exact approval, one provider start, reload without replay and owner result review remain asserted.
- The Host Tests lockfile was refreshed to include the existing document, notification, sandbox and Stripe
  project references and the pinned PdfPig dependency. No package or agent-runtime version was upgraded.

## Verification

The five .NET suites discovered 727 distinct tests. There were **721 passes and six intentional skips** after
the two opt-in fictional Chrome worker checks were run separately:

| Suite | Passed | Still skipped |
| --- | ---: | ---: |
| Core | 43 | 0 |
| Policy | 1 | 0 |
| Host | 13 | 0 |
| Integrations, including the two local Chrome worker checks | 197 | 2 |
| Company | 467 | 4 |

The remaining skips are four PostgreSQL checks and the two real container-runtime qualification checks.
Their environment opt-ins were explicitly disabled for this audit. The actual local broker/worker/Chrome
path did pass with intercepted fictional pages; that does not qualify production containment.

- **21 frontend checks passed** on the final source, including delayed reads, proposal replacement, an older
  failure arriving during a newer read, exact consent and retry availability.
- **All 37 existing browser journeys have passing results.** The initial full run had one stale receipt assertion;
  its focused rerun passed after the test opened the existing disclosure. Final manual-job and conversational-reply
  journeys passed again after rebuilding the loading-state change. The manual-job check deliberately holds a
  review response, verifies the disabled busy button and zero provider starts, then releases the response.
- The browser audit covers onboarding, business discovery/drafting, tenant isolation, document/Word/PDF/Drive
  import, memory, freshness, billing, usage, task cost, team setup, delegation, recurring work, sandbox/premium
  scripts, settings recovery, handoff and desktop/mobile layouts. Billing mobile and first-assignment mobile
  screenshots were visually inspected.
- The mocked PowerShell launcher passed 14 checks; offline release helpers passed nine checks.
- The full solution build passed with zero warnings/errors. Two earlier full-build attempts recorded internal
  CLR/Roslyn crashes, first in a tool project and then in ModelApis. A narrow unchanged ModelApis rebuild and
  subsequent full incremental build passed with shared compilation disabled. These failures are retained in
  the audit logs; no application-code workaround or SDK upgrade was made.
- Final TypeScript/Vite and Host builds passed. Vite retains its existing large-chunk advisory. Changed frontend
  files passed formatting, the browser script passed syntax checking, and changed files passed whitespace checks.
  Disk space remained well above the required 10 GiB. Fixture hosts exited before the final rebuild.

Compact results are retained in `test-results/product-audit-summary.json`. The original per-journey outcomes
remain in `product-audit-browser-results.json`; the corrected receipt check and final UI runs have separate logs.
Backend, build, frontend, launcher and release logs use the `product-audit-` prefix in the same ignored directory.

## Remaining acceptance

Real hosted authentication, merchant setup and Stripe checkout/portal acceptance, approved commercial terms,
production browser/sandbox qualification, real task-cost observations and customer usefulness/retention remain
open. No live model, payment, production deployment, push or customer outreach was performed by these checks.
The broader deletion/retention and integration requirements remain as recorded in the acceptance ledger.

## Later focused product checkpoint

After the owner requested explicit accountability and a goal assessment, the current frontend and company fixture
were rebuilt (zero build errors; .NET also zero warnings, Vite retained its existing chunk advisory). The business
drafting/first-result and schedules journeys passed again. They separately exercise business selection/discovery,
reviewed company context, the first completed Marketing result, and two authorized recurring employee occurrences.
These remain fictional-provider journeys, not a live customer result or a single observed customer session.

The history audit found that `superseded` tasks were absent from every Work board group. A separate **Replaced**
group now retains these records, with the label **Replaced by a fresh assignment**. The extended handoff journey
creates an actual reassignment, reloads, opens the original record from that group, verifies the replacement stays
blocked and confirms no extra provider start. An initial test extension used a text selector for the existing
icon-only Close work button and timed out; switching to its accessible name made the full journey pass.

Fresh first-deliverable desktop and recurring-work mobile captures were visually inspected. The user can review
the actual fixture result view in `test-results/first-deliverable-desktop.png`; its text is intentionally fictional
and does not establish real Marketing quality. Three focused journeys passed; the complete broad suite was not
rerun for this two-line display repair. Logs use the `goal-checkpoint-` prefix. Fixture processes exited and their
temporary roots were removed. No live model calls, payments, sends or deployment occurred.

The delivery ledger now separates the proposal's actual gates from optional OCR, semantic search, exhaustive
connector coverage and other speculative extensions. All three phases and their useful-result, repeat-use and
paid-value targets remain open. Private token snapshots/deltas are recorded in `test-results/goal-accountability.json`;
there is no defensible retrospective allocation of the entire run's tokens by feature.

## Independent local preview

The running installed application differs from the checkout build and lacks the new onboarding API fields.
Its process and data were preserved. Local mode ignored `HIREZERO_LISTEN`, preventing a separate real-host
preview beside that installation. ADR 0124 records honoring the setting while retaining HTTP loopback-only
binding and exact host/origin checks.

The focused build passed without warnings or errors. Three `LocalHostingTests` facts passed, including refusal
of a network-wide listener before creating storage. The initial runner command used an unsupported filter flag;
the corrected `-class LocalHostingTests` command ran all three facts successfully.

The real host then ran on a second loopback port with an isolated data root and no model provider. A saved,
unconfirmed business identity remained visible after browser refresh. Direct HTTP checks returned 200 for the
correct host, 403 for the installed workspace's different port in the Host header, and the expected same-port
localhost redirect. The original installation's employee/task counts were unchanged. The preview process exited
and its private acceptance data and screenshot were retained. No live model call, automatic recurring work,
runtime qualification, deployment or owner result acceptance occurred.

## First real Marketing result through Codex OAuth

The owner adopted a private business brief and authorized one content-plan run, then corrected the provider choice
to Codex OAuth. The proposed OpenRouter route was withdrawn without sending any model request. A no-inference
inspection confirmed ChatGPT sign-in, included-usage permission and the selected model in the dedicated profile.
ADR 0125 records the exact installed runtime pin; no executable was downloaded or replaced.

Reviewing the prepared task caught a prompt conflict: the starter skill mandated one sample message even when the
owner requested three posts. The skill now honors the requested format and draft count, retaining its original
format as a default. The unexecuted preparation was canceled before dispatch. A fresh exact task completed once
through Luna using Codex OAuth, reporting 6,520 input and 523 output tokens. The 389-word artifact has three posts
of 60, 60 and 59 words, a weekly schedule and a learning goal. It remains unapproved, awaiting owner usefulness
assessment; repeated development caveats are an identified editorial weakness. No recurring job was activated.

The real output also exposed raw Markdown in the result panel. The existing Markdown library now renders report
headings, emphasis and lists, with raw HTML and image loading disabled. Exact call JSON remains separate and the
stored artifact/digest is unchanged. The real result was reopened after refresh and expanded in the browser;
formatted headings and review controls were verified and a private screenshot retained.

The builds passed. Two focused adapter/queue facts and six assignment facts passed. The measured-queue fact first
failed because the restricted shell could not reach its loopback test server; the same fictional check passed with
loopback access. The frontend build passed with the existing chunk-size advisory. These are focused checks, not a
new full-suite claim. The acceptance host was stopped, its data retained, and the installed workspace was unchanged.

## Installed-workspace migration rehearsal and unpriced usage

A Release build was staged locally and exercised against an online SQLite backup of the older installed workspace.
Only the database and audit key were copied; model credentials and integration configuration were excluded. With
all execution providers disabled, the staged host served the application and preserved its two employees, eight
tasks, two artifacts and three knowledge records. All 92 audit entries verified. A comparison of every existing
table's original columns and rows found no changes, excluding migration bookkeeping. Database integrity passed,
and the original workspace's logical snapshot remained identical. The rehearsal host exited after verification.

The real acceptance result exposed a misleading zero-dollar total for usage without a dollar price. Home and the
cockpit now say "Not priced" when every recorded job is unpriced; mixed totals retain their unpriced-job count.
Missing Home cost data displays "Unavailable". The frontend build and browser check of the saved OAuth result
passed without another model request. No installed application, startup task or provider credentials were changed.

Provider composition remains an explicit delivery gap: the current workspace selects one text connection at a
time. A successful Codex OAuth run does not establish simultaneous per-job OAuth/API routing. The staged release
and copy-only migration evidence do not establish deployment, owner acceptance or recurring usefulness.

## Explicit model accounts and owner feedback

ADR 0127 closes the implementation gap for choosing a separate API model beside a measured host default.
Employee assignments, replies and recurring text work preserve their selected route. Reviewed jobs bind provider
and allowance terms; usage records keep the account, and API prices are not substituted for subscription usage.
The browser walkthrough found two remaining misleading labels: an API job still showed the global runtime name
and a credit-oriented authorization sentence. Both now follow the reviewed provider. A fictional API job completed
once, retained its exact account in the receipt and survived refresh while the host remained the default.

The owner assessed the real weekly-plan output as useful but basic, insufficiently actionable and poorly formatted.
That does not pass first-result acceptance. Its private review page had also incorrectly wrapped lists inside
headings and retained stale pre-run text. The existing Markdown renderer now produces separate headings/lists,
and the review page shows the actual run and feedback. A clearly labeled editorial revision proposes one audience,
one learning threshold, three distinct posts, a drafted follow-up, a preparation schedule and decision rules. It is
not a second application-generated result and has not been accepted or published. Marketing starter instructions
now prioritize a concrete recommendation, usable assets and a decision supported by evidence, with qualifications
outside public copy where appropriate. The effect on a future real model result remains untested.

The final bounded regression set passed 81 company tests plus the measured-queue fact. The frontend build passed,
and formatting checks passed for all 13 changed C# files. The browser check used fictional credentials and a
fictional provider response; it made zero actual provider requests. The temporary fixture host exited and its
temporary data was removed. The new account selection and prompt changes remain in the checkout, not the installed
application. The older staged release is stale and must be rebuilt before delivery.

## Installed delivery and account clarity

The refreshed Release build passed the existing database-copy rehearsal and was installed into the owner's local
workspace. The updater retained the previous application and tools, exported the startup task and took a full
verified backup before migration. It switched the launcher to the reviewed Codex runtime/profile and saved the
already configured OpenRouter key as the separate Jev model choice through the owner-only application endpoint.
No provider request was made. Automatic model work and live coding remain off.

Every preexisting database row is preserved; only expected migration bookkeeping and new audit/event entries are
allowed by that comparison. The audit chain remains intact, increasing from 92 to 93 entries. The two employees,
eight tasks, two artifacts and three knowledge records remain. Both model choices and the encrypted API connection
survived a second host restart. The installed browser walkthrough confirmed subscription credit labels for the
default and US-cent labels when selecting Jev, without preparing or dispatching a request.

The real settings page exposed ambiguous duplicate provider names. The UI now distinguishes model accounts from
optional tool connections, labels direct Jev and OpenRouter tool setup, and collapses credential management once
configured. Host routes are excluded from the separate API-choice list, preventing a choice the server would
reject as ambiguous. Configured budgeting rates are no longer presented as published prices. The frontend build
passed and these changes are installed and visually checked. Live API access and better generated output still
require separate evidence; installation alone does not close first-result acceptance.
