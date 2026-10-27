# ADR 0123: Drive links preserve exact file identities

Status: accepted, 2026-10-26. Extends ADR 0090's selected-file setup.

## Context

Drive setup required extracting opaque IDs from document links. The generic settings writer also removed
duplicate list values without considering case, contradicting the Drive adapter's exact case-sensitive allowlist.
Two distinct owner-selected IDs could therefore silently become one saved entry.

## Decision

Let an integration field declare an optional deterministic normalizer and case-sensitive list identity.
Validate bounded plain-text inputs before normalization, validate the resulting value, then deduplicate using
the declared comparison. Other integration fields retain their existing case-insensitive behavior by default.

The Drive field accepts raw file IDs or HTTPS links on exactly `docs.google.com` and `drive.google.com` using
the supported document/file routes or Drive's `open?id=` route. Save only the extracted ID with its original
letter case. Different link forms for the same exact ID collapse to one entry. Credentials in URLs, nondefault
ports, other origins, folders, unsupported routes and ambiguous query IDs are refused. Query/fragment metadata
does not become access authority and is not saved. Parsing a link never performs a network request.

Keep the 20-file limit, dedicated service-account setup, exact allowlist, fixed Google API endpoints, supported
file-type/download checks and separate owner preview, save and approval. No folder traversal or sharing change
is introduced. Existing raw-ID settings remain valid. A failed settings change leaves the saved selection intact.

Google documents that file links derive from a file ID and that access is separately checked against the file's
permissions ([Drive sharing guide](https://developers.google.com/workspace/drive/api/guides/manage-sharing)).
This implementation recognizes the stated link forms; it does not claim to accept every Google URL format.

Delegated OAuth/Picker onboarding and live Google acceptance remain separate work. This change reduces manual
selection friction and corrects stored identity handling without changing provider authorization.
