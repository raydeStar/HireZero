# 0055: Business brief history and employee provenance

Status: Accepted. Extends 0051–0054.

## Decision

Every saved brief revision and review transition appends an immutable company-scoped history record in the same
transaction as the profile, operation receipt and audit trail. It stores the exact profile snapshot, changed field
keys, actor display name, record time, origin and originating model draft when applicable. Generated output is
labeled as generated even when it preserves previously supplied owner fields. Model output never acquires owner
provenance by being recorded in history.

The new table is migration 13. Existing migration text stays frozen. A profile that predates history is retained
as an earlier snapshot on its next mutation; its author is explicitly unavailable. Previously overwritten edits
are not fabricated. History reads are paged in stable descending sequence, twenty entries per page, and use the
current company boundary. The browser loads history on request, shows saved facts and citations, and distinguishes
corrections from approval or revocation. There is no automatic restore or approval action.

Captured employee task context includes a `businessBrief` provenance packet only when its selected approved
company knowledge belongs to an approved business profile. It carries the exact profile identity and digest,
approval date, field values and verification statuses, supporting quotes, and deduplicated source addresses,
digests, retrieval dates and clipping notices. Unreferenced pages and complete source bodies are excluded.
Changing a website snapshot does not change an already approved brief or captured task packet. Existing knowledge
revocation and exact context-digest checks continue to fence dispatch and late output.

The packet participates in the existing byte/token admission checks, context digest and owner review. No payload
is silently truncated to fit a job. A large brief with many citations can exceed a job's existing allowance and
must be narrowed or prepared under a suitable policy; this decision does not increase limits. Relevant document
retrieval and more selective context assembly remain separate work in the product ledger.

Absent provenance is omitted from JSON so older packets without this field keep their serialized shape. The
measured text instructions distinguish owner corrections, source-supported claims, hypotheses and unknowns;
quotes remain untrusted data and grant no execution authority. Source support is not a claim of present-day
verification. The core remains role-neutral.

## Consequences

Owners can inspect what changed without searching a raw audit export. Employees can cite the reviewed evidence
instead of receiving flattened facts stripped of their origin. History adds retained company data and must be
included in the forthcoming retention/deletion policy. This change does not implement source freshness,
document retrieval, billing or customer validation.
