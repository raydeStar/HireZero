# Browser worker protocol — 2026-10-25

Scope: compose the previous public-browser foundation across a real process boundary and validate returned
outputs before durable company jobs are introduced. Decision:
[ADR 0076](../adr/0076-browser-worker-protocol-and-output-fence.md).

## Implemented

- Typed Core browser results validate scope, reading steps, public capture origins, content bounds, partial
  evidence and JPEG envelopes. Context cleanup remains independent of process exit and container release.
- A bounded duplex host exchange validates worker message identity/order/schema, mediates public GET resources,
  observes cancellation/revocation and withholds output even when authority changes at end of stream. Network
  usage comes from the host's broker, including crawl-policy requests and bytes.
- A Node worker protocol and bounded CDP pipe transport connect the existing driver to a trusted browser factory.
  Wrong-job or repeated host replies, malformed framing, debugger failures and transport limits stop work.
- The local E2E fixture launches a separate worker and Chrome using inherited debugging pipes. Its only page
  source is the actual .NET broker backed by fictional HTTP responses. It exercises a page's script, dynamic
  fetch, click, selector wait, text/JPEG capture and clean process exit. A second journey cancels during an actual
  broker resource read and verifies no replay and no disclosed capture.

## Verification

- Integration project build: zero warnings and errors.
- `BrowserProtocolTests` and explicitly enabled `BrowserWorkerIntegrationTests`: 10 tests, zero failures/skips,
  7.005 seconds on the final source.
  Eight focused facts cover protocol identity, duplicate/trailing/missing/oversized messages, output scope,
  revocation and independent cleanup acknowledgements; two facts cross the real Node/Chrome process boundary.
- `node tests/browser-protocol.test.mjs`: passed stream-boundary, malformed/incomplete/oversized-frame,
  mismatched-debugger-response, startup-scope, duplicate-reply and wrong-job-reply checks.
- Compact local evidence: `test-results/browser-protocol-build.log`, `browser-protocol-tests.log`,
  `browser-worker.json` and `browser-worker-page.jpg`. The fixture removes only its own temporary browser profile
  after its process exits. No user browser or existing application host is stopped.
- The saved JPEG was visually inspected and shows the dynamically loaded fictional services. The final receipt
  records four completed steps, one capture, five host HTTP requests (including crawl policy), confirmed context
  and browser-process exit, and no partial evidence. No owned fixture process or temporary profile remained.
- CSharpier and scoped Prettier formatting applied; existing script execution and its transport are unchanged.

The earlier broker/driver checks are retained. This turn adds composed boundary evidence; it does not replace
the previous crawl-policy, adverse-page or screenshot-provenance tests.

## Remaining work

The production browser resource adapter, immutable image selection and real OS qualification are not complete.
No image was built/downloaded, no runtime/daemon was changed, and no model, live public site or customer account
was used. The worker helper remains unregistered. The next implementation must bind resource identity, bounded
execution/log recovery and confirmed cleanup, then connect durable exact approval, quota allocation, cancellation,
saved output and task review. Full P3.1 and the three-phase product goal remain in progress.
