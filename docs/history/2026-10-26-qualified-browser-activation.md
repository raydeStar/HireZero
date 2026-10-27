# Browser qualification and default-off registration

The previous memory-revision work was verified progress against the full three-phase proposal. This increment
advances P3.1 without treating fictional browser evidence as production-runtime qualification.

## Changes

- Added a browser-specific qualification receipt and production host registration under
  [ADR 0095](../adr/0095-qualified-browser-activation.md). Exact image, installation, local endpoint, CLI digest,
  engine profile, required checks and expiry gate new execution. Script receipts cannot enable browser work.
- Added an explicitly opted-in real-runtime qualification suite using fictional broker responses. It requires
  dynamic capture, scope refusal, output bounds, recovery, deadline/cancellation and resource release before
  writing a new seven-day receipt. The suite was compiled, not executed against a real runtime in this session.
- Kept original-resource recovery available after expiry or disabling new visits. No retry revisits a website.
- Moved readiness monitoring outside the serial job loop. A focused test initially exposed that the core
  in-flight watcher omitted runtime availability; it now cancels when availability is withdrawn. The test fixture
  was also corrected to return valid cancellation and release receipts. Historical evidence remains reviewable.
- Replaced generic unavailable messages with distinct paused, expired, unreachable and missing-worker guidance.

## Verification

- Core, Sandbox integration, Host, Integration Tests and Company Tests builds passed. TypeScript/Vite passed;
  Vite retains its existing large-chunk advisory.
- **52 integration facts passed**, zero failures/skips: `DockerBrowserTests`, `SandboxQualificationTests`,
  `BrowserProtocolTests` and `CompanyBrowserTests`. This includes receipt mismatch/expiry, original-binding
  recovery, engine drift, cancellation and durable browser lifecycle regressions.
- **17 host/settings facts passed**, zero failures/skips: `CompanyBrowserHostingTests`, `CompanySettingsTests`
  and `CompanySandboxHostingTests`. Independent readiness monitoring withdraws authority during an active job;
  cleanup settles once without replay.
- **1 company UI contract check passed**.
- **1 local Chrome desktop/mobile journey passed** using the fictional browser executor: exact approval,
  captures/downloads, unknown-release recovery while paused, review while disabled, lost-response recovery,
  cancellation, deletion/retention and parent-task completion. No horizontal mobile overflow was detected.
  Mobile capture review and retention settings screenshots were visually inspected.
- C# and frontend formatting and `git diff --check` passed. Compact receipts/logs are under ignored
  `test-results/browser-qualification-*` and `test-results/browser-review-browser.json`.

## Remaining acceptance

Read-only Docker inspection could not connect to the configured local Linux-engine pipe. No engine was started,
image built/pulled, production Chromium pin selected, container launched or real qualification receipt issued.
No live model, payment, customer-site or publishing action was performed. These checks establish software behavior
with fictional inputs; actual containment, real-site usefulness and production activation remain unverified.
The three product phases remain incomplete, including their real customer and commercial validation targets.
