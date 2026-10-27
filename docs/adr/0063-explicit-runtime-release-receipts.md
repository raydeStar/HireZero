# 0063: Explicit runtime release receipts

Status: Accepted. Extends the reviewed local coding lifecycle in ADRs 0012–0014.

## Problem

The local coding runner swallowed runtime disposal and temporary-file cleanup failures, then unconditionally
reported termination. A completed model reply was therefore able to hide an unconfirmed stop. Scratch files
could also remain without a durable indication to the owner.

## Decision

Process termination and temporary-file removal are separate observations. The integration returns one of:

- `released`: the owned app-server stop completed and the run's temporary directories were removed;
- `termination-unconfirmed`: stop failed; no scratch cleanup is attempted;
- `cleanup-scope-refused`: the process stopped, but the proposed cleanup includes an unexpected path, file,
  link or junction;
- `cleanup-incomplete`: the process stopped, but filesystem cleanup failed.

Only a successful stop sets the coding lifecycle's termination flag. Unconfirmed termination continues through
the existing unknown-outcome handling, preserving reservations and checkout claims for reconciliation. An
uncertain preflight stop is not reported as a safely unsent attempt. Cleanup failure alone does not discard a
valid reviewed output or pretend that the runtime is still running.

The Developer bridge records the receipt after disposing its runner. It persists on the company-scoped
execution grant and emits an audit/feed event. Receipts are immutable; a later conflicting observation cannot
silently replace one. Older grants omit the nullable field and acquire no invented cleanup evidence.
The execution view explains released, retained-file and unconfirmed-stop states separately. Attention states
also create a durable inbox notice linking to the task. A retained resource's generated directory identifier
lets the operator find the run under the configured scratch root without exposing an absolute host path.

## Cleanup scope

Cleanup verifies the absolute parent and exact generated `coding-<32 hex digits>` directory name. It may
remove that directory and its sibling `-inputs` directory only. Both targets, their ancestors and their
contents are checked before either deletion begins. Links and junctions cause refusal rather than traversal.
The inspection pass stops after 50,000 entries or three seconds; an oversized tree remains for operator cleanup.
The approved checkout, durable result, sign-in home, toolchain folders and unrelated scratch runs are kept.

Scratch preparation is inside the same failure-handling scope as runtime preflight, so a setup failure is
also released or reported. A runner instance accepts only one start, including concurrent start attempts.
Cleanup diagnostics contain fixed status codes, not arbitrary exception text or credential-bearing paths.

## Limits

The host supplies its local coding factory only to the original local company, matching the machine-tool
boundary in ADR 0062. Capabilities, integration status and execution requests enforce the same rule, even when
a factory is qualified. Other local companies and all hosted companies require an isolated execution provider;
qualification of the operator's runtime does not authorize a tenant to borrow it.

These receipts cover the existing local pinned app-server adapter. Its current process-tree kill mechanism
is retained; this does not prove a new OS/container isolation boundary or a hosted worker lifecycle. A
separate provisioned sandbox, crash/orphan recovery, operator cleanup workflow, temporary storage quotas,
compute metering and premium environments remain in the full three-phase plan. Retained files are not
deleted automatically after an uncertain stop, and a failed task is never replayed by cleanup.
