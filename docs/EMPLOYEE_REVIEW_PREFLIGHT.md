# Preparing two employee reviews

These workflows use the existing one-turn conversation path. Selecting an employee is explicit; free text does not silently delegate to another role or invoke a classifier. A Marketing application review goes to the selected Marketing employee. A Developer repository review is informational text and uses Luna; the Sol coding route requires a separate execution workflow and approval.

No live review is enabled by these tests. Their evidence describes a fictional Compass application and fictional `example/compass` repository. Every generated finding begins with **SIMULATED** and says it is synthetic test output. These are routing and accounting checks, not employee findings about the real application or any real repository.

## Evidence and access

The company-local profile currently has no GitHub evidence collector, repository registration bridge, coding runner or live repository-execution factory. It does not inherit legacy discovery settings or host services. A repository name or URL in a message supplies no access. Native replies cannot fetch links, browse GitHub or read the checkout.

The retained legacy Developer code includes explicit repository resolution, owner-PR/check observation and bounded supporting-evidence readers. Its general repository-chat route also calls interpretation/triage models and may escalate proposal review. That route is separate from company-local and is not an approved shortcut for a bounded one-turn review. Do not invoke it merely to collect evidence or enable its automatic services.

Collect any future evidence outside the model turn through an explicitly scoped read-only capability, then inspect and approve the exact outgoing packet. Credential permission to read a repository does not authorize forwarding its private content to a model provider. Never import credentials, auth/config files, personal records, private chats, unrelated checkout files, binary attachments or full logs. A bounded redaction scan is evidence, not a guarantee that content is safe.

Start with one owner-selected primary repository. Related repositories remain excluded until the owner names them and their relationship is established; at most two additional repositories may be proposed. Do not enumerate the account or follow arbitrary repository links. A proposed first pass may use repository aliases, default-branch/revision references, manifest versions, bounded check/PR metadata and local validation summaries. Label local-checkout evidence separately from the remote revision. Source excerpts, issue bodies, comments, diffs, logs and screenshots need their own exact payload review before inclusion.

For the Marketing review, provide a sanitized text description of navigation, onboarding, company/private context, the prepare/review/Send interaction, review-required artifacts and visible cost disclosures. The native path is text-only; do not silently forward screenshots or claim that the employee opened the running app.

For the Developer review, provide only the approved repository evidence summary. Ask for a small number of update candidates with evidence references and missing-data labels. Dependency versions alone do not establish a vulnerability or prove that an update is available. The employee must not claim to have scanned GitHub or executed tests when the packet contains only supplied summaries.

## Dispatch and results

The initial proposed bound is **two native turns total**, one per review, with no classifier, extra validation turn, tool call or automatic follow-up. Exact model, profile, recipient, scope, instructions, evidence, intent allowance and digests must be reviewed before each Send. No real packets are dispatched while payload/destination authorization is pending. The target Developer must exist and be active before its live packet can be prepared.

Each application packet must fit 4,096 UTF-8 bytes including instructions and context. Its 8,000-token reservation is an estimate, so two turns represent 16,000 planning tokens, not an upstream maximum. The 60-second deadline, 2,000-character stop, 1,000 output-token target and observed-usage checks are soft. Native internal retries may occur. Any proposed existing-credit allowance records intent and cannot enforce a credit ceiling. Included usage must be available; no purchase, recharge or API/provider fallback is allowed. A provider-enforced spending maximum cannot be supplied by this native interface.

Preparation sends no native request and reserves no tokens. A sealed proposal expires after ten minutes and is checked again during dispatch/execution; it cannot remain valid indefinitely while awaiting an owner response. Use the ordinary user-controlled review and **Send & authorize one reply** flow when ready. Never renew an expired packet or reuse an approval silently.

Successful informational findings are saved only in the selected conversation. Returning from management or reloading the page reads that history without redispatch. Results create no plan, coding task, approved artifact or execution grant. Duplicate operation IDs reconcile to one turn; a new stale operation is refused. Cancellation withholds partial text, retains known/unknown usage appropriately and cannot replay.

## Local checks

The full Company test suite includes two fictional review inventories, correct role/Luna routing, exact-context isolation, duplicate-operation reconciliation and stale-ID refusal, full usage settlement, saved synthetic findings, reopen without replay, pre-dispatch cancellation and cancellation after an emitted native fake delta.

`tests/employee-review-browser-smoke.mjs` follows both review requests through the actual UI, owner packet review, a compiled fake native-stdio provider, saved findings, management navigation and return-to-chat. It also checks cancellation, unknown-usage holds and reload without replay. The fixture makes zero GitHub requests and zero live model calls. Existing conversational and manual-draft browser flows remain regression checks.
