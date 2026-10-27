# 0050: Company-scoped browser recovery

Status: Accepted. Extends [0021](0021-companies-per-data-root.md).

## Context

The browser previously read the shared company preference for every request. Changing companies in a second tab
could therefore send a command from an already-loaded screen to a different company. Hiring drafts and uncertain
operation IDs also used unscoped session-storage keys, so switching companies could expose the wrong draft or
block unrelated work. Older uncertain operations cannot safely be discarded or repeated during an upgrade.

## Decision

- Capture the company preference once when a document loads. Every API call and receipt lookup from that document
  keeps that company. Explicit company switching reloads the document; reload selects the saved preference.
- Store hiring drafts and uncertain command IDs under separate versioned keys containing that company ID. The
  first company uses `local-company`. A new uncertain operation in one company does not block another company.
- Retain uncertain command IDs in memory when browser storage is restricted. Clear only the matching ID after a
  confirmed response. Normal in-flight work remains cancelable; no uncertain command is automatically repeated.
- Retain the legacy unscoped command ID until a receipt confirms it in the current company. A missing receipt does
  not prove the operation failed elsewhere. Offer guidance to return to its original company and block new command
  submissions while that older outcome is unresolved. Do not probe other companies automatically.
- Automatically migrate an older hiring draft only when there is no current scoped draft and its saved employee,
  department and context references establish the current company. Every referenced saved record must match.
  Unanchored profile text requires an explicit choice to use it in the current company. An explicit receipt check
  can confirm a pending older draft in the current company without replaying the command. That check updates only
  the matching older draft; importing it into an open form is a separate choice. A delayed check cannot overwrite
  a newer current draft.
- Never replace an edited current draft during legacy recovery or a role shortcut. Remove an older draft only
  after writing its scoped replacement successfully. Restricted storage keeps the current form in memory and
  retains the older stored copy.

## Consequences

Open screens retain a stable request scope even when another tab changes its company preference. Returning to a
company restores that tab's draft and recovery state. A reload can select another tab's last chosen company, but
the loaded records, forms and outgoing requests then all use that newly selected company together.

Legacy data without a provable company needs an explicit recovery choice. Session storage remains local to a
browser tab; this decision does not introduce synchronization across tabs or durable server-side form drafts.
