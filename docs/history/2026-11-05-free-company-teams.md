# Free-company teams — 2026-11-05

Implemented and security-reviewed locally on `codex/free-company-teams`, based on the merged hosted app. No deployment, push, live
identity-provider request or model call. The invitation setting remains off by default. The decision and operating
rules are [ADR 0182](../adr/0182-consent-based-free-company-teams.md) and [SETTINGS](../SETTINGS.md#teams-in-free-companies).

## Delivered behavior

- Owners of free companies can issue, explicitly replace and revoke seven-day, email-bound invitation links for
  approvers/viewers. Five unexpired pending invitations are allowed; this is not an active-member cap.
- Only token hashes persist in the database. Issuance bypasses generic command-result storage, and URL fragments
  keep the secret out of request URLs. A protected 15-minute cookie carries the invitation through sign-in.
- The invitee reviews the company, role and account and explicitly chooses Join through a protected POST. Neither
  opening a link nor signing in accepts a free-company invitation. Wrong/unverified accounts, stale invitation or
  browser scopes, expiry, revocation and token reuse are refused.
- Membership, token consumption, verified email and audit event commit together. Concurrent joins have one winner;
  a failed audit write rolls everything back. Unknown browser outcomes are checked with reads, not automatically
  submitted again.
- Joining preserves the subject's one-free-company entitlement. The chooser exposes creation to eligible viewers
  and approvers, while ordinary creation still cannot bypass the signup ledger. Joining does not grant credit.
- Non-owners can leave from the joined banner or Members. Access ends and the audit remains. Turning the invitation
  flag off blocks issuance/acceptance while preserving existing access, revocation and leaving. The original owner
  remains; extra owners and ownership transfer are excluded.

## Verification

Free disk space was checked before builds and remained well above 10 GiB. All data and identities were fictional;
model execution was unavailable in the browser fixture. Temporary HTTPS certificates, browser profiles and data
roots were removed after their fixture processes exited.

| Check | Result |
| --- | --- |
| `dotnet build tests/HireZero.Company.Tests --no-restore -m:1 --nologo -v:q` | Passed, zero warnings/errors |
| Focused .NET suite below | 68 passed, 5 skipped, 0 failed |
| Migration, legacy-claim and integration-path checks below | 16 passed, 0 failed (includes two checks also in the focused suite) |
| `npm run build --prefix web` | Passed; Vite reported the large-bundle advisory |
| `node --test tests/company-ui.test.mjs tests/company-creation-ui.test.mjs tests/measured-ui.test.mjs` | 8 passed, 0 failed |
| `node tests/company-invites-browser-smoke.mjs` | Passed against a disposable HTTPS host with real browser cookies and APIs |
| `dotnet csharpier check .`, `npm run format:check --prefix web`, `git diff --check` | Passed |

The focused .NET command was:

```text
dotnet test --project tests/HireZero.Company.Tests --no-build --filter-class CompanyInvitationTests HostedSignInTests CompanySignupTests CompanySignupSpendTests CompanyMemberTests CompanyCreationTests CompanySettingsTests CompanyTenancyTests CompanyAuditTests StoreDatabaseTests CompanyNotificationTests LocalHostingTests
dotnet test --project tests/HireZero.Company.Tests --no-build --filter-method '*Migration*' '*LegacyClaims*' '*IntegrationPath'
```

The five skipped checks require a separately configured disposable PostgreSQL server. SQLite migration, storage,
atomicity, audit, tenancy, signup and spending checks ran. The hosted tests exercised authorization-code OIDC with
a local fictional issuer, including verified entry when enrollment is closed, protected cookies, expiration,
tamper rejection, explicit consent, account mismatch, another tab replacing an invitation, and rate limiting.

The browser journey exercised owner issuance, anonymous preview, wrong-account recovery, explicit viewer consent,
the joined banner, viewer creation of a separate owned company, shared allowance display, and a real host restart
with invitations disabled followed by a successful viewer departure. It also deliberately lost an acceptance
response after commit and verified one POST plus membership-read recovery, and checked same-page fragment changes.
It held an acceptance across a replacement-link review and verified that no second acceptance could overlap it or
stale error could overwrite the new review. Mixed-case and trailing-slash landing URLs were exercised as well.
Desktop and 390-pixel mobile renders were inspected; no horizontal overflow or uncaught UI exception was observed.
Screenshots are local ignored artifacts under `test-results/team-*.png`.

## Final review fixes

- Reproduced an issuance rate-limit bypass through a trailing slash. Rate limits now cover those route variants;
  rejected requests retain the usual no-store and security headers. Guest access uses explicit methods and paths,
  with case/trailing-slash handling consistent with routing, instead of exempting every path under `/api/join`.
- Reproduced an overlapping-submit window when a replacement fragment arrived during acceptance. The page now
  retains its in-flight guard across reviews and ignores old acceptance results when a newer review is displayed.
- The new schema table exposed outdated older-schema test fixtures. Updated their table cleanup without relaxing
  production migration checks, and verified version-31 upgrade preservation of members, audit, allowance and legacy
  invitations. All 16 selected migration/legacy/integration checks pass.

No remaining blockers were identified within the local review. Local commit is owner-authorized; production
deployment remains on hold.

## Deferred acceptance

Production deployment and live Google-account checks remain intentionally deferred. Disabling the setting is an
admission stop, not erasure of memberships or a database downgrade. Migration 32 is additive, but older binaries
refuse a newer migration ledger; retain this build with invitations off for the normal stop procedure.
