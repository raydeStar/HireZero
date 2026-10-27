# 0060: Structured organizational memory

Status: Accepted. Extends approved company context, bounded retrieval and recurring work.

## Decision

Keep concise decisions, goals, preferences, project notes, owner feedback and summaries of prior work as
role-neutral company records. Migration 18 adds the records and a company-scoped keyword index. The owner
chooses a company, department or single-employee audience. Source evidence is optional except for prior-work
records, which require an exact quote from an approved artifact and pin its version, digest, task and employee.
Listing approved work returns metadata; the full result is fetched only when selected for inspection.

A draft is excluded from retrieval. Owner approval pins the exact definition, citation, replacement and version.
Reviewed records are immutable. A revision is a new draft that pins the earlier record's version and digest.
Explicit `approve-and-replace` retires the earlier record and indexes its replacement in one transaction.
Ordinary approval cannot replace it. A stale replacement is refused until its draft is saved and reviewed again.
Only one approved record can have the same kind, title and audience. Earlier content remains available in history.

## Maintenance and provenance

An optional review date expires at 00:00 UTC. New dates must be in the future and within one year. Expiry,
inactive single-employee audiences and stale source evidence immediately exclude a record from new retrieval
and from dispatching prepared work, independently of background maintenance. The host examines approved
memory on startup and at most once per minute while running. It marks excluded records `needs_review`, removes
their index entries and emits one durable inbox alert. It never rewrites a statement or extends its authority.
Renewal requires a new draft and explicit replacement approval.

Cited artifacts must still be approved with the captured version and digest. Their originating tasks cannot be
canceled or superseded, and their captured document, memory, brief, handoff and selected-evidence context must
remain current. Existing context checks use a maximum lineage depth of 16. A replacement whose citation
depends on the record it would retire fails and rolls back the whole approval. Source quotes are historical
evidence, not independent verification, new instructions or execution permissions.

## Retrieval and execution

Normalized keyword retrieval considers at most 24 query terms, 30 ranked candidates and three current records.
Titles receive additional weight. The SQL query applies company and employee audience boundaries before
returning candidates. Company conversations and meetings include company-wide memory only. Each returned
record carries its exact identity, version, digest, approval date, review date and source quote if present.
Storage is limited to 200 retained records and 1 MiB of definition JSON per company; statement, rationale,
title and quote lengths have separate bounds. This is lexical retrieval, not semantic search or fact checking.

Task capture adds only relevant approved records. Existing measured request limits remain unchanged: optional
document excerpts and then lower-ranked memory are omitted when necessary, with an explicit notice. Owner
facts and selected evidence are retained. The optional context field is omitted when null, preserving old
packet serialization. Exact captured records are checked again before dispatch and before accepting results;
late stale output is withheld while actual usage is settled.

Recurring employee consent now expressly includes relevant approved documents and memory on each run.
Its authority fingerprint includes a versioned retrieval scope. Schedules approved before this change cannot
silently acquire memory access: the changed fingerprint requires a fresh draft and owner approval.

## User experience

The company Memory page supports draft recovery, exact lost-response receipts, approval, revisions, renewal,
revocation, citations to approved results and a no-model retrieval preview. Active records are the default;
retired versions have a separate history view. Today and inbox alerts open the exact record. A refreshed
record clears its approval checkbox, and an obsolete preview response cannot replace a newer query's results.
The measured-job review shows included memory before the complete serialized packet.

## Limits

Semantic contradiction detection, automatic proposals from feedback, deletion/retention controls, private
connector ingestion and measured retrieval quality remain separate work. Owner approval establishes an
authorized record, not objective truth. Real usefulness and customer retention need observed use.
