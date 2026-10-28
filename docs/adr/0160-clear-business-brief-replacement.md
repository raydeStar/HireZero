# ADR 0160: Explain brief replacement before approval

Status: accepted

## Context

A new generated or edited draft can coexist with an approved business brief. The review screen offered approval
even though the store correctly refused it until the previous approval was explicitly withdrawn. The withdrawal
control was in a separate collapsed section, so the normal review path ended in an avoidable error.

## Decision

When a saved draft coexists with an approved brief, explain that employees still use the approved version. Disable
draft approval and provide a shortcut that opens and focuses the existing brief's review and withdrawal section.
Withdrawal remains a separate explicit action; the shortcut neither withdraws nor approves anything. Once the
previous approval is withdrawn, normal exact-version draft approval becomes available.

## Consequences

Owners can discover the replacement sequence without submitting a request that is known to fail. Existing
knowledge and task history remain intact until the owner acts. The server's concurrency and approval checks remain
authoritative; no atomic replacement, automatic revocation or wider approval is introduced.
