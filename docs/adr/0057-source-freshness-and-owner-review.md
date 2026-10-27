# 0057: Source freshness and explicit owner assessments

Status: Accepted. Extends 0051, 0054 and 0055.

## Decision

Approved business facts are immutable until the owner explicitly revokes and replaces their brief. Public page
changes create review evidence, never automatic corrections. An owner can authorize one refresh of one to three
sources already cited by the current approved brief. Reads use the existing public HTTPS, same-origin, robots,
pacing, address, size and redirect protections. The host imposes a shared 60-second deadline; each company may
start three refreshes per hour and only one may be running. No model or search API is called.

Migration 15 adds company-scoped refresh runs, immutable source checks and immutable owner assessments. The
existing operation receipt makes an exact repeated command return its original receipt without another read.
Completion checks the initiating owner's current membership and the exact approved profile/business revision.
Revoked authority or a changed brief withholds results. Interrupted runs become unknown on recovery and are never
replayed. Source snapshots saved before interruption remain unapproved evidence.

A check has its own time, independent of the original source snapshot's retrieval time. Identical content reuses
the existing snapshot rather than inventing a new discovery date. Changed content saves a separate snapshot.
Each source check records unchanged, changed or unavailable. Text differences mean a possible change, not a
proven factual conflict. The UI compares old and new snapshots and reports whether the exact original quote is
still present; clipping, paraphrases and irrelevant page changes can require human judgment.

## Owner review and execution

The owner can assess a field as verified, possible, conflicting or outdated, with a bounded explanation. Review
binds the exact approved profile, current checked evidence and previous owner assessment. Verification also
requires an exact supporting quote from a current checked snapshot for that field. It does not rewrite the
approved wording or upgrade its original sourced/hypothesis/owner/unknown classification.

Conflicting and outdated assessments pause work using the whole brief until the owner resolves the assessment
or explicitly replaces the brief. Later source reads cannot clear these assessments. Verification survives a
new check of identical evidence; changed or unavailable evidence returns it to an uncertain state. Explicitly
resolving a critical assessment requires a new owner explanation; it is never a network side effect.

New task packets carry a compact freshness digest, affected field states, owner notes and check dates alongside
the original provenance. They carry no full page bodies or refresh history. Existing byte/token admission applies.
Dispatch, continuation and result admission require the captured freshness to remain current. A subsequent
check or assessment invalidates an already-prepared packet; an in-flight late result is withheld while reported
usage still settles. This deliberately includes a new check date, even when the page text is identical, because
the reviewed packet's dated evidence changed. Briefs without checks or assessments omit the optional property,
preserving older context serialization and digests.

## Experience and limits

The brief page offers bounded source selection, dated comparisons, field assessments and correction. Receipt
recovery is company/profile-scoped and survives reload. A running refresh is polled for status only. Unsaved
brief edits survive source checks. Changed, unavailable or critically assessed evidence appears in Today with a
link back to Business brief. Replacing the brief still requires explicit revocation followed by exact approval.

This is owner-started maintenance, not a recurring scheduler, semantic change detector or independent fact
verification service. Checks cover retained bounded page text; they cannot prove an entire website is unchanged.
Old snapshots, assessments, receipts and task contexts are retained. Permanent deletion and retention controls
remain separate work. Real-site quality and recurring customer usefulness require separate field validation.
