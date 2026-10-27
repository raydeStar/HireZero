# 0075 — Bounded public-browser execution foundation

Status: accepted; provider, durable composition and production qualification pending. Date: 2026-10-25.

## Problem

Public discovery reads static text. It cannot inspect JavaScript-driven pages or save a visual capture after a
specific navigation. The context-first proposal also requires scoped browser work. Adding unrestricted browser
networking to the script sandbox would bypass its isolation, access checks and bounded execution contract.

## Decision

The first browser workflow is public website inspection. It uses a reviewed, fixed itinerary of navigation,
non-form clicks, selector waits and text/screenshot reads. It accepts no arbitrary script, credential, request
body, account profile or model loop. This is a foundation, not a registered product tool or a general account
automation capability. No employee can invoke it yet.

- Core owns the role-neutral request and network-session contracts. An itinerary names a job, company, one to
  four exact HTTPS origins and two to sixteen steps, beginning with navigation and containing one to four reads.
  Runtime, request-count, response-body and viewport limits are explicit and bounded. Both the network broker
  and driver validate their inputs; changing the caller's input arrays cannot expand a running job's scope.
- The intended production browser runs in a fresh, qualified, network-free operating-system sandbox. A trusted
  supervisor carries bounded resource messages to a separate host broker. Chrome request interception alone is
  **not** a network containment boundary. The new driver cannot enable a runtime or change existing script
  sandbox permissions. A production image, supervisor, resource adapter and qualification are still required.
- `PublicBrowserNetwork` owns one short-lived HTTP session per job. It sends public GET requests only, on the
  standard HTTPS port, to approved DNS origins. It reuses the socket-time public-address check, disables ambient
  proxies/cookies/automatic redirects and sends its own declared research user agent. Browser headers, cookies,
  authorization and bodies never enter this interface. It accepts no credentials.
- Each origin's crawl policy must permit the requested path. Missing policies (404/410) are allowed; unavailable,
  restricted, unreadable or cross-origin redirected policies are refused. All actual HTTP requests,
  including crawl-policy reads, count against the request limit. Shared research pacing applies a newly discovered
  delay before the first page request. Delays above ten seconds are refused within this short job class.
- Redirects return only a validated approved-origin location to Chrome, which must request the next URL through
  the broker again. Restricted/rate-limited responses and downloads are refused. Individual response bodies are
  at most 2 MiB; crawl policies are at most 128 KiB. The overall decoded body allowance is at most 16 MiB.
  Oversized unknown-length streams are detected after at most one extra byte, which is counted. Requests are
  never retried. Revocation and cancellation are checked before transport, during reads and before releasing content.
- The driver creates a fresh incognito context in a dedicated browser, disables caches and service-worker use,
  and supplies intercepted responses through CDP. It never continues a request to the browser's own network.
  Authentication challenges are canceled, downloads denied and site-created windows/worker targets closed or
  kept paused until context disposal. Chromium-owned `browser_ui` targets do not create false page warnings.
  Operating-system containment remains necessary for every browser subsystem and any compromised process.
- Clicks are limited to visible, uncovered links and non-form buttons. Forms, file inputs, download links,
  disabled controls and new-window links are refused. This does not prove arbitrary public sites are free from
  side effects: operator source eligibility and exact owner approval are required before activation. Authenticated
  browsing, submissions, publishing, account actions and arbitrary code are outside this first job class.
- Hardcoded extraction runs in an isolated JavaScript world, avoiding page replacements of DOM helper methods.
  Page text and link labels remain untrusted evidence. A page cannot change the itinerary or grant permission.
  Capture provenance includes the full URL, including CDP's separate fragment field, and a timestamp. A document
  or URL change during capture invalidates it rather than relabeling the result.
- A read retains at most 8,000 text characters and 40 bounded links, plus an optional viewport JPEG of at most
  256 KiB. The final envelope is at most 2 MiB, including refusal diagnostics. Refused resources, truncation or
  omitted captures make evidence explicitly partial. Completing an itinerary is distinct from complete page coverage.
- Cancellation, deadline, failure, partial evidence and context cleanup are independent receipt fields. Lost
  cleanup acknowledgement remains unconfirmed even when work completed. Context disposal is not evidence of
  container removal. A future supervisor must stop the dedicated process and reconcile the original resource;
  uncertain external work or resource creation must never be replayed automatically.

## Evidence and remaining composition

Focused offline .NET tests cover scope, methods, robots, pacing, request/body limits, headers, revocation and
deadlines. A real local Chromium fixture fulfills only fictional pages and tests dynamic JavaScript, navigation,
captures, profile isolation, adverse requests, timeouts and cleanup receipts. Its blocked proxy/DNS settings are
fixture protection, not production containment qualification. No public website or model is contacted by these tests.

Before registration, implement the bounded supervisor protocol, pinned image/runtime identity, OS resource
provider and qualification; durable exact approval, quota reservation and settlement, revocation/cancellation,
crash recovery, output validation and retention; task review, source lineage, notifications and user-facing readiness.
Review real source eligibility and usefulness separately. These are acceptance requirements, not implied
capabilities of the driver. P3.1 remains partial and no product phase is complete.

Primary protocol references: [Fetch](https://chromedevtools.github.io/devtools-protocol/tot/Fetch/),
[Target](https://chromedevtools.github.io/devtools-protocol/tot/Target/) and
[Page](https://chromedevtools.github.io/devtools-protocol/tot/Page/).
