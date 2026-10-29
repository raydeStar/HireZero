# Context-first product delivery

Scope: all three phases of the [owner's proposal](product/context-first-vision.md).
Priority: immediate usefulness. State a goal, receive useful work with minimal input and navigation.

## Current checkpoint — 2026-10-29

- Home accepts a business sentence, goal and optional website, including [ordinary pasted addresses](history/2026-10-29-home-website-addresses.md),
  then generates one disclosed draft. Results
  open with Copy, Download and Refine, plus [section copying](history/2026-10-28-section-copy.md) beside individual
  post headings. [Section exports now keep nested headings and bold emphasis](history/2026-10-29-channel-aware-copy-and-complete-sections.md).
  Internal notes, receipts and administration are collapsed. Company knowledge and
  Settings group the navigation; multi-employee tools stay secondary for a small workspace.
- Weekly plans adapt to the business instead of prescribing an AI-workflow demonstration. The latest
  [length and table follow-up](history/2026-10-28-deliverable-length-and-tables.md) removes a conflicting Core
  word ceiling and renders readable tables. [Weekly-use follow-up](history/2026-10-28-weekly-use-and-review-notes.md)
  adds proposed posting days, batch preparation and recognition of internal-review headings for exact exports.
  Home now also recognizes [natural requests for a week's content](history/2026-10-29-natural-weekly-goals-and-raw-comparison.md).
- Raw quality remains uneven and Luna remains the default. After earlier rejected or generic results,
  the [latest agency comparison and fresh photography-studio run](history/2026-10-29-channel-aware-copy-and-complete-sections.md)
  give practical guidance with less repetitive service copy and better channel attribution. The owner found the
  photography draft useful but noted that its routine omitted replies and its tally overstated per-post tracking.
  A [rubric-scored iteration](history/2026-10-29-weekly-plan-rubric-iteration.md) revised the starter-plan
  instructions. Under strict blind grading, grounding, clean plain-text copy, realistic routines and voice now
  reach at least A- in every final sample. Craft depth, distinct value, offer phrasing and the next-week rule
  still vary between B- and A. Owner acceptance remains open. The
  [model checks](history/2026-10-29-weekly-plan-rubric-iteration.md#model-checks) on that text returned no draft:
  Sol at high effort hit Home's 90-second deadline, and DeepSeek V4.1 Flash, the model for the owner's Vultr competition build, spent
  its whole 1,800-token OpenRouter output limit on reasoning.
  [ADR 0166](adr/0166-openrouter-deliverables-get-their-output-target.md) gives OpenRouter deliverables their
  policy's 6,000-token target. DeepSeek still reasoned through it. With reasoning turned off
  ([ADR 0169](adr/0169-openrouter-reasoning-per-model.md)), it completed a plan. That plan blind-graded about B
  against about A- for Luna, and the owner judged it unshippable. Vultr serves the 1017 snapshot with reasoning
  always on. A stand-in for it, with low reasoning and room for 16,000 tokens, graded about A-, level with Luna's
  best. Earlier
  [Sol diagnostics](history/2026-10-28-frozen-request-model-comparison.md) were stronger by assistant assessment,
  but actually used default-low effort because the runtime ignored their requested effort. Saved model text
  remains unedited. Repeatable first-run usefulness remains open.
- Both local installs now include confirmed thread-start effort and v3/6,000-token terms after
  [regression fixes and backed-up installation](history/2026-10-29-handoff-regressions-and-local-install.md).
  The drafting, measured and schedules browser checks pass; selected mobile navigation stays visible.
  The primary company install was later republished at ff69ecf, then at 655196a, each after a data backup. The
  pilot install was not updated. On 2026-10-29 "database is locked" errors in the scheduler stopped the primary
  host twice, and its startup task did not restart it. Since
  [ADR 0167](adr/0167-a-locked-database-skips-a-scheduler-round.md), such a round is logged and tried again
  instead. The install keeps thirteen companies in one database file, and the host runs a scheduler for each.
  Timestamped warnings show rounds skipped across several of those companies, including at startup.
  [ADR 0168](adr/0168-company-writers-queue-for-the-database.md) measured the cause on a copy: short transactions,
  but blind 150 ms retries. Queued writers with SQLite's busy handler cut the longest wait from 1.9 s to 75 ms.
  The old weekly schedule remains saved under v2/3,000 and
  [shows its outdated approval before the due date](history/2026-10-28-schedule-review-visibility.md), with a
  direct review link. No replacement is active.
- The 31-run Luna evaluation allowance is used up, with 220,397 reported input/output tokens; the rubric iteration
  took its last six runs, then fourteen more under the owner's instruction to cycle until confident (164,063 tokens
  for all twenty). Both separately authorized Sol diagnostics completed, reporting 14,892 tokens. Of two more Sol
  runs approved for the final text, one was cancelled at the deadline with usage unknown and one was left unused.
  OpenRouter test runs are limited to one a day unless the owner approves more; the owner approved four on
  2026-10-29, billed $0.0080 in total.
  Exact receipts and untouched raw outputs stay private; actual credit charges are unavailable.
  Three separately approved handoff diagnostics consumed their own grants: two completed with 15,833 reported
  tokens; the withheld request last reported 9,344 tokens, with final usage unknown. The latest cycle used
  two of the broader allowance's runs and 15,616 reported tokens.

## Next acceptance gate

1. Improve repeatable first-draft usefulness across more fresh businesses. Keep later runs labeled as comparisons,
   never as replacement acceptance evidence. No assistant editorial copies or automatic retries.
2. Obtain owner usefulness assessment; do not equate complete sections or successful execution with useful work.
3. Observe prospective users with the [goal-first pilot protocol](product/pilot-protocol.md). Local runs do not
   establish signup success, retention or consistent quality. Review a new weekly schedule before recurring use.

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
| P1.8 | First useful modest research/marketing deliverable, observations separate from hypotheses             | The owner accepted the 460-word editorial copy after a native revision, confirmed its claims, concrete examples and completed-use learning goal, and authorized approval unchanged. The exact copy is approved and its task completed. This verifies one owner pilot including human editing, not consistent unedited quality or broader customer acceptance | verified                 |
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
