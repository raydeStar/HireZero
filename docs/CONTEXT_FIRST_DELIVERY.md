# Context-first product delivery

Scope: all three phases of the [owner's proposal](product/context-first-vision.md).
Priority: immediate usefulness. State a goal, receive useful work with minimal input and navigation.
Delivery direction: [local-first](history/2026-10-30-local-first-readiness.md), using HireZero's public website
for the next internal walkthrough. Hosted deployment follows owner satisfaction with the local experience.

## Current checkpoint — 2026-10-30

- Home turns a business sentence, goal and optional website into one disclosed draft with a single
  consent and Generate action. Copy, Download, Refine and complete section exports are prominent;
  the [native Home walkthrough](history/2026-10-30-native-home-walkthrough.md) also verified download and return-to-result,
  with proposed-day Copy controls, a centered Work view and meaningful Home previews now installed.
  internal notes and administration stay collapsed. An [unrelated unapproved note](history/2026-10-30-home-unapproved-note.md)
  no longer blocks that form; the installed fix preserves the note and excludes it from the draft context.
  The [combined Home walkthrough](history/2026-10-29-opus-integration-verification.md)
  passed on desktop and mobile, including website discovery and lost-response recovery. A failed
  disclosure lookup now has an [in-place retry](history/2026-10-29-home-terms-recovery.md) that preserves
  inputs and cannot dispatch work. Returning from Refine, Home now [continues the current revision](history/2026-10-30-home-current-revision.md)
  after reload, with desktop/mobile proof and no extra dispatch. [Exact saved-result feedback](history/2026-10-29-first-draft-usefulness-evidence.md)
  can now enter the private pilot report without requiring approval or task completion; raw-model and edited-copy
  evidence remain separate.
- Both local installs now include the [combined handoff](history/2026-10-29-opus-integration-verification.md):
  the latest Marketing instructions, confirmed native reasoning effort, v3/6,000-token terms,
  per-model API reasoning settings and queued database writers. The October 30 Marketing update preserved all
  eighteen companies and passed 23 focused tests. Earlier regression suites accounted for 884 passes and eleven
  environment-dependent skips. Automatic execution remains disabled.
- The [rubric iteration](history/2026-10-29-weekly-plan-rubric-iteration.md) improved routines, attribution,
  offer connections and clean copy. A [fresh three-business Home evaluation](history/2026-10-29-fresh-home-usefulness.md)
  completed without retries or editorial copies. The [owner's raw baseline](history/2026-10-30-owner-raw-usefulness-baseline.md)
  is one use-as-is (email), one useful with small edits (bookshop), and one needing expansion (service page).
  [Two raw comparisons](history/2026-10-30-prompt-usefulness-comparison.md) now show a developed service page
  within its requested range and standalone bookshop posts with natural business connections. The bookshop's
  attribution notes called a channel count an upper bound; the instructions now call it only a minimum, and all
  eight [retest drafts](history/2026-10-30-tally-minimum-retest.md) got it right. Owner judgments of both comparisons
  are pending.
  Paragraph spacing and copying individual dated or weekday-labeled posts were corrected
  on desktop and mobile. Luna remains the default;
  the DeepSeek competition stand-in is comparison evidence, not an installed model change.
- The owner-approved [replacement pilot schedule](history/2026-10-30-recurring-review-terms.md#owner-approved-activation)
  is active for two private runs on November 2 and 9 at 09:00 Mountain time, using current high-effort
  Luna/Codex OAuth terms with a 6,000-token target and dated requests. The stale original is canceled;
  readback confirms one active schedule with a current review and no occurrences consumed.
  [Copied and selected reviews](history/2026-10-30-recurring-review-navigation.md) now retain the correct address
  through reload and browser Back/Forward, with consent cleared on return.
  Hosted service acceptance, observed repeat
  use and commercial validation remain open alongside the engineering gaps in the evidence map.
- The original 31-run Luna allowance is consumed (220,397 reported input/output tokens). Additional
  owner-authorized comparisons, uncertain outcomes and provider costs are recorded in the
  [rubric history](history/2026-10-29-weekly-plan-rubric-iteration.md#usage) and private receipts.
  The separately approved fresh-business batch used three requests and 23,541 reported input/output
  tokens, consuming that allowance. The two separately approved prompt comparisons used another 15,939
  reported tokens and consumed both requests. One separately authorized internal Home run used 9,903
  reported input/output tokens and consumed its one-request allowance. Raw outputs remain unedited; actual subscription
  credit charges are unavailable.

## Next acceptance gate

1. Assess the completed internal Home draft; the owner is obtaining a separate content review. Then broaden
   the local walkthrough to fresh businesses. Keep later
   runs labeled as comparisons, never as replacement acceptance evidence. No assistant editorial copies or
   automatic retries; internal use is separate from prospective-customer validation.
2. Preserve the recorded raw baseline and obtain owner judgments for the two completed prompt comparisons.
   Further calls need separate run authorization. Do not equate complete sections or successful execution with useful work.
3. Observe prospective users with the [goal-first pilot protocol](product/pilot-protocol.md). Local runs do not
   establish signup success, retention or consistent quality. Observe the two approved weekly occurrences
   and assess their untouched results; activation alone does not establish repeat usefulness.

Commit and push each completed cycle to `main`, without a PR. Reserve new ADRs for architectural decisions;
put routine checkpoints in `docs/history/`. The repository is currently private; private pilot evidence and
credentials still stay out of source control.

## Evidence map

The tables below track the full proposal. Local/fictional proof is distinct from real customer, recurring,
commercial and production acceptance. Earlier chronological detail is preserved in
[the checkpoint archive](history/2026-10-28-context-delivery-checkpoints.md).

## Phase 1 — context-first onboarding

| ID   | Requirement                                                                                           | Current evidence / missing work                                                                                                                                                                      | Status                   |
| ---- | ----------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------ |
| P1.1 | Account/company entry; business name, optional website, goal, location/industry for disambiguation    | Business identity form and exact confirmation verified in local browser; hosted OIDC/membership exist but hosted entry remains separately unverified                                                 | partial                  |
| P1.2 | Find candidate websites when needed; confirm the correct business before importing                    | Owner-authorized candidate search, explicit selection and exact identity confirmation verified with fake API/browser; real matching quality and operator service setup pending                       | partial                  |
| P1.3 | Bounded public discovery of official pages and relevant public sources                                | Automatic confirmed-page plus two relevant-link discovery, partial failures, robots/pacing/address/time limits and immutable sources verified locally; broader source eligibility remains incomplete | partial                  |
| P1.4 | Structured profile extraction with source, discovery date and verification status for important facts | Nine-field extraction verified with fictional providers/browser and one successful native website request; five quotations checked, owner goal preserved, unsupported fields Unknown; new draft owner acceptance and broader quality pending | partial                  |
| P1.5 | Editable concise brief; confirm/correct/remove; corrections authoritative                             | Saved and unsaved corrections, exact review/unknown/revoke, immutable paged correction history and mobile source/date display verified locally with fictional fixtures                               | verified                 |
| P1.6 | Persist isolated company knowledge, documents and identity; no model use before approval              | Identity, evidence, approved profiles and private text documents persist with tenant isolation. UTF-8 and bounded Word imports, draft recovery, separate approval and scoped retrieval have fictional proof under ADR 0089. Selected Drive document copies now have fictional owner/setup/read/approval proof under ADR 0090. Selectable PDF text now has a bounded parser process, page labels, missing-text notices and separately approved scoped retrieval with local proof under ADR 0098. OCR, visual fidelity, broader formats, live private-source acceptance and further connectors remain pending | partial                  |
| P1.7 | Recommend and set up one Marketing employee from approved company knowledge                           | Role-owned setup and shared goal-specificity ranking reuse the approved brief. Ten contrasting goals, editable setup, existing-employee reuse, direct latest-result access and reviewed first assignments have fictional browser proof; broader customer recommendation relevance remains pending | partial                  |
| P1.8 | First useful modest research/marketing deliverable, observations separate from hypotheses             | The owner accepted an earlier 460-word editorial plan and, on October 30, one untouched first-run welcome email from the fresh Home batch. The other two raw results need small edits or expansion. This establishes individual owner usefulness, not consistent unedited quality or prospective-customer acceptance; raw assessment does not promote or publish a result | verified                 |
| P1.9 | Minimal-assistance onboarding that customers recognize as useful                                      | Requires observed prospective-customer sessions, not synthetic fixtures                                                                                                                              | field validation pending |

## Phase 2 — persistent employee workflows

| ID   | Requirement                                                                                                     | Current evidence / missing work                                                                                                                                              | Status                   |
| ---- | --------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------ |
| P2.1 | Durable schedules and recurring jobs; safe resume after restart; no replay of uncertain execution               | Finite daily/weekly employee work, source checks and reviewed scripts, exact approval, pause/resume, missed-slot handling and all-company startup restoration verified locally. Recurring script reports have fictional lifecycle and desktop/mobile proof under ADR 0087. Accepted measured work now copies into a separate recurring draft, preserving its skill, selected approved references, exact model and output allowance under ADR 0093. Two occurrences through separate owner result approval pass fictional lifecycle and desktop/mobile journeys. Standalone public website itineraries now have separate finite recurring consent, per-slot compute admission, saved captures, cleanup recovery and owner-reviewed completion under ADR 0096; host must be running | implemented; local proof |
| P2.2 | Durable task history, useful results, outstanding decisions and owner notifications                             | Durable personal-read inbox, exact result/schedule/plan/meeting links and bounded opt-in email with no-replay recovery verified locally. Parent plan completion, meeting decisions and uncertain-turn attention have fictional lifecycle and desktop/mobile proof under ADR 0116. Real SMTP deliverability and actual result usefulness remain pending | partial                  |
| P2.3 | Multiple distinct specialists and CEO coordination, responsibilities and private contexts                       | CEO delegation with reviewed dependencies and parent completion is verified locally. Goal-based role suggestions and distinct Developer/CEO setup and first assignments have fictional browser proof; real planning quality and recommendation relevance remain unverified | partial                  |
| P2.4 | Approved shared knowledge plus task-specific retrieval over facts, documents, decisions and prior work          | Approved briefs, documents and structured memory reach bounded exact task packets with citations, audience isolation and revocation fences; full-request keyword coverage and late-topic capture verified locally, real relevance/semantic retrieval pending | partial                  |
| P2.5 | Small authorized private integration set; scoped access and customer isolation                                  | Google Analytics/Ads, OpenRouter and Microsoft 365 have company-owned settings, credentials and providers with fictional isolation proof. A separate Drive service-account connection enforces selected files, bounded previews, mid-read revocation and separately approved text copies under ADR 0090; fictional backend and desktop/mobile journeys pass. Real/hosted authorization, delegated Drive onboarding and further connectors remain pending | partial                  |
| P2.6 | Metering, monthly allowances, billing, BYOK policy and measured cost accounting                                 | Monthly metering and price snapshots; provisional Stripe adapter, purchased AI/compute admission, signed durable events, portal and owner Billing UI verified in fictional backend and desktop/mobile journeys. Company-owned encrypted API keys, fresh model review and direct-provider funding receipts pass fictional backend and desktop/mobile journeys. Owner-reviewed per-task operating expenses, model receipts and estimates, immutable corrections and recovery have fictional proof under ADR 0097. Real hosted Stripe/provider acceptance, commercial terms and actual complete cost inputs remain pending | partial                  |
| P2.7 | Explicit exact approval for consequential actions, bounded budgets/tools/time and uncertain-outcome recovery    | The cross-phase local regression audit covers approval, admission, revocation, lost-response recovery and non-replay in the billing, recurring, delegation, model and compute flows. Real provider/runtime acceptance remains pending; fictional receipts do not establish external completion | partial                  |
| P2.8 | Meaningful change detection: verified, possible, conflicting, outdated; never overwrite approved facts silently | Manual and explicitly approved recurring source checks, dated comparison, exact owner assessments and execution fences verified locally; semantic relevance and real-site evaluation remain pending | partial                  |
| P2.9 | Customers return for recurring useful work                                                                      | Requires observed repeat-use evidence and cohort retention                                                                                                                   | field validation pending |

## Phase 3 — advanced autonomous capabilities

| ID   | Requirement                                                                                                    | Current evidence / missing work                                                                                                                             | Status                   |
| ---- | -------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------ |
| P3.1 | Scoped browser automation with explicit task authority and bounded execution                                   | Broker/worker/Chromium have fictional process-boundary E2E proof. A distinct resource adapter has offline creation, isolation, exact identity, non-replaying recovery and cleanup checks. Durable company approval, shared quota holds, non-replaying recovery and revocable capture-to-task review have fictional lifecycle proof. All-company host recovery, itinerary/capture controls and capture-to-completed-task flow have fictional desktop/mobile proof. Owner raw-capture deletion and bounded optional expiry preserve exact saved excerpt proofs, verified with fictional lifecycle and desktop/mobile checks. Browser-specific qualification, default-off host registration, expiry checks, independent readiness monitoring and original-resource recovery have offline proof under ADR 0095. Finite recurring standalone itineraries have fictional lifecycle and desktop/mobile proof under ADR 0096, including separate approval and captures per occurrence. Pinned local image passed real runsc qualification with fictional pages and restart-stable readiness on 2026-10-26; application execution remains disabled. Production qualification/activation, complete data lifecycle and real-site usefulness remain pending | partial                  |
| P3.2 | On-demand isolated compute for code, untrusted files and developer work; persist outputs and release resources | Local coding boundaries, sandbox adapter, durable approval/admission/recovery, qualified configuration, full-output download and selected-output task review/completion verified locally. Reviewed script composition with delegated plans and finite recurring reports has fictional lifecycle and desktop/mobile E2E proof under ADRs 0083 and 0087. Corrected pinned runsc profile passed bounded local standard/extended qualification and restart-stable receipt checks under ADR 0146; application execution stays disabled. Wider reliability, hostile-workload/production acceptance and actual compute costs remain pending | partial                  |
| P3.3 | Developer workflows and cross-agent task delegation with explicit provenance/permissions                       | Reviewed script-output handoff and measured delegation are verified locally. Plan scripts select exact approved predecessor results and complete through artifact review under ADR 0083, with fictional lifecycle and desktop/mobile E2E proof; exact parent authority, model/compute admission and source revocation apply. Reviewed browser assignments feeding later scripts and exclusive per-task execution have fictional desktop/mobile proof under ADR 0084. Actual repository execution and production compute orchestration remain unverified | partial                  |
| P3.4 | Expandable integration ecosystem with scoped permissions and audit                                             | Registry/module boundaries and self-described company setup now include a selected-file Drive document adapter, separate credentials, read audit, bounded import and owner disconnection under ADR 0090. Fictional lifecycle and desktop/mobile proof exists; broader connector discovery, delegated onboarding, live acceptance and additional integrations remain pending | partial                  |
| P3.5 | Advanced organizational memory, refresh/retrieval and owner feedback                                           | Decisions/goals/preferences/projects/feedback/prior work, exact revisions, review-date maintenance, citation checks and owner alerts verified locally. Result-linked owner feedback now preserves drafts, defaults to the originating employee and requires separate approval before future retrieval under ADR 0088. Owner-authorized selected-record model checks now validate exact citations, preserve facts, track stale revisions and reconcile usage without replay under ADR 0091. Cited model suggestions now open editable drafts with preserved audience/evidence, current-source checks and separate replacement approval under ADR 0094; fictional lifecycle and desktop/mobile journeys pass. Real model conflict-detection and revision quality, and semantic retrieval remain pending | partial                  |
| P3.6 | Premium long-running environments, execution tiers and usage charges                                           | Paid extended-script entitlement, exact plan/runtime admission, shared monthly allocation, in-flight cancellation and recovery without rerun have fictional lifecycle and desktop/mobile proof under ADR 0086. Bounded local extended-duration/deadline/recovery qualification passed under ADR 0146, with execution disabled. Wider reliability, production acceptance, broader premium environments and actual compute-cost accounting remain pending | partial                  |
| P3.7 | Customers pay more for measurable additional completed work                                                    | Requires actual commercial validation, not seeded billing records                                                                                           | field validation pending |

## Cross-phase architecture and acceptance

| ID  | Requirement                                                                                                                                    | Evidence required                                                                                         |
| --- | ---------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
| X1  | Shared platform; persistent identities/state independent of live compute; no permanent per-customer VM requirement                             | Restart/offline persistence and disposable-worker lifecycle tests                                         |
| X2  | Isolation at database, source/document storage, retrieval and tool execution boundaries                                                        | Cross-tenant negative tests for every new store and execution surface                                     |
| X3  | Public content is untrusted; no embedded instruction can grant permission or override approved facts                                           | Adversarial source fixtures, exact review/digest checks, guarded tool context                             |
| X4  | Access restrictions, terms, rate limits and privacy respected                                                                                  | Enforced crawl/access policy, per-origin pacing, refusal tests and documented source eligibility          |
| X5  | Private connections authorized, scoped, revocable; secrets protected                                                                           | Permission/grant/revocation tests, secret storage audit, tenant-safe retrieval                            |
| X6  | Retention controls and deletion support with audit/backup implications                                                                         | Partial: never-approved document deletion and optional discarded-draft retention, including operation-result erasure and replay protection. Raw browser capture deletion/expiry, operation-response erasure and retained exact excerpt proofs have fictional lifecycle and browser verification. Revoked document library sources now have separate exact owner deletion and receipt recovery under ADR 0099. Historical task copies, other drafts, full-company deletion and backup erasure remain pending |
| X7  | Cheapest suitable execution; model routing, deterministic processing and relevant bounded context                                              | Partial: optional purpose-based analysis routing, unchanged routine/coding routes, exact native/API dispatch, price receipts and no-retry recovery have fictional proof under ADR 0092. Real suitability and full-cost comparisons remain pending. Deterministic tools, bounded retrieval and execution entitlements have separate local evidence. |
| X8  | Budgets cover tokens/tools/runtime/spending; no unlimited free autonomous execution                                                            | Reservation/settlement tests, quotas/ceilings, retry and uncertain-usage handling                         |
| X9  | Full lifecycle: task → context → permission/budget → environment → bounded work → durable result → cleanup → notification                      | End-to-end tests for API, sandbox and premium job classes, including crashes/cancellation                 |
| X10 | Initial market stays focused on independent marketers and agencies of roughly 1–5 people                                                       | Coherent onboarding copy and research/campaign/website/report/content/opportunity/recurring-work journeys |
| X11 | Metrics: identity accuracy, time to approved profile, first-task completion, corrections, full job cost, repeat use, paid conversion/retention | Partial: company progress projects saved onboarding times, generated-brief field edits, first assignment, completed tasks, member request days and model costs into a UI/JSON export. Task-level model and non-model reconciliation now has fictional accounting and owner-journey proof under ADR 0097. Accuracy, usefulness, actual full job costs and real cohort/payment observations remain pending |
| X12 | Approximately ten-person pilot; measure corrections and valued recurring jobs before extensive infrastructure rollout                          | Pilot protocol plus real authorized observations; no fabricated customer or payment evidence              |

## Verification rules

- Keep this scope intact across implementation sessions. Record concrete evidence per requirement before marking it verified.
- Use fictional fixture data and offline/fake providers for routine checks. Live spending, external publishing,
  production deployments and customer outreach remain explicit owner actions.
- Product-validation targets require real observations. Software instrumentation and a pilot kit enable those
  observations but do not substitute for them.
- Reuse existing code when it meets the proposal; preserve previous local changes and role/integration layering.
