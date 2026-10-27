# 0080 — Browser capture retention

Status: accepted; local verification recorded in the dated history note. Date: 2026-10-26.

## Decision

Browser page bodies, screenshots and diagnostic output do not need to remain indefinitely merely because a
selected excerpt became a task result. Provide distinct controls for deleting raw captures and revoking evidence.

- Owners can delete one exact saved capture result only after its original resources are confirmed released and
  its allocation is settled. The command binds the current job version and result digest, and requires explicit
  deletion consent. It works when the execution runtime is disabled or absent.
- A separate company policy keeps captures until manual deletion by default, or deletes them 7, 30 or 90 days
  after confirmed cleanup. Changing the policy requires exact owner review and starts deletion of already expired
  captures. Each sweep removes at most 20 jobs, oldest cleanup first. Eligibility reads only IDs and timestamps;
  it does not deserialize every retained screenshot. The existing all-company scheduler checks retention every
  minute while the host is running, including when models or browser execution are unavailable. Unresolved jobs
  are never expired. Turning expiry off preserves captures not yet removed by a batch.
- Erasure removes the complete browser result and execution reason from current job records and earlier saved
  command responses. Operation IDs and fingerprints remain, preventing replay from restoring deleted data or
  authorizing another visit. Settled-job result recording and recovery also cannot rehydrate captures.
- Before erasure, preserve bounded proofs for already saved, currently valid excerpts: artifact ID, body digest
  and digest of the exact source provenance. No new excerpt can be prepared from an erased capture. Existing
  excerpts still require their exact body/provenance, current task context and an unrevoked source job; deletion
  cannot make invalid evidence valid. Original source date, URL, partial flag and digests remain with each excerpt.
- Keep the approved itinerary, original result digest, runtime/resource identity, usage, release receipt and audit
  chain. A dated deletion record distinguishes missing content from lost output. Revocation still prevents an
  excerpt from completing a task or entering another task after raw capture deletion.
- The task UI explains exactly what is removed and retained, requires a checkbox, handles lost deletion replies,
  and links to surviving task results. The Usage screen exposes expiry policy even without an execution runtime.
  Browser refreshes reject obsolete responses so a stale read cannot overwrite a newer deletion view.

## Scope and storage implications

This deletes raw browser output from current application records. It is not whole-company erasure, deletion of
selected task results or their historical context copies, secure physical disk erasure, or deletion from existing
downloads/backups/database recovery files. Those distinctions are displayed before owner consent. Database WAL,
replicas and backup retirement require an operator storage lifecycle; this change does not truncate them, rewrite
the audit chain or claim that old backups have been purged. Restoring an old backup can restore older data and
policies; deletion reconciliation after restore remains required for full-company deletion support.

Migration 24 adds a company-owned browser retention policy with expiry off. Existing migration checksums and
browser job payloads remain compatible. Production browser qualification and activation remain separate work.
