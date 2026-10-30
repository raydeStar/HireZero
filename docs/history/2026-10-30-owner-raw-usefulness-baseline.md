# Owner assessment of the fresh Home batch

The owner reviewed the three untouched first outputs from the
[fresh-business Home evaluation](2026-10-29-fresh-home-usefulness.md) and supplied these
verdicts on October 30, 2026. This is the retained raw-usefulness baseline:

| Fictional case                    | Owner verdict               | What the verdict means                                                                                                                                                                                        |
| --------------------------------- | --------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Illustration studio welcome email | **Would use as-is**         | The tone, approximately 177-word body, grounded reply invitation, five-prompt template and concrete example were immediately useful.                                                                          |
| Bookshop weekly plan              | **Useful with small edits** | The practical guidance and 65-minute routine worked. Friday must stand alone, business connections should follow the goal rather than a one-post cap, and the attribution request needs a more natural voice. |
| Organizer service page            | **Needs edits / expansion** | The calm, grounded framing worked. The roughly 195-word body did not fulfill the requested 250–400 words or sufficiently explain each requested space.                                                        |

Counts: **one use-as-is, one use-with-small-edits, one needs-expansion**. These are owner
judgments of fictional first drafts, not three customer sessions or a population success rate.
The two drafts needing changes have not been edited. They must not be counted as successful
edited results, and a later comparison cannot replace these first-run verdicts.

The private `owner-baseline.json` in ignored `.pilot/fresh-start/` binds each judgment to the
original task, attempt, artifact ID and content digest. Each body was verified against its
saved raw file before recording the assessment. All three remain version-one model outputs;
no artifact approval, context promotion, scheduling or publication was performed.

## Instruction corrections

`StarterPlanInstructions` now requires every post to work for a reader who sees no other post.
It removes the direction to refer to an earlier tip and allows limited repeated context when
needed to make a post self-contained, while preserving distinct useful takeaways.

The one-post business-description limit is removed. Where the owner's goal calls for it,
each post can connect its useful advice naturally to supported business work. The connection
should vary with the reader's problem instead of repeating an introduction or disclaimer footer.
Off-channel attribution requests must sound like customer copy in the business's voice;
existing grounding and honest attribution limits remain in place.

`GoalDraftInstructions` now asks for a developed explanation and practical detail for every
requested topic. Explicit minimum/maximum word counts apply to the asset, excluding internal
notes; the model should check and expand thin sections before returning. Approximate lengths
still allow modest variation. Missing business facts cannot be filled with invented operations,
offers or commitments, and extra length should come from useful domain detail rather than filler.

These are instruction changes, not evidence that the next output will meet the requirements.
This cycle makes no new model requests and does not change model terms or the grading rules.

## Verification

- The Company test project and Release Marketing assembly built with zero warnings/errors.
- All 23 focused Marketing instruction, Home goal-preparation and assignment tests passed.
  These checks cover instruction constraints and application preparation, not model quality.
- CSharpier and frontend formatting passed. No frontend code or architecture decision changed.
- Both local installs were backed up and updated. Only the Marketing DLL and symbols changed;
  all 194 staged files were verified against each install.
- All eighteen companies retained employees, threads, tasks, artifacts, knowledge and measured
  attempts. Their audit chains are intact. The three assessed raw drafts remain unchanged.
- Saved schedules retain their approval terms, including the pilot's outdated v2/3,000 approval.
  No schedule was reapproved. Home remains Luna/high/v3/6,000 with a 90-second local deadline,
  and automatic execution remains disabled.
- No model calls, editorial copies, publishing or outreach were performed.

## Remaining acceptance

The studio email establishes one owner-accepted raw first draft in this batch. Consistent
first-draft usefulness is still unproven. Any future test of the revised instructions must
retain this baseline, use a separately authorized request and label the new output as a
comparison. Prospective-customer onboarding, useful recurring work and commercial acceptance
remain separate gates in the full product proposal.
