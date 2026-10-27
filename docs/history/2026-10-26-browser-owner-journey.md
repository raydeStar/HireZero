# Browser owner journey — 2026-10-26

Implemented [ADR 0079](../adr/0079-browser-queue-and-owner-review-journey.md): company-scoped background queue,
startup recovery, explicit execution/recovery readiness, owner HTTP routes, lazy-loaded itinerary/capture UI,
shared allowance wording, dated artifact provenance, plain-text/screenshot/JSON downloads and task progress.
Completing a parent task now withdraws pending/active browser permission and withholds late output.

Production browser composition remains disabled. Only an explicit fixture executor and fixture URL register
the service in these checks. No real website is visited by that executor, and no container, image pull, daemon
change, runtime installation, live model call, push or deployment is performed.

## Local verification

Final local checks passed:

- 56 focused browser/script lifecycle, resource-adapter and protocol facts: 8.028 seconds, no failures or skips.
- Six browser/script host facts: 3.304 seconds, no failures or skips.
- Browser owner journey and existing script owner journey in isolated Chrome, both on desktop and mobile.
- Existing company UI checks, TypeScript checking and frontend build. Both backend builds completed with zero
  warnings and errors. Scoped C# and frontend formatting checks and `git diff --check` passed.

Receipts are in `test-results/browser-product-*.log`, `test-results/browser-review-browser.json` and
`test-results/sandbox-browser.json`. The final browser journey includes the parent-task progress and plain-text
download assertions. Its first rerun stopped because the assertion expected “Running” instead of the existing
visible label “Working”; correcting that assertion produced a complete passing rerun. The lifecycle facts also
cover task completion withdrawing pending/active execution authority while withholding late captures.

The real UI journey uses fictional stored page data and an embedded JPEG from the earlier fictional Chrome
worker page. It covers a four-step exact itinerary, no pre-approval reservation/visit, lost approval response,
partial evidence, safe link rendering, screenshot display, downloads, failed cleanup holds, recovery while
paused/disabled, cancellation and withheld output, exact excerpt review after runtime disablement, lost artifact
reply recovery and explicit task completion. Mobile and desktop screenshots are saved as
`test-results/browser-review-mobile.png` and `test-results/browser-review-desktop.png`.

The final 390-pixel mobile and 1440-pixel desktop captures were visually inspected: dated source and text remain
readable, review/download controls fit, and there is no horizontal overflow. Browser controls are loaded in a
separate 16.12 kB JS chunk (5.83 kB gzip) and 0.86 kB CSS chunk. The 510.68 kB main JS bundle still exceeds the
existing 500 kB warning threshold; that threshold has not been increased. Disposable fixture profiles were
removed after the runs; the final process check found no owned test host or Chrome process. Disk space remained
above 315 GiB free.

This does not complete P3.1 or the overall product: production qualification/activation, retained-data deletion,
real-site usefulness and the remaining three-phase acceptance ledger are still open.
