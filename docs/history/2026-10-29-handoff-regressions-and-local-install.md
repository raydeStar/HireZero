# Handoff regressions and local installation

The handoff supplied the thread-start effort fix (`1b80a69`) and the versioned 6,000-token
deliverable policy (`2f8091e`). This cycle repaired the reported regressions, exercised the
drafting journey, and installed those changes locally with backups. No new architecture decision
or live model evaluation was needed.

## Repairs

- Small revisions had outgrown the existing 4 KiB application packet. Shortened repetitive revision
  instructions while retaining original deliverables and constraints, explicit overrides, earlier
  feedback, complete replacement output, and the distinction between owner direction and approved
  company facts. The original packet limit remains unchanged. Existing revision tests now explicitly
  assert that the small-job policy and its byte limit are preserved.
- Four migration fixtures recreated an earlier schema incorrectly after the decision ledger was added.
  They now remove later tables and migration entries together before testing forward migration.
  Production migrations and checksum validation are unchanged.
- Updated the Marketing learning-signal assertion and browser fixtures for current prompts,
  deduplicated business-brief context, grouped Settings, collapsed editors, draft labels and the
  longer advisory word guide. These retain the behavioral checks instead of restoring obsolete UI copy.
- The walkthrough exposed a real mobile navigation bug: only the top navigation row scrolled its
  selected tab into view. Company Knowledge and Advanced Settings now do the same on section changes,
  resizing and reload. The browser check verifies that the selected Advanced tab remains visible.

## Verification

The initial five .NET suites contained 884 tests: 866 passed, seven failed, and eleven intentionally
skipped environment-dependent checks. All seven failures were reproduced and fixed. Focused reruns
passed all 29 affected Company tests and all 44 Browser/Sandbox integration tests. Core (43), Host (13)
and Policy (one) had already passed. This is baseline coverage plus focused reruns, not a claim that
the entire suite was rerun after every edit.

All 48 Node tests passed across the initial suite and a separate rerun of the browser-driver test,
whose first attempt could not launch Chrome in the restricted shell. The production web build and
repository formatters passed. Vite still reports its existing large-chunk advisory.

The measured, schedules and business-drafting browser smokes passed with fictional providers. The
drafting walkthrough covers single-step Home consent, lost-reply reconciliation without duplicate
work, copy/download, owner review and completion, saved editor recovery, chained feedback, the
high-effort/6,000-token review, and desktop/mobile navigation. It uses synthetic owner-edited artifacts
to test the editor; these are not model-quality acceptance evidence. Failure-state capture now makes
an early missing-draft failure diagnosable without another speculative run.

Both local applications were updated after data and binary backups. Each installation's 162 files
matched the staged build. Saved employees, threads, tasks, artifacts, knowledge and measured receipts
were compared across every company; audit chains remained intact. The installed Home terms return
Luna, high effort, policy v3, a 6,000-token output target and the existing 90-second local deadline.
Automatic model, coding, sandbox and browser execution remain disabled.

The primary workspace has no schedules. The pilot's saved active schedule remains v2/3,000 with zero
consumed occurrences and an outdated approval. It was not upgraded or re-approved. The two canceled
schedule records are also preserved. Private backup paths, hashes and receipts remain outside Git.

## Evaluation interpretation and accounting

The [runtime investigation](2026-10-28-runtime-ignored-turn-effort.md) changes how earlier comparisons
must be read: the requested turn-only effort was not applied. The Sol comparison note now explicitly
records its actual default-low effort rather than describing it as a high-effort comparison.

The handoff's three separately approved Luna diagnostics used their own grants. Two completed,
reporting 15,833 input/output tokens combined. The withheld request last reported 9,344 tokens;
its final usage and charge remain unknown. Its later v3 comparison had separate approval and was
not an automatic replay. Those outputs are unchanged, and the broader Luna allowance remains
21 of 31 consumed, ten remaining under its recorded terms. No additional evaluations occurred here.

The two completed confirmed-high samples still needed edits by the handoff's assessment. The larger
output target provides room for reasoning; it does not prove useful output or guarantee completion.
The shortened revision instructions have offline packet and feedback-chain coverage but no new live
quality comparison in this cycle. Repeatable raw first-draft usefulness and owner acceptance remain
the next product gate; successful installation and synthetic workflows do not close it.
