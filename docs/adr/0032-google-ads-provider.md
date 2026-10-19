# 0032: Google Ads provider

Status: Accepted (Phase 4).

## Context

[ADR 0031](0031-ads-read-contracts.md) defined neutral `ads.*` Read contracts with fictional fixtures. The owner chose
Google Ads as the first real platform. Google Ads differs from GA4 in three ways that matter here:
- its API needs a developer token as well as OAuth;
- a service account cannot reach an ads account by itself in the usual setup;
- its only scope (`adwords`) is not read-only.

## Decision

- **A provider behind the same contracts.** `GoogleAdsProvider` in `HireZero.Integrations.Google` serves
  `ads.status` and `ads.performance`. Marketing is unchanged.
- **Read queries only, composed by the provider.** Each read is a GAQL `SELECT`, sent to `googleAds:search` for the
  configured customer.
  - Totals and the currency come from `customer`, and rows from `campaign` or by `segments.date`.
  - Dates and limits are inserted only after the contract's rules have checked them.
  - No caller supplies query text. No mutate endpoint is ever called.
  - Money arrives in micros and is converted to the account's currency, and statuses are lower-cased to the
    contract's values.
- **Read-only by who signs in.** The scope cannot be narrowed, so the guide asks for the refresh token to come from a
  Google Ads user with read-only access to the account. Even if HireZero's code were changed, that user cannot spend.
- **Refresh-token sign-in.** `GoogleOAuthRefresh` exchanges the refresh token at Google's token endpoint, and nowhere
  else, and caches the access token until shortly before it expires.
  - The developer token, client secret and refresh token are secrets read at use
    ([ADR 0027](0027-secrets-from-files.md)): `HIREZERO_GOOGLE_ADS_DEVELOPER_TOKEN`,
    `HIREZERO_GOOGLE_ADS_CLIENT_SECRET` and `HIREZERO_GOOGLE_ADS_REFRESH_TOKEN`.
  - The customer id, the optional manager (login) customer id and the OAuth client id are in
    `<data root>/google-ads.json`.
- **A version the owner can move.** Google retires Google Ads API versions about a year after release. The provider
  defaults to `v22`, and `google-ads.json` may name another version (`apiVersion`), so a sunset needs a settings
  change rather than a release.
- **Google first, fiction otherwise.** The host serves the `ads.*` tools from Google Ads when it is configured, and
  from the synthetic stand-in when `HIREZERO_SYNTHETIC_ADS=1` and Google is not.
- **Tested without Google.** A fake handler checks the sign-in, the headers (`developer-token`,
  `login-customer-id`), the query text and the parsing, using fictional responses. No check calls Google Ads.

## Consequences

An owner can review real Google Ads performance once a developer token with production access, an OAuth client and a
read-only user's refresh token are in place. API changes beyond a version bump (renamed fields) need a code change
and are caught only by a live connection, so the integration status names the version in use. Writes to Google Ads
remain a separate, approval-gated piece of work.
