# 0110 — Browser startup within the existing memory budget

Status: accepted for the candidate rebuild; actual qualification is recorded separately. Application execution remains disabled.

The first contained browser experiment exposed two adapter compatibility bugs: Docker's inspected capability
names include `CAP_`, and its local log driver requires compression to be explicitly disabled with `max-file=1`.
The adapters now compare equivalent capability names without accepting missing, extra or duplicate grants,
and explicitly request/check `compress=false`. Thirty-five focused offline integration facts pass on Linux.

With those fixes, the browser started but initialization stalled before any page request. A bounded startup-only
probe measured 507.6 MiB of the 512 MiB allowance and about one CPU in use. Chromium answered browser-level
debugger commands, while creation of the task's additional context stalled. This is evidence of memory pressure,
not a complete process-by-process memory attribution.

Suppress the unused initial window with `--no-startup-window` and disable component extensions that have
background pages. The itinerary continues to create its own isolated context and page. A probe with both
changes completed context creation, attachment, discovery, interception setup and orderly shutdown before its
five-second resource sample; the sample therefore did not measure a new peak. Its result is startup evidence,
not a full qualification pass. Discovery filters tested during diagnosis are not adopted.

Keep the existing 512 MiB memory, one CPU, 128-process, network, mount and capability bounds. Chromium's sandbox
flags remain intact. Rebuild the worker from the unchanged immutable base and qualify the resulting new image ID.
The initial image and failed-attempt receipts remain available privately. No application activation follows
automatically from a local pass.

Both startup switches are used in [Playwright's Chromium launch implementation](https://github.com/microsoft/playwright/tree/main/packages/playwright-core/src/server/chromium).
The exact candidate versions and local evidence are in the [execution receipt](../history/2026-10-26-local-browser-qualification.md).
