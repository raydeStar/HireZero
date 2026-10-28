# Durable local runtime recovery

The prior website-onboarding attempt exposed a launcher path into a desktop package directory that had been removed by an update. Windows deployment events confirm successful removal before that attempt. The retained-package location recorded by Windows no longer contains the original executable, so the previous pin could not be restored from it.

[ADR 0155](../adr/0155-durable-local-codex-runtime.md) records the reviewed replacement. The new official binary is copied to a versioned application-owned directory and checked against the repository pin. Both local startup definitions now reference that durable copy and its exact bundle identity.

Evidence:

- Read-only native inspection passed on the official package binary and again on the standalone copy, with the existing dedicated sign-in, exact Luna route and isolated ephemeral thread. No model turn was requested.
- The matching experimental protocol schema contains all ten turn-start fields used by the text adapter and all four required usage fields. Completion events retain the expected turn object.
- The native adapter and measured-queue regression groups passed. The initial queue check was blocked by the tool sandbox's loopback restriction; it passed with local test access. Both groups use fictional providers.
- Test and release builds passed. The pin update changes no package dependencies or lockfiles.
- Both app releases were backed up and replaced; all 180 installed files matched. Both original audit chains remained intact. The second company retained its imported sources, manual draft and uncertain attempt, with its own intact audit chain.
- The primary update's first stop check observed a listener still closing and stopped before backup or app replacement. Subsequent process inspection proved it had exited. The same update resumed from that saved checkpoint; completed pilot steps were not repeated.
- Read-only runtime inspection through both installed hosts now succeeds. Existing accounts and routes are retained, and automatic model, coding, script and browser execution remain disabled.

No inference, publishing, purchases or recurring occurrences occurred during recovery. The accepted plan and exact two-run schedule remain unchanged. Runtime readiness is restored at the preflight level; live delivery still needs its own evidence.

Remaining: resolve the historical uncertain attempt using a supported evidence type without inventing a provider receipt; re-review the schedule against the changed runtime binding; produce and review a real generated sourced brief. All three product phases and their customer-validation gates remain open where unproven.
