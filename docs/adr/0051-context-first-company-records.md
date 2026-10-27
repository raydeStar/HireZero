# 0051: Context-first company records

Status: Accepted. Extends 0021, 0041 and 0043.

## Context

The owner adopted all three phases of the context-first product proposal. Source drafting currently starts after
hiring an employee and loses citations when its answer is copied into string fields. That cannot support business
confirmation, verified organizational memory or safe ongoing source maintenance.

## Decision

- Add role-neutral, company-scoped business identity, source snapshot and profile records in Core. Network reads,
  search and extraction remain behind integration/tool contracts; Core never fetches a website or knows a role.
- Business identity records include a name, optional official website, owner goal and optional disambiguating
  location/industry. The owner confirms an exact version/digest before public discovery imports records.
- Source snapshots are immutable evidence: final URL, retrieved time, bounded readable content and digest. They
  belong to one confirmed business identity revision and one company. A source is evidence, never authority.
- Proposed profile fields carry a verification class and citations containing source IDs and exact quoted
  excerpts. Uncited extracted facts are refused. Owner corrections carry separate owner provenance; unknowns and
  hypotheses are explicit. Source updates create proposals and never silently replace approved facts.
- Profile changes are versioned and reviewed by exact digest. Approval creates approved company knowledge through
  the existing storage and context boundary; drafts do not reach employees. Later source imports do not change it.
- Replacing an approved brief or changing its business identity requires the owner to explicitly revoke that exact
  brief first. Neither saving an identity nor approving another draft automatically retires existing approved facts.
- Public business research uses a separate `web.research` contract. Reads stay on the confirmed origin, check
  `robots.txt` before every page/redirect, enforce pacing and a session deadline, and fail closed on restricted or
  unavailable crawl policies. This does not establish permission under every site's terms or implement broad crawling.
- New tables use new migrations. Existing migration checksums, stored companies and earlier UX work are preserved.
- The delivery ledger covers all three product phases and field-validation requirements. This foundation does not
  imply that discovery, extraction, billing, sandboxes or customer acceptance are already complete.

## Consequences

The same provenance and confirmation boundary supports initial onboarding, corrections, retrieval and future
maintenance. Every new execution feature must reuse the existing approval, tenant and uncertain-outcome gates.
Fixtures prove mechanisms only; usefulness and paid retention remain evidence from real customers.
