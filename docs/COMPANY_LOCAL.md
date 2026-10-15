# Using the local company workspace

The company workspace manages departments, employee instances, private conversations, internal tasks, reviewed artifacts, CEO plans and Board Meetings in one HireZero backend. Multiple employees can share a role without sharing private history or notes. The current local profile does not execute live model work or repository changes.

See [employee execution readiness](EMPLOYEE_EXECUTION_READINESS.md) for the current Marketing, Developer and CEO/meeting paths, the saved file-diff review, and the concrete gaps before live work.

## Codex connection and model routing

Runtime selection is separate from model routing. Ordinary tasks, Marketing work, CEO coordination, meeting turns and Developer conversations record `openai/gpt-6-luna`. Only explicit `developer-work` execution tasks record `openai/gpt-6.1-sol`; reviewed execution proposals retain that route. Older tasks without routing metadata receive it when first admitted. Configuration changes do not rewrite a recorded route. Task cards show the saved runtime and model.

The launcher accepts `-TextModel` and `-CodingModel`; backend equivalents are `HIREZERO_COMPANY_TEXT_MODEL` and `HIREZERO_COMPANY_CODING_MODEL`. Requests cannot supply model/runtime authority. Supported Codex `model/list` discovery must contain both exact selected models. There is no model or provider substitution.

Add `-Codex -CodexExecutable <absolute-installed-executable> -CodexIdentity <reviewed-bundle-identity> -CodexHome <dedicated-profile-home>` to select the optional connection. The home is passed only to owned native processes; the user's global profile is not reconfigured. `CompanyCodingFactory.Identity` hashes the executable and any companion .NET assembly/configuration files. Review this identity after a runtime update; it is not a credential. The owner-only `/api/company/runtime` endpoint inspects account, catalog and allowance without starting model turns. It uses native Codex sign-in, with no credential-file reading/copying, login flow or account-setting calls from HireZero.

The owner completed official native sign-in for a dedicated profile. Supported status/account checks confirm ChatGPT Pro; an empty ephemeral thread reports zero ambient instruction sources and a read-only/no-network sandbox. The first separately authorized fictional qualification attempt was admitted by approval review but refused by Codex at `turn/start`; no turn ID or token-usage receipt was returned. CPU-only investigation identified the installed release's explicit rejection of legacy `readOnly.access`, before turn dispatch. The connector now selects a process-local named permissions profile, verifies scratch-only reads/no network/no inheritance, and checks returned profile provenance. The failed request was not replayed; no further native turn was sent during repair. See the qualification report for version-specific evidence and accounting limits.

Codex exposes no compatible exact preflight token counter or hard completion-token field in its installed app-server protocol. Automatic company execution stays disabled. The separate qualification entry point requires a scoped credit allowance and reserves conservative in-flight exposure before each of at most four attempts. It requests low reasoning, standard service tier, no application retries, 60-second local deadlines, restricted read-only roots, feature/memory restrictions, output character limits and complete usage checks. An initial Windows check could read outside the named profile, but the subsequently approved process-local MXC check passed permitted-read, denied-sibling-read, denied-write and owned-network-fixture controls. The connector selects and verifies exact MXC without fallback. No saved settings or broader permissions were applied. The corrected live qualification command was blocked by execution approval review before starting; no new native turn or usage receipt exists. These controls are not backend token/spending ceilings or proof that cancellation stops remote generation. See the qualification report for current evidence.

## Build and open

Build the locked frontend first, then the host. Do not rebuild frontend assets while a .NET build is scanning them.

```powershell
npm ci --prefix web --ignore-scripts --no-audit --no-fund
npm run build --prefix web
dotnet build src/HireZero.Host/HireZero.Host.csproj
./tools/Start-CompanyLocal.ps1 -DataRoot /path/to/a/new/company-data-directory
```

Use an absolute directory appropriate to your machine. The launcher requires an explicitly selected data root and refuses an existing legacy database. It runs the company-only composition profile at `http://127.0.0.1:5065/`. Stop this foreground host with Ctrl+C. An occupied port is an error; do not stop another application to make room. Process environment changes are restored when the launcher exits.

The installed SDK used during development was 10.0.203, while the source global.json requests 10.0.302. The implementation handoff records the direct MSBuild invocation used with the existing SDK. No SDK/runtime was installed or global setting changed. Frontend dependencies retain their lockfile versions and install lifecycle scripts are disabled.

## First management workflow

1. Create Marketing, Engineering and optionally Leadership departments.
2. Hire the first employee in a department with a role and a concrete responsibility. That employee is the onboarding guide. Additional hires wait for approved department knowledge.
3. Complete the department's onboarding fields, save a draft, inspect it and approve the exact version. Later employees inherit this knowledge reference; they do not inherit the guide's chats, private notes, credentials or approvals.
4. Open an employee from the roster. Its private conversation is created on hire. Add private notes, choose a skill and assign work with selected approved evidence. Marketing drafts use a structured campaign brief.
5. Live model work becomes visibly blocked. You can still add an owner-authored artifact to a saved task, review its exact content, share an approved artifact to a specific employee conversation and mark reviewed work complete.
6. Company conversation updates reflect committed state and link to employees. An unaddressed message is an owner note; an addressed message creates work only for the selected employee.

