# Three fresh businesses through Home

The owner approved three additional native Luna requests after reviewing the prepared
fictional cases. All three ran once through the installed Home form: business description,
goal, one consent checkbox and Generate. No setup adjustment, second run button, previous
draft, model retry or assistant editorial copy was used. This is a fresh first-run sample,
not a revision of the earlier evaluation businesses.

## Raw results

These are assistant assessments, not owner acceptance or customer evidence.

| Case                           | Requested work                                                                               | Assessment of the first raw result                                                                                                                                                                                                                                                                                                                                                                                                                     |
| ------------------------------ | -------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| Three-person bookshop          | Three Facebook gift-selection posts with a posting/reply routine and learning signal         | Useful with a small edit. The children's-book checklist and mystery-gift example give practical advice without inventing stock. The shop post uses the supported in-person route and asks visitors to mention Facebook. The 65-minute routine includes posting, replies and review, and attribution stays at channel level. Friday refers back to Monday, weakening it as a standalone post. The offer post is less distinctive than the advice posts. |
| Solo home organizer            | A 250–400-word service page explaining kitchens and entryways with an enquiry-form next step | A grounded starting point, not fully finished. Concrete everyday problems help a parent choose where to start. The calm copy preserves the supplied services and route without inventing packages, prices or a session process. At about 202 words including its headline, it falls short of the requested scope and could explain the two spaces more fully. Length alone is not the usefulness verdict.                                              |
| Two-person illustration studio | Welcome email, subject, preview, useful cover-mood/story template and reply invitation       | Strongest of the three; suitable for owner review as-is. The five prompts plus a concrete fictional example make the email useful before buying. It connects naturally to the studio's work and supported reply route. It adds no newsletter cadence, free service, delivery promise or customer result. The body is about 177 words for an approximately 200-word request.                                                                            |

Each company has exactly one task, measured attempt and version-one model artifact. Raw bodies
and digests were checked again after the frontend update; all were unchanged. No result was
approved, promoted to shared knowledge, scheduled or published. Original inputs, raw outputs,
packets, identifiers and authorization receipts remain in ignored `.pilot/fresh-start/`.

## Experience and usage

- Each completed draft opened automatically, with Copy, Download and Refine available and
  internal notes, provenance, execution receipts and costs collapsed.
- The first completed draft was observed within approximately 46, 30 and 22 seconds of Generate,
  respectively. These are observation intervals including polling/tool delay, not precise model
  latency or a measured customer onboarding time.
- The bookshop Markdown download contains the usable plan and excludes internal notes. Refine
  opens in one click and was closed without saving a request. Copy showed success, but the
  automation browser's virtual clipboard reader was empty; native clipboard contents were not
  independently verified. The download itself was verified on disk.
- Every run used Codex OAuth, Luna, high effort, v3, a 6,000-token output target, a 90-second
  local deadline and one intended existing subscription credit. These remain soft limits.

| Case      | Reported input tokens | Reported output tokens | Reported reasoning tokens |
| --------- | --------------------: | ---------------------: | ------------------------: |
| Bookshop  |                 6,481 |                  3,376 |                     2,634 |
| Organizer |                 5,943 |                  1,174 |                       823 |
| Studio    |                 5,950 |                    617 |                       272 |

Reported input plus output totals **23,541 tokens**. Reasoning is displayed separately and is
not added again. Actual subscription-credit charges are unavailable. All three authorized
requests are consumed; no further calls were made.

## Presentation correction

The live service-page walkthrough exposed a real rendering issue: the app-wide paragraph reset
left draft paragraphs with zero margins, making separate thoughts look like one dense block.
Draft-level paragraph, list and quotation spacing now overrides that reset. Saved text, copy
and download contents are unchanged.

The frontend build and both required formatters passed. The installed email has approximately
12-pixel paragraph separation; desktop and 390-pixel mobile inspection showed no horizontal
page overflow. Both web frontends were backed up and updated, with all sixteen build files
verified per install and both health endpoints healthy. No server restart or data change was
needed for this CSS update. No new architecture decision or broad regression rerun was needed.

## Follow-up: copy one post

The raw bookshop plan used standalone dates, not Markdown headings, for its three posts.
Only the parent group initially had a copy button. The renderer now recognizes repeated
full English weekday/month/day labels when they begin a section and offers a copy action
for each dated item. One isolated date, dates after introductory prose, quoted text and
fenced examples do not become inferred sections. Whole-section copying remains available.

The existing focused section-copy tests now cover dated items, exact body preservation,
nested checklists, emphasis, year-bearing dates, CRLF input and exclusion of later posts
and owner notes. The tests, frontend build and formatters pass. The original installed
bookshop draft shows all three new controls and reports success when copying Monday;
native clipboard readback retains the tooling limitation described above. Desktop and
390-pixel mobile views were inspected without horizontal overflow. Both frontends were
backed up and updated with sixteen file hashes verified per install, without restart.
This follow-up made no model requests or saved-draft edits.

## Next gate

The [owner's October 30 assessment](2026-10-30-owner-raw-usefulness-baseline.md) now records
one use-as-is, one useful with small edits and one needing expansion against these exact raw drafts.
The email demonstrates an accepted immediate-use result; the service page demonstrates the remaining completeness gap.
Three fictional examples do not establish consistent usefulness, prospective-customer onboarding,
repeat use, or completion of the wider three-phase product goal. Keep any later revisions or
comparisons separate from this evidence.
