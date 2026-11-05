# 0181 — Open sign-up and durable free model allowances

Status: accepted, 2026-11-05. Implements Brief B of [0179](0179-hosted-hirezero-on-vultr.md);
extends the invitation-only entry rule in [0020](0020-members-roles-and-sign-in.md) and company creation in
[0021](0021-companies-per-data-root.md).

## Decision

Hosted open sign-up is an explicit operator opt-in. OIDC still verifies the issuer/subject and email. With the
setting enabled, a verified stranger receives an authenticated session that can list their own memberships and
explicitly create one company. Other company endpoints still demand current membership. The existing public
origin, JSON/CSRF and stale-member checks apply to enrollment. Invitation-only and local installs keep their
existing entry behavior.

Migration 31 adds a subject-unique sign-up ledger, the lifetime cap saved for each new company, a durable install
policy, and company/dispatch-unique spending receipts. Company insertion, first-owner membership, verified email,
audit event and sign-up slot commit together under the database's shared schema transaction scope. Repeated
creation returns the same accessible company; it never reactivates a revoked member. Sign-up subjects and sign-up
companies cannot use ordinary owner creation to obtain additional companies. The install maximum counts all
sign-up rows, independently of current memberships.

Model requests pass through the company model connection. Before transport, one transaction reserves the API
worst case against both lifetime and shared UTC-day limits. The full measured-policy output target is conservative
when a vendor requests a smaller low-effort target. All companies, including pre-existing companies, consume the
shared daily limit. Usage settles using the captured reviewed price or a reported bill, without rounding each
request to whole cents. Complete replies, withheld tool/truncated replies and known zero-usage refusals settle;
unknown outcomes, missing/inconsistent usage and accounting faults retain the reservation. Existing token
accounting and provider refusal behavior remain authoritative and unchanged. A retained or settled dispatch ID
cannot send again, even after restart. Every reservation, settled or not, belongs to its dispatch day: an uncertain
request cost at most its worst case on that day, so it does not reduce later days' limits, while it stays on its
company's lifetime cap for good. Sign-up companies cannot invite members, and a pending invite never adds a
sign-in to one, so a stranger's company cannot claim another person's account. Disabling enrollment never removes the spending policy.

Only priced, bounded host API work can use this allowance. Capped installs refuse Codex and saved company keys,
and disable separate Jev decision and model-tool transports that cannot reserve a worst case through this boundary.
This does not implement another provider or another decision transport. Provider routing remains Brief A.

Signed-up companies can hire text roles, Marketing first; workspace/coding roles are excluded. Their tool registry
has public-page reading only, with no data-root search key, synthetic data or shared integration. Connection/setup
routes, company model keys and billing are unavailable; outgoing owner notices are not wired to these companies.
The allowance is shown in dollars using the existing allowance module. Explicit owner approval for work remains
required; a free allowance does not authorize a model request by itself.

## Verification and limits

Focused offline facts cover verified OIDC entry, disabled/local behavior, concurrent enrollment, the maximum,
company isolation, ordinary-creation refusal, both spending limits before HTTP, usage settlement, withheld tools,
known refusals, duplicate dispatch prevention and unknown holds across restart. HTTP model handlers and
OIDC providers are fictional. Live Google/Vultr, deployment and customer acceptance remain separate work.
