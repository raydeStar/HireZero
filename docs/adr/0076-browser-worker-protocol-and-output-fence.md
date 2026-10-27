# 0076 — Browser worker protocol and returned-output fence

Status: accepted; contained resource provider and durable company jobs pending. Date: 2026-10-25.

## Problem

The public-page broker and Chromium driver in [ADR 0075](0075-bounded-public-browser-foundation.md) were tested
separately. The script sandbox's one-input/one-output transport cannot mediate browser resource requests while
the page runs. A browser worker also must not self-certify permission, output scope or process/container cleanup.

## Decision

- Introduce a versioned, newline-delimited duplex protocol between a trusted host and browser supervisor.
  The first packet is exactly `start`, version 1, with the approved `BrowserRequest`. The worker emits sequential,
  job-bound resource requests containing only URL and method. The host responds to each request ID once and can
  send cancellation. A terminal result is accepted only once and only with a clean end of its output stream.
  Unexpected messages, duplicate properties/IDs, wrong-job requests, additional request fields, oversized frames,
  incomplete frames and data after the result are failures. Nothing is replayed automatically.
- Core owns typed capture/result records and validates job identity, state, completed reading steps, approved
  capture origins, timestamps, text/link limits, JPEG envelope bounds and disclosure of partial evidence. A
  completed receipt must account for every requested read. JPEG signatures are envelope checks, not image
  fidelity or containment proof. The host also bounds the complete raw result before deserialization.
- `BrowserWorkerProtocol` in the sandbox integration handles the stream exchange. It rechecks public GET scope
  before invoking the supplied `IBrowserNetworkSession`, rechecks authority after resource reads and again when
  the terminal stream closes. Revocation or cancellation withholds both in-flight response bodies and earlier
  captures. The authority observer cancels resource reads and asks the worker to stop while allowing a bounded
  interval to collect its cleanup receipt. Loss of the authority store closes the exchange.
- Host accounting comes from the network session, including crawl-policy traffic. Worker-reported resource
  counts are bounded diagnostics and cannot substitute for billable usage or enlarge the approved allowance.
- `runtime/browser/protocol.mjs` frames the worker exchange and supports CDP through Chromium's two inherited
  remote-debugging pipes. Broker stdin/stdout and browser debugging descriptors are separate. No debugger port
  is needed. CDP frame size, total bytes, event/command count and pending-command count are bounded; protocol loss
  rejects pending commands. The browser driver still never continues a request to direct browser networking.
- Worker input/startup, resource and result frame sizes have separate limits. Startup is at most 64 KiB,
  outgoing resource messages at most 4 KiB, host replies at most 3 MiB (including base64), and the terminal
  envelope at most 2 MiB plus 1 KiB of framing metadata. Cumulative worker input is at most 64 MiB. The independent
  driver deadline and a supervisor deadline remain active; output and debugger pipes also have byte limits.
- `serveBrowserWorker` receives a trusted browser factory rather than a tenant-selected executable or command.
  Its `close` callback reports process exit independently from the driver's context-disposal acknowledgement.
  Both acknowledgements remain distinct from OS resource removal. Failed or lost cleanup acknowledgement is
  retained as uncertainty; it does not erase a completed result or authorize replay.
- The local fixture uses a separate Node worker and a fresh headless Chrome process, supplies pages only through
  the real .NET broker with a fake HTTP handler, blocks normal DNS/proxy use and removes its owned profile after
  exit. This proves the composed protocol, not production OS containment. The browser fixture is explicitly
  opted into, so ordinary unit runs do not silently start Chrome.

## Activation remains separate

No host tool or employee workflow is registered. The helper does not select a production image, launch a browser
by itself or grant network access. A production resource adapter must bind an immutable qualified image and engine,
launch the dedicated browser without host credentials/paths and with an unprivileged process identity, apply
network-free OS isolation and resource limits, persist/recover exact terminal output and confirm removal of the
original resource. Durable task authority, quota reservations, output retention/lineage, notifications and review
UI must compose with that adapter. An interrupted exchange must reconcile the existing resource rather than start
a replacement. A late-created resource remains the creating adapter's cleanup responsibility.

Capture text, warnings and links are untrusted data. Future review UI must render text safely and only activate
eligible public HTTPS links; arbitrary scraped link schemes never become application actions.

Primary implementation reference for pipe descriptor assignment:
[Puppeteer's Chromium launcher](https://github.com/puppeteer/puppeteer/blob/main/packages/puppeteer-core/src/node/BrowserLauncher.ts).
The project does not add Puppeteer or download a browser; the fixture records behavior of the locally selected executable.
