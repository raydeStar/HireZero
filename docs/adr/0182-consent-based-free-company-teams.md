# 0182 — Consent-based teams in free companies

Status: accepted, 2026-11-05. Owner-authorized local implementation; deployment is deferred.
Extends [0181](0181-open-signup-and-durable-spend-caps.md) and [0020](0020-members-roles-and-sign-in.md).

## Decision

`HIREZERO_SIGNUP_INVITES` is a hosted-only setting, off by default. It gates issuance and acceptance, not access for
existing members, owner revocation or non-owner departure. Free-company owners invite approvers or viewers; the
original owner cannot be removed or demoted and additional owners cannot be appointed. Other companies retain
their existing invitation behavior.

An invitation contains a random 256-bit secret, with only its SHA-256 digest stored in migration 32's invitation
table. It is email-bound, expires after seven days, and is replaced explicitly with optimistic version checking.
At most five unexpired pending invitations may coexist per company; active membership has no new numeric limit.
Invitation issuance uses a dedicated owner-only endpoint so the one-time secret is never serialized into generic
command receipts. No invitation email is sent. Lost responses are checked through Members; replacing a link requires
another owner action and invalidates the preceding secret. Revocation and pending-role changes invalidate links.

The link is `/join#<secret>`: URL fragments are not sent to the HTTP server or identity provider. The UI removes
the fragment and submits it once through the existing same-origin JSON/custom-header boundary. A validated link
creates a protected, Secure, HttpOnly, host-only, SameSite=Lax cookie lasting 15 minutes. The page shows the company,
role and current account. Sign-in may establish a verified, membership-free session for a valid invitation even
when public enrollment is disabled, but it does not activate membership. The user explicitly chooses **Join**.

Acceptance requires a protected POST, matching browser-member scope, matching invitation fingerprint and reviewed
membership version, an unexpired unused token, and the invited verified email. Another tab cannot substitute an
unreviewed invitation. Token use, membership, verified-email record and audit event commit in one company transaction.
The cookie is cleared on success. A lost acceptance response directs the user to read their company list; it never
automatically repeats the POST. Join routes and issuance are limited to 30 requests per client IP per minute after
trusted-proxy normalization. Invalid input, wrong accounts and GET requests never consume an invitation.

Signup eligibility belongs solely to the subject-unique `co_signups` ledger, independent of memberships. The host
and UI expose **Create my company** to eligible signed-in teammates. Ordinary creation remains blocked for signup
companies and subjects; this flow cannot mint an uncapped company. Joining never adds allowance. Existing company
and install spending reservations remain the accounting boundary.

`member.leave` is available to free-company viewers and approvers, acts only on the current actor, revokes access
and records an audit event. It does not erase existing work or identity history. A new deliberate invitation is
required to restore a departed member. The joined banner names the company and offers Leave; Members retains Leave
after the banner is dismissed.

## Rollout and limits

Disabling invitations is a stop switch, not a rollback of memberships or schema. Keep the new build with the flag
off if issuance must stop. Older binaries refuse the newer migration ledger; a binary downgrade needs a separately
planned database restore. Do not erase signup or spend receipts. Existing caps persist when enrollment is disabled.

Offline verification covers the store and hosted fake-OIDC boundaries and browser behavior with fictional data.
Live Google and production deployment remain separate acceptance work. No deployment is authorized by this ADR.
