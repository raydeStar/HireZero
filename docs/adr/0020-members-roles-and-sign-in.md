# 0020: Members, roles and sign-in

Status: Accepted (Phase 3). Supersedes the single local owner as the only identity.

## Context

Until Phase 3 every request is the one local owner, on a loopback-only listener. Real users need their own identities
and sign-in, a company they belong to, and a role that limits what they can do. HireZero runs either on one
person's machine or hosted for real clients; any real client reaches it over HTTPS. Not every client has Microsoft
Entra ID, so sign-in must not depend on one provider.

## Decision

- **Members.** A company has members, each with one role:
  - **owner:** everything, including structure (departments, hiring, knowledge), budgets, checkout grants,
    integrations and members;
  - **approver:** reviews and approves artifacts, plans, meetings, runs and publications, and starts, runs and
    manages work and conversations;
  - **viewer:** reads only.

  A member is identified by a stable subject (the identity provider's issuer and subject), shown by email and name.
  A company always keeps at least one owner.
- **One authorization table.** Each company command and step endpoint declares its minimum role in Core
  (`CompanyAccess`). The command path checks the acting member's role before anything changes. A refusal names
  the role it needs and changes nothing.
- **Every change has an actor.** The acting member is recorded on each company event, which feeds the Phase 3
  audit log.
- **Two ways to sign in:**
  - **Local owner** (unchanged): the loopback-only, single-person mode. Its identity is the company's first
    owner.
  - **Generic OIDC:** the authorization code flow with PKCE against any standards-compliant provider (authority,
    client id and scopes in configuration; a client secret only from the environment). The session is a secure,
    HTTP-only, same-site cookie. A signed-in user with no membership sees nothing. An owner invites by email, and
    the first sign-in with that verified email becomes the member.
- **HTTPS beyond loopback.** Listening on anything but loopback requires HTTPS, either Kestrel's own certificate or
  an explicitly trusted reverse proxy.
- **Companies.** Every company table is already keyed by company id. One data root holds several companies, and
  a request acts in one company its member belongs to.

## Consequences

The local single-person mode keeps working unchanged, as the owner of its company. Hosted installs add OIDC
configuration and HTTPS. Provider-specific features, such as Entra app roles or group mapping, can be added later
on the generic path. Owner approvals in earlier ADRs now mean an approval by a member whose role allows it.
