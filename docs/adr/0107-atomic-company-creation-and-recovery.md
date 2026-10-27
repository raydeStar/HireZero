# 0107 — Atomic company creation and read-only recovery

Status: accepted; local verification recorded separately.

The company creation route previously initialized a store before validating its name. Invalid input left an
unwanted company behind. A browser that lost the success response could also submit a second creation because
the route had no durable request identity.

Creation now validates its bounded name and UUID first. A rare directory write uses the database-wide writer
scope to check current source-company ownership and atomically commit the destination company, initial owner,
destination audit entries and receipt. The receipt uses the destination's existing operation table; there is
no schema migration and the source company's audit/operation records do not reveal another company's name.

The destination ID is derived from the authenticated subject and normalized creation UUID. Concurrent exact
requests converge on one company. Changed input with the same identity is refused. Repeated requests and
read-only recovery return the current destination name and membership; they never rename it, recreate members
or restore revoked access. Initialization of its runtime remains lazy and separate from the creation commit.

The browser stores the creation UUID and proposed name in tab session storage, scoped to the source company, before sending
the POST. Lost replies and server failures trigger a bounded receipt read. An unresolved request survives
reload and replaces the create action with **Check saved company**. That action only reads saved state. No
result, missing receipt or failed read automatically repeats creation. If the tab cannot save the request, no
creation is dispatched. A definitively refused client request permits correction and a new explicit submission.

If this server explicitly confirms that no creation receipt is recorded, the UI also offers **Finish this
creation**. This requires another user click and submits the original saved name and UUID. It safely completes
a request that never arrived, or returns the one saved company if the original arrives later. A generic 404
from an older/unknown server does not advertise this action. Revoked destination access returns a distinct
403; it never pretends the company was not created. Earlier tabs with only a saved UUID retain read recovery
but cannot resubmit missing original input.

Legacy callers without a UUID retain their existing one-request behavior, with validation and atomic creation
but no retry identity. The application UI always supplies a UUID. Tab storage is not cross-device recovery;
the existing company directory remains the way to find previously created companies from another tab/device.
Hosted authentication and live PostgreSQL acceptance remain separately required.
