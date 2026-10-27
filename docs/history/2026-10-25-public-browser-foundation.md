# Public browser foundation — 2026-10-25

Scope: begin P3.1 with a role-neutral, bounded public-page itinerary, a separate authorized network broker and
a real Chromium driver. This advances the three-phase goal without registering an incomplete runtime.
Decision: [ADR 0075](../adr/0075-bounded-public-browser-foundation.md).

## Implementation

- Core browser contracts bound approved websites, steps, viewport, runtime, requests and decoded response bytes.
- `PublicBrowserNetwork` checks scope, public socket addresses, crawl rules, pacing, read-only methods, redirects,
  response types, body sizes, revocation and cancellation. No browser cookies, authentication or bodies are
  forwarded. Shared research pacing now honors a newly discovered crawl delay before fetching the first page.
- The CDP driver supports dynamic public pages, safe-selector clicks, waits and bounded text/JPEG captures in a
  fresh incognito context. Isolated-world extraction avoids page-modified DOM helpers. Refusals and truncated
  evidence remain visible; capture URLs cannot silently change during reading or screenshots.
- Chromium's browser-owned address-bar targets are excluded from false popup warnings. Site windows remain
  restricted. Same-document URL fragments are included in source identity because CDP reports them separately.
- Context cleanup is explicitly confirmed or uncertain. Completion is not a container-release receipt and no
  uncertain action is retried.

## Verification

- Integration test project build: zero warnings and errors.
- Focused `BrowserNetworkTests` plus existing `WebResearchTests`: 12 passed, zero failed/skipped, 23.288 seconds.
  Cases cover sealed scope, public origins/methods, no forwarded credentials, crawl rules and first-request delay,
  request/body limits, revocation and deadline behavior. HTTP providers are fictional.
- `tests/browser-driver.test.mjs`: 11 scenarios on local Chrome 156.0.8078.47 with fictional intercepted pages.
  Coverage includes redirects, separately loaded JavaScript and JSON, clicks, navigation, capture, isolated
  storage/cookies, attempted writes/off-site requests/popups, form refusal, bounded text/links, request storms,
  a stalled deadline, cancellation, oversized replies, same-document capture changes and uncertain cleanup.
- The saved dynamic-page JPEG was visually inspected. It shows the services revealed through JavaScript before
  capture. Compact local evidence is in `test-results/browser-driver.json`, `browser-driver.log`,
  `browser-driver-page.jpg`, `browser-foundation-build.log` and `browser-foundation-tests.log`.
- CSharpier and scoped Prettier formatting were applied. Disposable owned Chrome profiles are removed after exit.

The first build found an unreachable derived-exception catch, which was removed. The initial broader partial-result
assertion exposed browser-owned UI targets being misclassified as extra site windows. A new same-document
navigation test exposed omission of CDP's separate fragment field. Both driver issues were corrected before the
final focused run; the passing count refers to the final source.

## Explicit limits

The broker tests and Chromium driver fixture verify their respective boundaries; there is no registered
end-to-end host/browser provider yet. No production image was built or downloaded, no daemon was changed,
no live site/model was used and no deployment was performed. The existing script sandbox remains unchanged.

Next composition: bounded supervisor protocol and qualified runtime identity; durable approval, compute allocation,
usage settlement, revocation, recovery and output storage; task review, lineage, notifications and readiness UI.
Actual OS containment qualification and real website usefulness remain separate acceptance checks. P3.1 is
partial and all three product phases remain in progress.
