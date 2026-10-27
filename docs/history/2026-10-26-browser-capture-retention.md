# Browser capture retention — 2026-10-26

Implemented [ADR 0080](../adr/0080-browser-capture-retention.md): owner deletion, optional company expiry after
confirmed resource cleanup, current-record/operation-response erasure, retained exact excerpt proofs, original
revocation checks and explicit deletion/retention UI. Production execution remains disabled.

Local verification passed:

- 44 focused browser/script lifecycle and excerpt facts, including seven new deletion/retention facts:
  12.474 seconds, zero failures or skips.
- Seven browser/script host facts, including expiry in unopened companies with no execution runtime:
  4.871 seconds, zero failures or skips.
- Nine migration facts and three affected legacy/integration facts: 1.786 and 10.143 seconds, all passed.
- The extended fictional desktop/mobile browser journey: explicit deletion consent, lost deletion reply recovery,
  reload persistence, approved excerpt navigation and owner retention settings, alongside the existing itinerary,
  capture, cancellation, cleanup, download and review checks. The existing script journey also passed after the
  shared checkbox layout change.
- Company UI checks, scoped C# formatting (23 files), frontend formatting, TypeScript/frontend build and
  `git diff --check`. Both backend builds completed with zero warnings and errors.

Receipts are saved in `test-results/browser-retention-*.log`, `test-results/browser-review-browser.json` and
`test-results/sandbox-browser.json`. Retention is opt-in, starts from confirmed cleanup and removes at most 20
jobs per sweep. The batch test verifies that disabling expiry preserves jobs not yet erased. A concurrent
excerpt/deletion test verifies that a saved excerpt receives a valid proof or preparation is refused. Revocation
and forged provenance remain rejected after raw capture deletion. Old operation results are scrubbed without
removing replay fingerprints. The queries use the existing shared SQL subset; this was not a live PostgreSQL run.

Final 390-pixel screenshots (`browser-retention-mobile.png` and `browser-retention-settings-mobile.png` in
`test-results`) were visually inspected. The first pass exposed a wrapped consent checkbox and touching result
buttons; both were corrected and the journey rerun. Labels, deletion status and retained-result links now fit
without horizontal overflow. The browser chunk is 18.07 kB (6.29 kB gzip); the main JS bundle is 513.03 kB
(149.88 kB gzip). The existing 500 kB warning remains; its threshold was not increased.

The final process check found no owned test host or Chrome process. Disposable fixture profiles were removed;
an unrelated pre-existing demonstration profile was left intact. Free disk space remained above 315 GiB.

This covers current raw browser output. Selected results, historical task copies, itineraries, audit/usage records,
downloads, recovery files and backups have separate retention requirements. No real sites, models, containers,
image builds, runtime installation, publication or deployment are involved in these checks.
