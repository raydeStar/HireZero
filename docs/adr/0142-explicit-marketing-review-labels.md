# 0142 - Align Marketing's requested format with its draft checks

Status: accepted, 2026-10-27.

The starter-plan checker looks for named learning-goal, tally and decision-rule fields and a bounded list of open
decisions. The instructions previously requested their substance without requiring those labels, so valid prose
often produced manual-review notices.

New suggested assignments and Marketing's role instructions explicitly request `Learning goal:`, `Tally:` and
`Decision rule:` labels and an `Open decisions` bullet list, even for a single decision. If no consequential
decisions remain, the model should state `Open decisions: none` rather than invent one. The checker recognizes
that explicit declaration while continuing to count any actual listed decisions. Explicit owner format choices
take precedence over the role's default.

Existing saved results and requests are unchanged. Unrecognized prose remains a manual-review notice, not proof
that content is absent. Label and count checks do not establish semantic quality or grant approval. Changed role
instructions invalidate prior schedule authority through the existing instruction digest; re-review is required
before affected recurring work can run.
