# Durable browser lifecycle — 2026-10-26

Implemented [ADR 0078](../adr/0078-durable-browser-authority-and-reviewed-captures.md): company-scoped exact
browser approval, a transactional dispatch claim, cancellation/revocation fences, restart recovery without
revisiting websites, saved output before release, shared compute admission and dated capture-to-artifact review.
The usage screen now describes the shared allowance and distinguishes script/browser history. No production
browser provider or browser background queue is registered.

## Verification

- Integration test build: zero warnings/errors. `CompanyBrowserTests`, `CompanySandboxTests`, `DockerBrowserTests`
  and `BrowserProtocolTests`: 55 passed, zero failures/skips, 7.918 seconds. The new browser lifecycle class contains
  12 focused facts using fictional executors; it performs no real browser/network/container execution.
- Company test build: zero warnings/errors. Eleven migration/legacy-upgrade checks passed. The existing broader
  `CompanySuite.IntegrationPath` initially failed its HTTP transport assertion in the restricted environment;
  rerunning its fictional loopback server outside that restriction passed, one fact in 8.716 seconds. The final
  combined migration/integration pass was 12/12 in 11.999 seconds; three sandbox host-queue facts passed in
  2.030 seconds. Across the final focused backend suites, 70 facts passed with no failures or skips.
- The frontend build and `node tests/company-ui.test.mjs` passed. The existing 500 kB bundle warning remains;
  the resulting JavaScript asset is `index-DO9FoOoG.js` (507.99 kB), with unchanged `index-C92hLdNW.css`.
- `node tests/sandbox-browser-smoke.mjs` passed with the renamed shared compute controls, exact consent,
  lost-response recovery, retained output after failed release, recovery while paused/disabled, selected-output
  review, completion, handoff and late-output revocation. Its 390-pixel mobile capture was inspected: no horizontal
  overflow, readable result and review/download controls. This was fictional script execution with a real UI
  browser, not a real container or the new browser-job product journey.
- Scoped CSharpier/Prettier checks and `git diff --check` passed. Production containment and the browser-specific
  product journey remain unverified.

Receipts: `test-results/browser-ledger-build.log`, `browser-ledger-tests.log`,
`browser-ledger-company-build.log`, `browser-ledger-migration-tests.log`,
`browser-ledger-integration-path.log`, `browser-ledger-host-tests.log`, `browser-ledger-web-build.log`,
`browser-ledger-script-browser.log`, `sandbox-browser.json`, `sandbox-mobile.png` and `sandbox-desktop.png`.

The test rollback fixtures now remove migration 23's table before replaying earlier schema versions. No existing
schema checksum was rewritten. Existing dirty work was preserved. No push, deployment, real model call, runtime
upgrade, image pull, Docker execution or daemon change was performed.
