# Recurring measured deliverables — 2026-10-26

Implemented the first-accepted-result to recurring-work path under
[ADR 0093](../adr/0093-recurring-measured-deliverables.md). This is verified local progress against P2.1,
not completion of any of the three product phases. All phases remain in scope.

## Delivered

- Accepted measured results offer **Repeat this work**. The new draft copies the employee, skill, original
  instructions, selected approved references and prior allowance. The owner adapts the request and reviews its
  cadence, dates, finite count and current context. Unrelated unfinished schedule drafts remain intact.
- Recurring employee work can select a role-declared measured skill instead of always using the short chat
  policy. Marketing starter plans retain their deliverable instructions and output allowance. The finite
  approval binds the selected model route, instructions, policy and reference revisions.
- Approved result references appear in the review. Changed or revoked references stop future occurrences;
  other companies' results cannot be selected. Role actions and coding execution remain outside this path.
- Recurring deliverables produce review-required artifacts. Provider completion does not accept the result:
  artifact approval and task completion remain separate owner actions.
- All scheduled occurrences reject independent retry and reassignment. Uncertain work retains its usage hold
  and recovers through its original schedule without another dispatch.
- Repeated requests exclude previous conversation messages and implicit thread handoffs. A new two-occurrence
  test found that earlier employee replies leaked into the second deliverable despite a zero watermark;
  explicit schedule filtering now prevents that. Approved scoped documents and memory remain eligible, and
  coordinating roles retain their declared company overview.
- Saving a copied draft navigates to its exact saved schedule. Reloading does not recreate the copy. Edited
  drafts and uncertain-save receipts survive reload; read-only recovery finds the original save.
- Added nullable fields are omitted from old serialized inputs and contexts, preserving previous schedule
  approvals. Existing short updates, reviewed scripts and source checks remain supported.

## Verification

- 48 focused xUnit cases pass across schedules, scheduled scripts, first assignments and model connections.
  Five new deliverable facts cover two full results, independent acceptance, selected-reference revocation,
  tenant isolation, serialization compatibility, exact analysis-model choices and uncertain-outcome recovery.
  The initial run exposed the history leak above; the full 48-case set passes after the fix.
- One company UI contract test passes. Core, Host and Company test builds, TypeScript and Vite pass. Vite keeps
  its existing bundle-size advisory.
- The local HTTP/Chrome schedule journey creates and accepts a fictional first marketing plan, copies it into
  recurring work, preserves an unrelated draft, reloads edits, recovers a lost save reply, reviews the exact
  skill and allowance, runs twice, reviews the saved results, pauses/resumes and reaches finite completion.
  The same journey verifies recurring source checks, inbox navigation and no mobile horizontal overflow.
  Business identity, approved company context and the employee are seeded fixtures, not onboarding UI proof.
- The existing recurring-script Chrome regression also passes: two reports, separate acceptance, approved
  input replacement, cleanup recovery without replay and mobile layout. Both browser processes exit with code 0.
- Inspected desktop review and 390-pixel draft screenshots: fields, skill selection, prompt, model route and
  allowance are readable. Temporary browser profiles and databases are removed after owned processes exit.
- Compact ignored evidence: `test-results/recurring-deliverable-xunit.log`,
  `recurring-deliverable-browser.log`, `recurring-deliverable-script-regression.log`, `schedules-browser.json`,
  `recurring-script-browser.json`, `recurring-deliverable-draft-mobile.png` and
  `recurring-deliverable-review.png`.

## Remaining acceptance

The checks use fictional providers and data. They establish no real model quality, actual customer retention,
production runtime containment, hosted payment acceptance or cost-based launch pricing. No external provider
request, live model call, payment, container launch, runtime upgrade, push or deployment was performed.
