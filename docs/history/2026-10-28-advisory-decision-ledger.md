# Durable advisory decision lifecycle

Status: core implementation and fictional checks; application provider binding and review remain pending.

[ADR 0150](../adr/0150-durable-reviewed-advisory-decisions.md) adds a company-owned decision record, exact owner
authorization, monthly/company reservations, a persistent one-shot dispatch claim, outcome accounting and
explicit reconciliation. Core remains provider-neutral. The configured preparation/current-packet callbacks
default to refusing access, and the host does not yet bind a live provider or expose a decision review flow.

Sixteen focused facts cover immutable packet/company boundaries, changed credentials, expiry and exact
consent, concurrent claims and command replay, complete advice without downstream work, retained partial
charges across month/restart, late advice, revocation before/after dispatch, overrun reconciliation,
cancellation, owner authority, invalid advice, incomplete usage, monthly/pause controls, provider exceptions
and canceled calls with late completion. Providers are in-process fictional callbacks.

The schema change appends a migration and preserves the earlier schema definition. Existing upgrade fixtures
that deliberately remove later migrations also need to remove the new table. The first usage regression found
that fixture omission; it was corrected in the affected fixtures rather than weakening production migration
checks. Seventy distinct selected facts pass with no skips: the sixteen new facts, related usage,
memory-analysis, audit and retirement checks, and the affected upgrade paths including the original
integration-path and schema-ledger adoption checks. Earlier schema checksums and existing records survive.
Two HTTP checks initially hit the sandbox's loopback restriction; both pass outside it against temporary
fictional servers. One document-retention fixture lives in a partial class with a different filename; its
correctly selected migration fact also passes. No PostgreSQL server was selected.

The company test project builds with no warnings or errors. The initial parallel build exited without
diagnostics; the serial build succeeded. CSharpier and web formatting completed. Existing production
databases were not migrated or used as test fixtures.

No live model call, OpenRouter/Jev request, runtime qualification job, credential change, application install
or scheduled run is part of this change. The installed applications and accepted weekly-content pilot are
unchanged. PostgreSQL execution and real provider accounting are not established by these SQLite checks.
