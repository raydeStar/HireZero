# 0018: Module storage migrations

Status: Accepted (Phase 2B.5j).

## Context

The company schema is versioned: `company_schema_migrations` records each step with its checksum, and a changed,
missing or newer step refuses startup. Modules and integrations that keep their own tables did not follow that rule.
They ran `CREATE TABLE IF NOT EXISTS` on every start, so a store could run on a table some other build had shaped.
Coding runs are the first example: their table lives in the company database and in the steering runs database.

## Decision

- **One runner for owned storage.** `ModuleStorage.Migrate(connection, owner, steps)` applies an owner's ordered SQL
  steps once each, inside one transaction, and records them in `module_schema_migrations(owner, version, checksum)`.
- **The company schema's rules apply.** Startup is refused, with nothing started, when:
  - a recorded step's text changed;
  - the recorded versions have a gap;
  - the database holds a version this build does not know.
- **Adopting older tables.** A first step written as `CREATE TABLE IF NOT EXISTS` adopts a table that an older build
  created without a record.
- **Owners are independent.** Each owner (for example `coding-runs`) versions only its own tables. The company schema
  keeps its own table and steps.

## Consequences

A module or integration that adds a table declares it as a step and never edits a recorded step; a change is a new
step. Legacy stores keep their current behaviour until they are ported or retired with the legacy profiles.
