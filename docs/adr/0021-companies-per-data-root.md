# 0021: Companies per data root

Status: Accepted (Phase 3). Extends [0020](0020-members-roles-and-sign-in.md).

## Context

Every company table has always been keyed by company id, but the store used one constant id, and the host built one
store, one tool registry and one scheduler. Phase 3 tenancy needs several companies on one install, each with its
own members, work and audit, without a separate data root per company.

## Decision

- **Company-scoped stores.**
  - A `CompanyStore` opens one company by id over the shared database. Every query is scoped to that id, so a
    store never sees another company's rows.
  - The data root's first company keeps the id `local-company`.
  - Company ids are lowercase letters, digits and dashes; new ones are random.
- **A directory.** `co_company` lists the companies, each with its creator and a display name (a new schema step).
- **One runtime per company.** The host gives each company its own store, tool registry (audited in that company)
  and scheduler. Each is built on first use with the same tools and optional integrations; integration settings
  remain per data root.
- **Requests name their company.**
  - The `X-HireZero-Company` header selects the company; without it, the request acts in the first company.
  - Membership and role are checked in that company only, so the same person can be an owner in one company and a
    viewer in another.
  - An unknown company is refused.
- **Creating a company.** An owner of the current company can create another and becomes its first owner. Hosted
  sign-in binds a waiting invitation in whichever company issued it.

## Consequences

One install serves several companies, and nothing about a company's records changes. A per-company data root,
different integration settings per company, or moving a company between installs would each be a separate
decision.
