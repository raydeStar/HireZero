# Company-owned private connections — 2026-10-25

Part of the full three-phase context-first product goal. Implements a private-connection boundary and UX
repair under [ADR 0062](../adr/0062-company-owned-private-connections.md); no complete phase is claimed.

## Finding and change

The host created separate company stores and tool registries but reused private provider instances,
settings and pasted secrets across them. Saving a connection activated it in every open company. Google
callback state held only a verifier/expiry, so callback handling used the default company store.

Each runtime now owns its Google Analytics/Search Console, Google Ads, OpenRouter and Microsoft 365
connections. New/hosted companies have separate configuration/credential directories, company-specific
encryption purposes and no ambient host-credential fallback. Existing local root connections stay only
with the original local company. Machine GitHub, Azure CLI, coding-app history, Git/process tools and
legacy Jev are unavailable in other companies until separate company-safe connectors exist.

One-time Google/Microsoft consent binds the company, initiating owner and settings version. Changes
invalidate pending consent; callbacks recheck ownership and return to that company's URL. Microsoft token
refresh cannot restore a disconnected account. Google caches bind all OAuth credentials and refuse use
after refresh-token removal. External credential removal is not falsely reported as successful app sign-out.

The page explains company ownership, Google has a sign-out control with a service-account fallback
explanation, callback results use plain text, and setup links can focus their corresponding form. Changing
Microsoft settings requires consent again. Unavailable machine GitHub sign-in is visibly disabled.

## Verification

- Company test project build: success, zero warnings/errors.
- 28 company tests passed in 64.134 s: six new isolation facts, existing integration setup and Microsoft
  company tests, plus all nine `CompanySuite` groups (security, HTTP, execution, handoffs, conversation,
  marketing, coding and integration paths). Those groups contain additional individual checks.
- Integration test project build: success, zero warnings/errors. Fourteen Google Analytics, Google Ads,
  Google sign-in and Microsoft tool-provider tests passed in 0.437 s.
- `company-ui.test.mjs` passed. Frontend production build passed.
- New `integration-isolation-browser-smoke.mjs` passed against an isolated Chrome profile and compiled fake
  Google provider: configure the first company, create another, prove no inheritance, save the second
  company's Google client, complete the real callback against the fake token endpoint, select a property,
  sign out, reload, and verify the first company's OpenRouter connection remains unchanged.
- Existing `settings-recovery-browser-smoke.mjs` passed: rejected/uncertain saves preserve edits, double
  submissions stay single, confirmed saves clear only submitted text, and company forms remain usable.
- The new browser journey asserted no horizontal overflow at 390 × 844. Its screenshot was inspected:
  sign-in control, scoped connection explanation and setup sections remain readable and reachable.
- Scoped Csharpier/Prettier formatting and `git diff --check` passed.

Compact logs/screenshots remain ignored under `test-results/integration-*`. Both browser fixtures cleaned
their disposable roots. The new fixture waits for its own processes to exit before deleting its verified
temporary directory. No user profile, real account, model, payment, deployment or external publication was used.

## Boundaries still open

Real consent/permission quality, hosted secret delivery and live provider revocation are unverified.
App disconnect prevents later reads but does not erase earlier evidence or revoke the upstream OAuth grant.
Already-dispatched reads may finish. Hosted upgrades require explicit company credential provisioning;
they do not adopt legacy root credentials automatically. Configuration activation after out-of-band delivery
requires startup or an owner configuration action.

This is not a complete sandbox or tenant model-BYOK audit. Developer execution/compute boundaries, broader
private connectors, paid entitlements and the remaining orchestration/product journeys are still in the
full acceptance ledger.
