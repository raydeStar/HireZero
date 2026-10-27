# 0136 - Local draft checks and reviewed repair

Status: accepted, 2026-10-27.

The real starter-plan revision required several reviews to catch missing constraints and audience drift.
Simple counting should be visible before result approval without paying for another model to do it.

Roles may contribute a pure draft checker through an explicit `draft-checks:<skill>` UI slot. The generic
workspace renders its findings; Marketing owns the starter-plan rules. No role-specific policy enters core.
The checker counts visible whitespace-separated words, the 500-word starter-plan ceiling, recognized day-labeled
post sections, literal numeric post-word ranges from the original request and ordered feedback, a recognized
unknowns list, and labels for the channel hypothesis, preparation, learning goal, tally and decision rule.
The latest recognized post range applies. Missing ancestry and unrecognized formats require manual review.

These are advisory format checks, not semantic evaluation or a complete natural-language requirements parser.
The default ceiling and template labels do not prove compliance with every custom instruction. Checks do not
establish accuracy, audience fit, complete assets, usefulness or approval. All current result controls remain
available. Historical outputs retain their exact text and receipts; the UI checks the displayed copy locally.

Definite count violations can fill an editable change-request draft only after an explicit button click.
Existing unfinished feedback is preserved and survives reload. Creating the revision, reviewing its context and
authorizing one run remain separate actions. Earlier feedback is retained under ADR 0135. This introduces no
automatic repair, model call, replay or repeating loop. Unrecognized wording is flagged for review, not fed into
a proposed repair as a definite defect.
