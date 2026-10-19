# 0025: One schema, two databases

Status: Accepted (Phase 3).

## Context

Company data lived in one SQLite file per data root. That suits a single machine. A hosted install wants a server
database it can back up, replicate and run beside the host. Schema changes were also made in two places: the company
store kept its own ledger (`company_schema_migrations`), and modules and integrations used `ModuleStorage`
([ADR 0018](0018-module-storage-migrations.md)).

## Decision

- **One way to talk to the database.** Core's `StoreDatabase` opens connections, starts write transactions and
  translates statements. Statements are written once, in the portable subset the schema already uses, with `$0`, `$1`
  … parameters. `StoreConnection` runs every statement through the database's translation.
- **SQLite is built in; PostgreSQL is an integration.**
  - `SqliteStoreDatabase` stays the default: `workspace.sqlite` in the data root.
  - `HireZero.Integrations.Postgres` (Npgsql) is used when `HIREZERO_DATABASE` holds a connection string. It
    translates the few differences:
    - parameters become named ones;
    - an autoincrement key becomes an identity;
    - a JSON field becomes a `jsonb` operator;
    - each payload table gains the `rowid` insertion order that SQLite keeps implicitly.
- **The same exclusion on both.**
  - On SQLite, a write transaction takes the single writer (`BEGIN IMMEDIATE`).
  - On PostgreSQL, it takes a transaction-scoped advisory lock on its scope: the company, or `schema` for
    migrations. Writes to one company exclude each other, and other companies are not held up.
  - Both give up after five seconds.
- **One migration framework.**
  - `SchemaMigrations` applies every owner's steps, the company's (owner `company`) and each module's, in order,
    once each, inside a write transaction.
  - The steps are recorded in `module_schema_migrations` with the checksum of their portable text, so the ledger
    reads the same on both databases. A changed step, a gap or an unknown version refuses startup.
  - A database from an earlier build has its `company_schema_migrations` rows adopted into the shared ledger,
    and that table is dropped.
- **Files stay in the data root.** The audit key, the scheduler lock and module or integration files that are
  per-machine (pinned runtime receipts, trusted PostgreSQL test profiles) stay on disk, whichever database holds the
  company.

## Consequences

The same company code and the same checks run on both databases. When `HIREZERO_TEST_POSTGRES` points at a disposable
server, the company scenario and the HTTP tenancy checks run against PostgreSQL, each test in its own schema. Routine
checks without it stay offline.

Running several hosts against one PostgreSQL database is safe for data integrity, because writers exclude each
other per company. Spreading one company's scheduler across hosts is not yet supported. The connection string is
read from the environment until secrets come from a vault.
