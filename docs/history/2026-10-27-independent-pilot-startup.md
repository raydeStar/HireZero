# Independent local pilot startup

The accepted weekly plan and prepared recurring draft previously depended on a review process attached to the
development session. Registering a second workspace through the existing Windows helper would replace the
primary workspace's task names. [ADR 0143](../adr/0143-independent-local-startup-tasks.md) adds an optional task
prefix to registration, removal and update; existing default names remain compatible.

The pilot now uses a separate install, data root, listener, log folder and backup destination. A fresh verified
backup was restored into an empty data root. The original data is retained for recovery, and the old review
entry point forwards to the new startup task so it cannot silently reopen stale data.

Verification performed:

- Both modified PowerShell helpers parse successfully. The already-tested application was copied without a
  rebuild; all 181 installed application files match the release hashes.
- The new current-user tasks use limited privileges, hidden PowerShell windows, a sign-in startup trigger and
  a separate nightly backup. No credentials were placed in task definitions.
- A manual run of the real backup task succeeded; every manifest file's size and hash matched.
- The pilot was stopped and restarted. Windows stopping the launcher left its host child alive, so the private
  review helper now stops only the exact verified pilot binary after checking that model work is idle. The
  existing update helper already performs equivalent scoped child cleanup.
- Accepted artifact approval, completed task, earlier drafts, business context, measured jobs and the exact
  saved schedule survived migration and restart. The audit remains intact and unchanged.
- The primary workspace's process, startup/backup definitions, saved records and model connection stayed
  unchanged. Neither its process nor other Windows/WSL workloads were restarted.
- Ten completed native pilot calls remained recorded; the startup work started no model calls. At that point
  the proposed weekly schedule remained a draft with zero runs.

This verifies local process recovery and configured sign-in startup. It does not claim an actual reboot test,
availability while the computer is asleep or signed out, or successful recurring model execution. The concrete
two-occurrence proposal required separate owner activation approval. Owner usefulness acceptance of the first
editorial plan remains valid; general customer validation, commercial decisions and broader phase gates remain open.

## Owner-approved activation

The owner then explicitly approved the two private runs. Activation through the installed review page succeeded.
The saved definition, model authority and digest match the reviewed proposal exactly. Both occurrences remain:
November 2 and 9, 2026, at 09:00 America/Denver. The schedule is active, with zero occurrences consumed and no
immediate model request. The audit remains intact.

Each run uses Luna through Codex OAuth at high reasoning effort, with a 3,000-output-token target, a 90-second
local deadline and an intended allowance of one existing subscription credit. Limits are soft. The scope is
private content-plan drafting from the approved context and accepted example; it does not authorize publishing,
outreach, OpenRouter use, credit purchases or automatic retry. The computer must be awake and signed in.
Successful activation is not evidence that a future occurrence ran or produced useful work.
