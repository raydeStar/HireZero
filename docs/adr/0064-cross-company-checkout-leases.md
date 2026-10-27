# 0064: Cross-company checkout leases

Status: Accepted. Extends the checkout claim in ADR 0012 and the local execution boundary in ADR 0063.

## Problem

Checkout claims were keyed by company and canonical path. Two companies in the same database could each
claim the same physical checkout. Restricting the current local coding adapter to its original company
prevents that adapter from lending the machine, but the shared store must also reject conflicting claims.

## Decision

Migration 20 adds a resource lease keyed by the canonical checkout digest across the database. Acquisition
inserts without overwriting, then verifies that the lease belongs to this company and attempt. The database's
unique key arbitrates concurrent inserts, including companies whose PostgreSQL transaction locks differ.
The company-scoped claim and global lease commit together; any conflict rolls back the acquisition.

Only confirmed settlement or a known unsent attempt releases its lease, in the same transaction that releases
the company claim. Unknown outcomes and restart recovery retain the lease. Repeating acquisition for the same
attempt is idempotent. Existing canonical-path and nested-checkout rules remain in force.

Pre-migration claims still block other attempts. They are consulted during acquisition, even before their
owner has adopted a new lease. Conflicting legacy claims are preserved and neither claimant may acquire a
lease until the conflict is reconciled; migration never selects a winner or discards uncertain work.

The only information returned across companies is that a checkout is reserved. Other companies' identifiers,
tasks, employees and lease details remain private. There is no user API for deleting another company's lease.

## Limits

This coordinates claims within one application database. Separate installations/databases must not be pointed
at the same physical checkout. Future distributed execution providers need a provider/worker-aware resource
identity and their own containment. This is a concurrency boundary, not filesystem access authorization;
company runtime/tool restrictions and exact owner approval still apply. Unknown claims have no automatic
timeout or stealing policy. Operator reconciliation remains separate work.
