# Remove the duplicate current request from new context packets

The installed first-assignment walkthrough exposed a context-cost issue: the owner request appears both as
task input and as the newest history message. The inspected fresh-plan packet contains a duplicated 1,890-character
request. No further model call is needed to establish or remove that duplication.

[ADR 0138](../adr/0138-single-current-request-in-context.md) limits removal to an exact owner-message match at
the task watermark. It runs before the sixteen-message selection and changes no saved conversation, task input,
approved facts, evidence, feedback or existing prepared packet. This advances task-specific context and cost
management; it does not establish semantic retrieval quality or owner acceptance of the deliverable.

Two focused assertions reproduced the old duplicate behavior. The final assignment, revision and schedule
selection passes 33 xUnit facts. Four existing shared-path check groups also pass: conversation, measured queue,
security and task handoff. Coverage includes an empty first-assignment history, retention of sixteen earlier
messages including an identical request, exact packet persistence after restart/later messages, different text
at the watermark, matching employee text, context isolation, reviewed dispatch and recurring/revision behavior.

The build and required formatters pass. This pass makes zero live model calls. The character duplication is
observed, but no provider-token or billing saving is claimed. Private packets and compact check logs stay ignored.
The original first-draft result remains available for the owner's usefulness assessment.

The release is installed with separate main and pilot backups. All 181 installed files match the staged build;
main-workspace accounts and work are unchanged. After restarting the pilot, all tasks and artifacts, the previous
exact model call and its approval digest match the pre-update capture. Both audit chains remain intact, and the
pilot still has nine native completions. Existing reviewed context was preserved rather than recomputed.
