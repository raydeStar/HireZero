# 0167 - A locked database skips a scheduler round

Status: accepted, 2026-10-29.

## Context

The company scheduler is a hosted background service. Every second it runs maintenance, schedules,
notifications and up to four employee ticks. The host stops when a background service throws, and the owner's
startup task does not restart a stopped host.

On 2026-10-29 the owner's company stopped for about an hour. At the start of an employee tick, `store.Claim` could
not take SQLite's single write lock within the 5-second busy timeout, and `BEGIN IMMEDIATE` failed with "database is
locked". The log does not record what held the lock. The exception reached the scheduler loop and stopped the
host.

A locked `BEGIN IMMEDIATE` or `COMMIT` leaves nothing half-done, because the transaction rolls back. After a claim
succeeds, `TickAsync` already keeps every fault inside its saved state, so an uncertain model call is never
replayed.

## Decision

- **Skip and retry:** when a round fails only because another writer held the database past its busy timeout, the
  scheduler skips the rest of that round and tries again one second later. The skipped round includes start-up
  recovery, which runs at every start and is therefore safe to repeat.
- **Recognising the error:** `StoreDatabase.IsBusy` decides which errors qualify. SQLite counts `SQLITE_BUSY` and
  `SQLITE_LOCKED`. Other databases count nothing yet, so they keep the previous behaviour.
- **Other faults:** any other error still stops the host, as before.
- **No overlap:** a round awaits all the ticks it started, even when one fails, so a skipped round never overlaps the
  next one.
- **Visibility:** each skipped round logs a warning in the company log, which shows how often the lock happens.
- **Busy timeout:** unchanged at 5 seconds. `SqliteStoreDatabase.BusyTimeoutSeconds` lets tests shorten it.

## Consequences

- **Repeated locks:** a long-held lock delays scheduled work rather than ending the company. While the lock lasts,
  a round is tried about every six seconds and logs a warning each time.
- **Unknown cause:** what held the lock on 2026-10-29 is still unknown. The warnings now record when it happens.
- **Not covered:**
  - other hosted services keep the stop-on-fault behaviour;
  - the owner's startup task still does not restart a stopped host;
  - PostgreSQL lock timeouts are not yet classified.
