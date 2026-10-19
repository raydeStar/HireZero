# Phase 3 report: members, roles and sign-in

Date: 2026-10-18. Branch: `feature/employee-contexts`. Plan: [ROADMAP](../ROADMAP.md).
Decision: [ADR 0020](../adr/0020-members-roles-and-sign-in.md).

Until now every request was the one local owner. Real clients now have their own identities and roles.

## Choices

- **Deployment.** You run HireZero either on your own machine or hosted, and any real client reaches it over HTTPS.
  Local mode stays loopback-only, with you as the company's owner.
- **Sign-in.** Not every client has Microsoft Entra ID, so sign-in is generic OIDC against any standards-compliant
  provider, not one vendor.

## What changed

- **Members and roles** (slice 1):
  - each member of a company is an owner, approver or viewer;
  - the company's creator is its first owner, and an active owner always remains;
  - owners invite by email, and the first sign-in with that verified email binds the invitation to the
    provider's subject.
- **One authorization table** in Core (`CompanyAccess`):
  - owners alone change structure, budgets, checkout grants, integrations and members;
  - approvers run and approve the rest;
  - viewers only read.

  The role always comes from the membership, never from the request. Every company event records the member who
  caused it.
- **Hosted mode** (slice 2, `--company-hosted`):
  - an HTTPS public URL, served either by the host with a certificate or behind one named reverse proxy whose
    forwarded headers are the only ones believed;
  - sign-in uses the code flow with PKCE, and a client secret comes only from the environment;
  - the session is a `__Host-` cookie: HTTP-only, secure and same-site;
  - anyone not invited gets no session.

  Configuration is in the README's hosted-mode table.
- **Web** (slice 3): a sign-in screen in hosted mode, and a members panel. Owners invite, change roles and remove
  members there.

## Checks

- 155 .NET tests pass. The new ones cover:
  - each role running only what it allows;
  - invitations binding only on a verified email;
  - the always-an-owner rule;
  - the acting member recorded on events;
  - hosted options insisting on HTTPS.
- A full sign-in against an in-process fake OIDC provider, which checks PKCE and signs real ID tokens, covers:
  - the first owner being bound;
  - a stranger being refused;
  - a request not over the public HTTPS origin being refused;
  - sign-out.
- 11 frontend tests pass, along with the company launcher test and the web build, type check and format checks.
- Every headless browser smoke passed at least once. Several timed out intermittently waiting on the UI while a
  second session was building and testing on the same machine. One of them, the chat smoke, also failed with the
  web change removed, so these are load-sensitive flakes, not a regression from this work. They are worth hardening.

## Next

- **Multiple companies per data root.** Every company table is already keyed by company id, but the store, the
  scheduler and the tool registry are bound to one company today.
- **Tailnet multi-user access**, on the hosted identity model.
