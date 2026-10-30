# Assess a useful draft without approving it for reuse

The product lets an owner read, copy and download a draft before approving shared reuse. The pilot
reporter still required a completed first task before accepting useful feedback. Because completion
requires artifact approval, that reporting rule imposed an unnecessary approval step on the pilot.

Owner Progress exports now include references to saved results of the first assignment: artifact ID,
content digest, provenance and creation time, in creation order. They contain no draft text. Non-owner
exports omit these references. The export reads existing records without changing artifacts, tasks,
approvals or knowledge; there is no new storage table or feedback prompt in the product.

Private observations can bind `firstArtifactId` and `firstArtifactDigest` to a saved result while its
task is still awaiting review. The reporter rejects mismatches, missing results and assessments dated
before the result existed. It separates exact-result assessments from legacy completed-task feedback.
A later or owner-authored copy cannot count as useful first saved model output; `useful-after-edits`
remains distinct. Legacy task-only exports retain compatibility but do not enter the new exact-result
counts. Completion and completed-task cost metrics remain unchanged.

The first saved model output is not automatically proof of a successful first request. Initial failed
or uncertain attempts still belong in the private acceptance record. Neither these references nor the
reporter authenticate human judgments or establish customer acceptance. Revision-task results remain
separate from the original assignment's first-result assessment.

## Verification and installation

- Four focused .NET tests pass. Coverage includes empty results before execution, an unapproved model
  result, a later fictional editorial copy, preserved creation order and references after approval,
  tenant isolation, owner-only access and no draft text in exports.
- Eight Node reporter tests pass, including useful feedback with zero completions, wrong result/digest,
  premature observations, missing results, edited-copy separation, private output and legacy compatibility.
- The focused build, release Core build and repository formatters pass. No UI behavior changed, so no
  additional browser walkthrough or full regression rerun was needed.
- Both local installations received only the updated Core assembly and symbols, after data and binary
  backups. All 185 staged files were verified; all fifteen companies' saved work, schedules and audit
  chains were preserved. The temporary stage was removed after verification; backups remain private.
- Installed API checks matched twelve exact saved references, eleven still unapproved. They exported
  no draft text and recorded no usefulness judgments. Automatic execution and the old schedule's approval
  remain unchanged.

This cycle made no model requests. The prepared three-business evaluation still awaits its batch
authorization. This reporting correction does not satisfy that raw-output or owner-acceptance gate.
