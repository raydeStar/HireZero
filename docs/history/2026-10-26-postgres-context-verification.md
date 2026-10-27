# PostgreSQL context and deletion verification — 2026-10-26

The cross-phase audit had skipped the four PostgreSQL facts because no disposable server was configured.
This session ran them against real PostgreSQL and added one focused context/deletion regression. All fixtures
are fictional; no live model, customer database, hosted service or application execution runtime was used.

## Test environment

The isolated loopback server used PostgreSQL 17.6 from the repository's existing
`@embedded-postgres/windows-x64` pin, `17.6.0-beta.15`. Its registry SHA-512 integrity was checked before
extraction; archive paths and entry types were validated, and package installation scripts were not run.
The server used a fresh cluster, random port and random password with SCRAM authentication. Each PostgreSQL
fact used its own schema. No system service or existing Docker/WSL workload was changed.

## Verification

- The company test project built with zero warnings and errors.
- `StoreDatabaseTests`: **7 passed, 0 failed, 0 skipped**, in 28.210 seconds. This comprises two existing
  offline/SQLite facts, four previously skipped PostgreSQL facts and the new PostgreSQL context fact.
- The existing PostgreSQL company lifecycle fact passed its **47 embedded checks** for identity, isolation,
  concurrency, fencing, budgets, coordination and meetings. These are included in the seven facts, not an
  additional count of tests.
- HTTP company selection and access refusal, identical migration ledgers, isolated company membership,
  audit-chain tamper detection and password rotation without a host restart passed on PostgreSQL.
- The new fact covers approved document/memory retrieval, exact historical task packets, revocation,
  cross-company deletion refusal, retired-source erasure, scrubbed old command responses, replay prevention,
  stale task refusal and preserved neighboring-company records/audit chains.

The server exited after a targeted fast shutdown, and the disposable cluster was removed only after exit.
Compact private pin, run and cleanup receipts and logs remain in ignored `test-results/postgres-qualification/`.
The downloaded archive and extracted test binaries were removed after process-exit verification.

This closes the observed skipped-database verification gap for these flows and strengthens P1.6, P2.4,
P3.5 and X2/X6. It does not establish hosted deployment acceptance, full-company or backup erasure, runtime
reliability, real provider acceptance or customer usefulness.
