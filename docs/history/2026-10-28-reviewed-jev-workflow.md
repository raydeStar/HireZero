# Reviewed Jev decisions in the application

Status: source and fictional workflow verified; installed in both local workspaces with recovery backups.

[ADR 0151](../adr/0151-reviewed-jev-workflow-in-the-application.md) binds the durable decision ledger to the
existing encrypted company OpenRouter AI account. It does not read the optional tool key, change text routing
or substitute a provider when a credential changes. The current company and credential revision must match;
saved request bytes are reconstructed and checked before dispatch.

Integrations now offers decision preparation, plain-language input review, exact external JSON, explicit
one-shot authorization, history, status recovery, cancellation and usage reconciliation. Section links make
the workflow reachable without searching through account setup. The review shows question, context and options
without requiring JSON literacy. Provider estimates remain advisory and cannot authorize downstream work.

Forty-one selected xUnit facts pass with zero skips: the existing company model/account cases, core decision
lifecycle cases, and four new connection/HTTP facts. The latter cover encrypted account reload, changed
credentials, exact saved bytes, malformed inputs, tenant isolation, command replay, incomplete provider usage,
reconciliation and owner demotion during a call. An owner demoted during the awaited call cannot read the
private result or either operation receipt; the result is withheld and known accounting remains recorded.

The shared company client checks pass, including a new regression for a rejected pre-dispatch storage callback.
That refusal previously left an impossible-to-reconcile operation in the shared guard. It now clears only the
never-sent operation, with zero network requests and no replay of uncertain work.

The complete fictional Chrome journey passes with two in-process decision calls and zero live provider calls.
It verifies explicit review, lost command/status replies followed by reload and read-only recovery, no duplicate
dispatch, denied receipt storage, incomplete-usage reconciliation, unchanged text routing, section navigation
and mobile overflow. Desktop review and mobile outcome screenshots were visually inspected. Fixture processes
exit before their temporary directories are removed.

TypeScript/Vite and the company test project build pass; the existing Vite bundle-size advisory remains.
An initial overlapping web/.NET build failed while generated assets changed, then sequential builds passed.
Two browser harness corrections were necessary: the test-only mode switch requires the local-origin header,
and section links cannot use the button-only click helper. Neither was treated as product evidence until the
corrected full journey passed. All provider traffic uses fictional handlers.

Real Jev availability, charges and recommendation usefulness remain unverified. No live model request,
scheduled run, publishing or runtime qualification job was performed for this workflow.

## Local delivery

Source commit `b8d536a` is installed in both local workspaces. Each idle application was stopped independently,
its data backed up, and its prior application retained for recovery. All 180 release files match the installed
copies. Both applications are healthy, with intact audit chains and unchanged accounts, saved work and model
records. The accepted Marketing copy, native specialist originals and separate editorial copy are preserved.
The exact active two-occurrence weekly schedule is unchanged, with zero runs consumed.

The primary workspace recognizes its existing company OpenRouter AI account for decision review; this proves
local credential availability, not remote provider access. The separate content pilot retains its host/Codex
account and has no OpenRouter decision account configured. Both decision histories are empty. No credential
was copied or replaced. Automatic model, coding, browser and script execution settings remain disabled.
