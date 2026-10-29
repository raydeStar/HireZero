# 0168 - Company writers queue for the database

Status: accepted, 2026-10-29. Follows [ADR 0167](0167-a-locked-database-skips-a-scheduler-round.md).

## Context

The owner's install keeps thirteen companies in one SQLite file, and the host runs a scheduler loop for each of them,
every second. Together they make about 28 short write transactions a second. On 2026-10-29 a write waited past
the 5-second busy timeout twice, which stopped the host. After ADR 0167, the same waits skipped rounds instead.

Each write transaction now records how long it waited for the write lock and how long it held it. A private
harness opened a copy of the owner's database the way the host does: one shared database object and a scheduler
per company, with no model and no notification sender. It ran each version for 90 seconds idle, then 90 seconds
alongside build-like load (32 busy threads and one thread flushing a scratch file):

| Version | Idle: longest wait | Idle: waits over 1 s | Idle: longest hold | Load: longest wait |
| --- | --- | --- | --- | --- |
| Before | 1,870 ms | 6 | 21 ms | 381 ms |
| Queue only | 1,424 ms | 3 | 165 ms | 176 ms |
| SQLite busy handler only | 662 ms | 0 | 21 ms | 49 ms |
| Both | 75 ms | 0 | 22 ms | 55 ms |

Each idle transaction held the lock for at most 21 ms, so the long waits came from how writers retried, not from
long work. The driver retries a locked statement blindly every 150 ms. With many writers, one of them can keep
missing the free moments. A COMMIT that must wait for readers sleeps the same 150 ms while holding the lock, and
everything behind it waits too.

## Decision

- **Busy handler:** every SQLite store connection sets `PRAGMA busy_timeout` to its busy timeout, still 5 seconds.
  SQLite's own handler then retries within milliseconds of a lock coming free.
- **Queue:** in one process, write transactions on the same database file take turns through a per-file queue
  before `BEGIN IMMEDIATE`.
  - Waiting past the busy timeout reports the database locked, as SQLite would, so ADR 0167 still skips the round.
  - A turn is handed back after every COMMIT or ROLLBACK, and after a failed `BEGIN`.
  - Other processes, such as backups, still meet SQLite's own lock.
- **Telemetry:** each write transaction records its wait and hold as `hirezero.store.write.wait` and
  `hirezero.store.write.hold`, tagged with its scope and outcome (committed, rolled back or busy), following
  [ADR 0024](0024-opentelemetry-traces-and-metrics.md).

## Consequences

- **Effect on the copy:** the longest wait fell from 1.9 seconds to 75 ms, and from 381 ms to 55 ms under load.
  Each figure comes from a single 90-second run on one machine.
- **Unchanged:** the busy timeout, the rollback journal mode, the database schema and PostgreSQL behaviour. The
  write-ahead log remains a separate decision.
- **Production evidence:** the installed host's "Skipped a scheduler round" warnings will show whether locks still
  happen. Thirteen scheduler loops on one file remain a design worth revisiting as companies grow.