Knowledge edits create a new draft; existing tasks retain their admitted version. Revocation prevents use or publication of affected pending output. Employee pause, retirement, cancellation and reassignment invalidate stale dispatch/result authority. Retired conversations and notes remain readable; private notes cannot be edited after retirement.

## CEO and meetings

A CEO is another employee instance, with the human owner retaining authority. The CEO receives approved knowledge, employee responsibility/identity metadata and task status metadata, not other employees' private transcripts or notes. Model proposals are artifacts requiring review. A reviewed structured proposal can be converted to a proposed plan; exact plan approval is a separate step before child tasks are admitted. You can also author a plan using the structured Plans screen.

Plans contain at most four ordinary internal tasks, explicit assignees, forward-free dependencies and a shared allowance. Recursive/self-delegation is refused. Root cancellation reaches pending child work. A task's review and completion do not grant publication or repository rights.

Board Meetings have selected employees, a qualified CEO chair, an agenda and selected approved evidence. Participant requests contain the shared meeting transcript and approved packet, excluding private preparation and unrestricted departmental notes. There are two turns per participant, at most five participants, a 15-minute deadline and a shared budget. The chair's synthesis consumes a turn. A lost or interrupted provider outcome is held for reconciliation and is not automatically replayed.

The owner records and accepts exact minutes after the discussion. Closing minutes dispatches no follow-up tasks. Follow-up work requires a separately reviewed plan. In normal local mode, discussions remain blocked because the live adapter is unavailable. Deterministic test fixtures exercise their full state machines without network inference.

## Execution and storage boundaries

The local profile registers no legacy model gateway, runtime preparation, coding runner, discovery timer, connector client factory or session watcher. It owns a lock only in its selected company data root. Existing Developer workflows are preserved under their existing host profiles; employee text/advice work uses a separate explicit route and does not enter the legacy responder chain.

New data lives in namespaced `co_*` tables inside the selected `workspace.sqlite`. Versioned schema checksums, relational ownership keys, transactional operation receipts, attempt fences and feed events protect mutations. Private context snapshots include provenance; the task-detail API exposes their digest and selected evidence metadata. Existing prototype and legacy tables are not imported or replayed. Production migration remains a separate task.

This first API exposes owner-only reads under `/api/company` and a strict typed command envelope at `POST /api/company/commands`. Commands require a UUID requestId and expectedVersion for mutable entities. Unknown JSON fields, stale versions and changed idempotency payloads are refused. `GET /api/company/operations/{requestId}` reconciles a lost HTTP reply; the frontend blocks further mutations while an operation remains unconfirmed. Feed reads resume strictly after their sequence cursor.

Internal checkout-claim scaffolding requires an explicit owner `workspace.grant` command for an eligible undispatched Developer task, then an attempt fence. Claims canonicalize repository roots and reject reparse traversal; another employee cannot claim the same checkout through a nested directory alias. Uncertain attempts retain their claim. This is a future adapter prerequisite; it does not enable repository execution. Windows drive aliases and any future execution adapter need qualification before actual writes are enabled.

Employee calls reserve a 5,000-token maximum before dispatch: at most 4,000 input and 1,000 output tokens. Required context cannot be silently dropped to fit. Optional history is packed deterministically and the stored snapshot matches the admitted request. Ordinary tasks have a 5,000-token root; plans and meetings have their approved shared roots. Company-module, department and employee ceilings also apply. Missing or uncertain usage retains reservations rather than becoming zero; retries and reassignment retain their root allowance. These are finite module limits, not an account-wide or monetary spending guarantee.

There is no live route, paid inference, tool channel, publication or automatic retry. Live activation requires separate proof of runtime isolation, compatible token counting, complete bounded usage, cancellation/outcome accounting and safe interaction with existing account serialization. It must not be obtained by falling back to the unrestricted owner runtime or duplicating an installation for each employee.

## Verification

`tests/HireZero.Company.Tests` exercises fictional departments, several employees, context isolation, targeted handoffs, owner reviews, concurrent claims, cancellation, uncertain usage, resource fencing, CEO approval, finite meetings and loopback HTTP hygiene. It uses fresh temporary roots and deterministic providers.

`tests/company-ui.test.mjs` checks lost-response reconciliation and prevention of accidental duplicate operations. `tests/company-browser-smoke.mjs` uses an already installed headless Chrome, an empty disposable browser profile and a fresh company-local host; it never opens or changes the user's browser profile. It exercises the management flow and writes desktop/mobile screenshots to ignored test-results. It requires local socket access and a free port 5065.

Existing Core, Developer, Host, Policy, Integration, frontend approval, execution-feedback and metadata-hook checks remain regression gates. A passing frontend build alone is not interaction evidence. Native computer-use tooling failed to initialize on this machine; browser smoke testing is a separate isolated application test, with results reported explicitly.

Current deliberate limits: exact owner approval per CEO plan (reusable mandates are not enabled); owner-authored final meeting minutes; no live adapter or Developer execution bridge; no automatic import of the old Marketing data; one owner/company per root; bounded local employee/queue counts. Existing legacy host profiles remain separate from company-local mode.
